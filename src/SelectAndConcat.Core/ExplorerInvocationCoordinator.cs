using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

namespace SelectAndConcat.Core;

public static class ExplorerInvocationCoordinator
{
    private const string MutexName = @"Local\SelectAndConcat.ExplorerInvoke";
    private const string PipeName = "SelectAndConcat.ExplorerPipe";
    private static readonly TimeSpan FollowerConnectTimeout = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan LeaderIdleWindow = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan LeaderMaxCollectionWindow = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan LeaderTotalLifespanWindow = TimeSpan.FromMilliseconds(2500);

    private static Mutex? s_leaderMutex;
    private static long s_leaderStartedTicks;

    public static IReadOnlyList<string>? CollectOrForward(IReadOnlyList<string> initialPaths)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var isLeader);
        if (!isLeader)
        {
            mutex.Dispose();
            ForwardToLeader(initialPaths);
            return null;
        }

        s_leaderMutex = mutex;
        s_leaderStartedTicks = Environment.TickCount64;

        return CollectAsLeader(initialPaths);
    }

    public static void ReleaseLeader()
    {
        if (s_leaderMutex is null)
        {
            return;
        }

        try
        {
            var elapsed = Environment.TickCount64 - s_leaderStartedTicks;
            var remaining = (long)LeaderTotalLifespanWindow.TotalMilliseconds - elapsed;
            if (remaining > 0)
            {
                Thread.Sleep((int)remaining);
            }
        }
        finally
        {
            s_leaderMutex.Dispose();
            s_leaderMutex = null;
        }
    }

    private static IReadOnlyList<string> CollectAsLeader(IReadOnlyList<string> initialPaths)
    {
        var collected = new ConcurrentBag<string>(initialPaths);
        using var cts = new CancellationTokenSource();
        var lastReceivedTicks = Environment.TickCount64;

        var listenerTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cts.Token);
                    Interlocked.Exchange(ref lastReceivedTicks, Environment.TickCount64);

                    _ = Task.Run(async () =>
                    {
                        using (server)
                        using (var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false))
                        {
                            try
                            {
                                string? line;
                                while ((line = await reader.ReadLineAsync(cts.Token)) != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(line))
                                    {
                                        collected.Add(line);
                                        Interlocked.Exchange(ref lastReceivedTicks, Environment.TickCount64);
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }
                    }, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(10, cts.Token);
                }
            }
        });

        var collectionStart = DateTime.UtcNow;
        while (DateTime.UtcNow - collectionStart < LeaderMaxCollectionWindow)
        {
            Thread.Sleep(50);
            var idleMs = Environment.TickCount64 - Interlocked.Read(ref lastReceivedTicks);
            if (idleMs >= (long)LeaderIdleWindow.TotalMilliseconds)
            {
                break;
            }
        }

        cts.Cancel();

        return collected.ToList();
    }

    private static void ForwardToLeader(IReadOnlyList<string> paths)
    {
        var deadline = DateTime.UtcNow + FollowerConnectTimeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(timeout: 150);

                using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: false)
                {
                    AutoFlush = true
                };

                foreach (var path in paths)
                {
                    writer.WriteLine(path);
                }

                return;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }

            Thread.Sleep(25);
        }
    }
}
