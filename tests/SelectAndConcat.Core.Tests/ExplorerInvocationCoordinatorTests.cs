namespace SelectAndConcat.Core.Tests;

public sealed class ExplorerInvocationCoordinatorTests
{
    [Fact]
    public async Task CollectOrForward_AggregatesLeaderAndFollowerPaths()
    {
        var leaderPaths = new[] { "C:\\test\\leader.txt" };
        var followerPaths1 = new[] { "C:\\test\\follower1.txt" };
        var followerPaths2 = new[] { "C:\\test\\follower2.txt" };

        IReadOnlyList<string>? collected = null;

        var leaderTask = Task.Run(() =>
        {
            try
            {
                collected = ExplorerInvocationCoordinator.CollectOrForward(leaderPaths);
            }
            finally
            {
                ExplorerInvocationCoordinator.ReleaseLeader();
            }
        });

        // Small delay to ensure leader initializes named pipe server
        await Task.Delay(100);

        var follower1Task = Task.Run(() =>
        {
            var res = ExplorerInvocationCoordinator.CollectOrForward(followerPaths1);
            Assert.Null(res);
        });

        var follower2Task = Task.Run(() =>
        {
            var res = ExplorerInvocationCoordinator.CollectOrForward(followerPaths2);
            Assert.Null(res);
        });

        await Task.WhenAll(follower1Task, follower2Task);
        await leaderTask;

        Assert.NotNull(collected);
        Assert.Equal(3, collected.Count);
        Assert.Contains("C:\\test\\leader.txt", collected);
        Assert.Contains("C:\\test\\follower1.txt", collected);
        Assert.Contains("C:\\test\\follower2.txt", collected);
    }
}
