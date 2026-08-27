using AppGeek.Models;

namespace AppGeek.Services;

/// <summary>
/// Registers (or removes) the Windows scheduled task that runs AppGeek's background scan.
///
/// schtasks.exe rather than the TaskScheduler COM library, for the same reason the app shells
/// out to winget: it keeps the project free of third-party packages and COM interop, and the
/// command line is something a support request can be asked to run by hand.
///
/// The task only ever scans. It passes <see cref="ScanSchedule.ScanArgument"/>, and that code
/// path cannot install anything — see App.OnStartup.
/// </summary>
public static class ScheduledScanService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Where the running AppGeek actually lives, for the task's action.</summary>
    public static string ExecutablePath =>
        Environment.ProcessPath ??
        System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ??
        "";

    public sealed record Outcome(bool Ok, string Message);

    /// <summary>
    /// Brings the scheduled task into line with the settings. Called whenever Settings is
    /// saved, so switching the dropdown to "Manually only" removes the task rather than
    /// leaving an orphan behind.
    /// </summary>
    public static async Task<Outcome> ApplyAsync(AppSettings settings, CancellationToken ct = default)
    {
        var plan = ScanSchedule.Parse(settings.ScanSchedule);

        if (!settings.AutoScan || !plan.NeedsScheduledTask)
        {
            var removed = await RemoveAsync(ct).ConfigureAwait(false);
            var why = !settings.AutoScan
                ? "Automatic scanning is off"
                : plan.Kind == ScanScheduleKind.AtStartup
                    ? "Scanning at startup needs no scheduled task"
                    : "No schedule is set";
            Log.Info($"Scheduled scan: no task required — {why.ToLowerInvariant()}.");
            return new Outcome(true, removed.Ok ? why + "." : why + ", and no task was registered.");
        }

        var exe = ExecutablePath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Log.Warn("Scheduled scan: AppGeek could not work out its own path, so no task was created.");
            return new Outcome(false, "AppGeek could not work out where it is installed.");
        }

        var args = ScanSchedule.BuildCreateArguments(plan, exe);
        Log.Info($"Scheduled scan: schtasks {args}");

        try
        {
            var result = await ProcessRunner.RunAsync("schtasks.exe", args, ct, timeout: Timeout)
                                            .ConfigureAwait(false);
            if (result.Success)
            {
                Log.Info($"Scheduled scan registered — {plan.Describe()}.");
                return new Outcome(true, plan.Describe() + ".");
            }

            Log.Warn($"Scheduled scan could not be registered (exit 0x{result.ExitCode:X8}): {result.Combined.Trim()}");
            return new Outcome(false, Explain(result.ExitCode));
        }
        catch (Exception ex)
        {
            Log.Error("Scheduled scan registration threw", ex);
            return new Outcome(false, ex.Message);
        }
    }

    public static async Task<Outcome> RemoveAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("schtasks.exe", ScanSchedule.BuildDeleteArguments(),
                                                     ct, timeout: Timeout).ConfigureAwait(false);
            // A task that was never there is not a failure worth surfacing.
            return new Outcome(result.Success, result.Success ? "Scheduled scan removed." : "No scheduled scan was registered.");
        }
        catch (Exception ex)
        {
            Log.Warn("Scheduled scan could not be removed: " + ex.Message);
            return new Outcome(false, ex.Message);
        }
    }

    /// <summary>True when Windows currently holds a task by our name.</summary>
    public static async Task<bool> IsRegisteredAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("schtasks.exe", ScanSchedule.BuildQueryArguments(),
                                                     ct, timeout: Timeout).ConfigureAwait(false);
            return result.Success;
        }
        catch { return false; }
    }

    private static string Explain(int exitCode) => unchecked((uint)exitCode) switch
    {
        0x00000001 => "Windows refused the task. Administrator rights are needed to create it.",
        0x00000005 => "Access denied creating the scheduled task.",
        _ => $"schtasks returned 0x{exitCode:X8}. The details are in the log."
    };
}
