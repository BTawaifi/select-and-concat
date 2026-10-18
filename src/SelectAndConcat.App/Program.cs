using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SelectAndConcat.Core;

namespace SelectAndConcat.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "--from-explorer", StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Invalid invocation. Expected: SelectAndConcat.exe --from-explorer <path1> <path2> ...");
            return (int)ProcessExitCode.Failure;
        }

        try
        {
            var coordinatedPaths = ExplorerInvocationCoordinator.CollectOrForward(args[1..]);
            if (coordinatedPaths is null)
            {
                return ExplorerInvocationCoordinator.ForwardSucceeded
                    ? (int)ProcessExitCode.Success
                    : (int)ProcessExitCode.Failure;
            }

            var processor = new SelectionProcessor();
            var redact = Environment.GetEnvironmentVariable("SELECTANDCONCAT_REDACT") != "0";
            var result = processor.Process(coordinatedPaths, new ProcessingOptions { RedactSecrets = redact });

            if (result.Included.Count == 0)
            {
                ShowError(BuildFailureMessage(result));
                return (int)result.ExitCode;
            }

            if (!TrySetClipboardText(result.ClipboardPayload))
            {
                ShowError("Unable to access clipboard after multiple attempts.");
                return (int)ProcessExitCode.Failure;
            }

            // Free the leader slot before any UI so new selections aren't dropped while a dialog is open.
            ExplorerInvocationCoordinator.ReleaseLeader();

            if (result.ExitCode == ProcessExitCode.Success)
            {
                ShowSuccessNotification(result.Included.Count, result.RedactedCount);
            }
            else if (result.ExitCode == ProcessExitCode.PartialSuccess)
            {
                ShowPartialOutcome(result);
            }

            return (int)result.ExitCode;
        }
        catch (Exception ex)
        {
            ShowError($"Unexpected error: {ex.Message}");
            return (int)ProcessExitCode.Failure;
        }
        finally
        {
            ExplorerInvocationCoordinator.ReleaseLeader();
        }
    }

    private static bool TrySetClipboardText(string text)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (Exception ex) when (ex is ExternalException or COMException)
            {
                if (attempt == 5)
                {
                    return false;
                }

                Thread.Sleep(50 * attempt);
            }
        }

        return false;
    }

    private static void ShowSuccessNotification(int count, int redacted)
    {
        try
        {
            using var notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                BalloonTipTitle = "Select-and-Concat",
                BalloonTipText = $"Copied {count} file(s) to clipboard." + (redacted > 0 ? $" {redacted} secret(s) redacted." : string.Empty),
                BalloonTipIcon = ToolTipIcon.Info
            };
            notifyIcon.ShowBalloonTip(1500);
            Thread.Sleep(800);
        }
        catch
        {
        }
    }

    private static void ShowPartialOutcome(ProcessResult result)
    {
        var lines = new List<string>
        {
            $"Copied {result.Included.Count} file(s) to clipboard.",
            $"Skipped: {result.Skipped.Count}",
            $"Truncated: {result.TruncatedCount}",
            $"Secrets redacted: {result.RedactedCount}"
        };

        if (result.Skipped.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Skip summary:");
            foreach (var group in result.Skipped
                         .GroupBy(x => x.Reason)
                         .OrderByDescending(x => x.Count())
                         .Take(6))
            {
                lines.Add($"- {group.Key} ({group.Count()})");
            }
        }

        MessageBox.Show(string.Join(Environment.NewLine, lines), "Select-and-Concat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "Select-and-Concat", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string BuildFailureMessage(ProcessResult result)
    {
        if (result.Skipped.Count == 0)
        {
            return "No files were copied.";
        }

        var lines = new List<string>
        {
            "No files were copied.",
            string.Empty,
            "Reasons:"
        };

        foreach (var group in result.Skipped
                     .GroupBy(x => x.Reason)
                     .OrderByDescending(x => x.Count())
                     .Take(8))
        {
            lines.Add($"- {group.Key} ({group.Count()})");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
