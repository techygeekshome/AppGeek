using System.Diagnostics;
using System.Text;
using AppGeek.Models;

namespace AppGeek.Services;

/// <summary>What to do when a cancellation or timeout arrives while a process is running.</summary>
public enum ProcessAbortPolicy
{
    /// <summary>
    /// Kill the process and its children. Only ever safe for read-only queries such as
    /// "winget list", where nothing is being written to disk.
    /// </summary>
    KillProcessTree,

    /// <summary>
    /// Never kill. Wait for the process to finish on its own, however long it takes.
    ///
    /// This is mandatory for anything that installs, upgrades or uninstalls software.
    /// Killing an installer's process tree part-way through is how a machine ends up with
    /// the old version already removed, the new one not yet written, and shortcuts left
    /// pointing at an executable that no longer exists.
    /// </summary>
    NeverKill
}

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string Combined => string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdOut + Environment.NewLine + StdErr;
}

public static class ProcessRunner
{
    /// <summary>Runs a console process, capturing output. Never throws for a non-zero exit code.</summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken ct = default,
        Action<string>? onOutputLine = null,
        TimeSpan? timeout = null,
        ProcessAbortPolicy abortPolicy = ProcessAbortPolicy.KillProcessTree)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            if (onOutputLine is not null)
            {
                foreach (var piece in WingetText.SplitProgressLine(e.Data))
                    onOutputLine(piece);
            }
        };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", ex.Message);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (abortPolicy == ProcessAbortPolicy.NeverKill)
        {
            // Deliberately ignores both the cancellation token and the timeout. The caller
            // decides not to start the NEXT piece of work; it does not get to interrupt
            // this one. A long install is slow, not broken — and half-installing software
            // does far more damage than waiting does.
            var warnAfter = timeout ?? TimeSpan.FromMinutes(30);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var nextHeartbeat = TimeSpan.FromMinutes(1);

            while (!proc.HasExited)
            {
                await Task.WhenAny(proc.WaitForExitAsync(CancellationToken.None),
                                   Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);

                if (proc.HasExited) break;

                // A heartbeat every minute, on purpose.
                //
                // Without one, a log that stops part-way through an install gives no way to
                // tell whether the app crashed, hung, or was killed from Task Manager by
                // someone who thought it had frozen. A heartbeat answers that outright, and
                // the same line reassures anyone watching the log that work is in progress.
                if (watch.Elapsed >= nextHeartbeat)
                {
                    Log.Info($"Still running: '{fileName}' after {Format.Duration(watch.Elapsed)}. " +
                             "A silent installer can take a long time — do not close AppGeek.");
                    nextHeartbeat += TimeSpan.FromMinutes(1);
                }

                if (watch.Elapsed > warnAfter)
                {
                    Log.Warn($"'{fileName}' has been running for {watch.Elapsed.TotalMinutes:0} minutes. " +
                             "Still waiting — an installer is never killed part-way through.");
                    warnAfter += TimeSpan.FromMinutes(15);
                }
            }

            Log.Info($"'{fileName}' finished after {Format.Duration(watch.Elapsed)}.");

            await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return new ProcessResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is not null) linked.CancelAfter(timeout.Value);

        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Safe only because this path is reserved for read-only queries.
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(-1, stdout.ToString(), "Cancelled or timed out.");
        }

        return new ProcessResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
