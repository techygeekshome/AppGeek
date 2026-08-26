using System.Collections.ObjectModel;
using System.Diagnostics;
using AppGeek.Models;

// System.Diagnostics also defines an ActivityKind (distributed tracing); ours wins here.
using ActivityKind = AppGeek.Models.ActivityKind;

namespace AppGeek.Services;

public sealed record RunSummary(int Succeeded, int Failed, int Skipped, bool Cancelled,
                               TimeSpan Elapsed, string LogPath, RebootPlan Reboot)
{
    public int Total => Succeeded + Failed + Skipped;

    /// <summary>Nothing failed, nothing was skipped and the user did not stop it.</summary>
    public bool WasClean => Failed == 0 && Skipped == 0 && !Cancelled;
}

/// <summary>
/// Executes a queue of install/update jobs one at a time, streaming progress.
/// Sequential by design: parallel MSI operations fail with ERROR_INSTALL_ALREADY_RUNNING,
/// and users care more about a readable log than shaving a minute off the run.
/// </summary>
public sealed class InstallRunner
{
    private readonly WingetClient _winget;
    private readonly SettingsService _settings;
    private readonly ActivityService _activity;
    private CancellationTokenSource? _cts;

    public InstallRunner(WingetClient winget, SettingsService settings, ActivityService activity)
    {
        _winget = winget;
        _settings = settings;
        _activity = activity;
    }

    public ObservableCollection<RunItem> Items { get; } = new();
    public bool IsRunning { get; private set; }
    public string? CurrentLogPath { get; private set; }

    public event Action<RunItem>? ItemChanged;
    public event Action<string>? LogLine;
    public event Action<RunSummary>? Completed;

    public void Cancel() => _cts?.Cancel();

    public async Task<RunSummary> RunAsync(IEnumerable<RunItem> items)
    {
        if (IsRunning) throw new InvalidOperationException("A run is already in progress.");

        Items.Clear();
        foreach (var i in items) Items.Add(i);

        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        CurrentLogPath = Log.BeginRunLog();
        var stopwatch = Stopwatch.StartNew();
        int ok = 0, failed = 0, skipped = 0;
        bool cancelled = false;
        bool rebootRequested = false;

        Log.Info($"Run started · {Items.Count} package(s) · elevated as {Elevation.CurrentUserName}");
        Emit($"Run started · {Items.Count} package(s) · elevated as {Elevation.CurrentUserName}");

        if (_settings.Current.CreateRestorePoint)
            await TryCreateRestorePointAsync(ct).ConfigureAwait(false);

        foreach (var item in Items)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }

            var itemWatch = Stopwatch.StartNew();
            item.State = RunItemState.Running;
            item.Detail = "Resolving package…";
            ItemChanged?.Invoke(item);

            try
            {
                if (item.Action == RunAction.Update)
                {
                    Emit($"{item.PackageId}: {InstallScopePolicy.Describe(item.Scope)} install" +
                         (string.IsNullOrWhiteSpace(item.InstallLocation)
                             ? "" : $" at {item.InstallLocation}"));
                }

                if (!await ClearRunningAppAsync(item, ct).ConfigureAwait(false))
                {
                    item.State = RunItemState.Skipped;
                    skipped++;
                    ItemChanged?.Invoke(item);
                    continue;
                }

                var result = await _winget.ExecuteAsync(
                    item.Action, item.PackageId, item.SourceName, item.Scope,
                    (line, pct) =>
                    {
                        if (pct is not null) item.Percent = pct.Value;
                        if (!string.IsNullOrWhiteSpace(line) && line.Length > 3)
                        {
                            item.Detail = line.Length > 120 ? line[..120] : line;
                            Emit($"{item.PackageId}: {item.Detail}");
                        }
                        ItemChanged?.Invoke(item);
                    },
                    ct).ConfigureAwait(false);

                itemWatch.Stop();
                item.Elapsed = itemWatch.Elapsed;
                item.ExitCode = result.ExitCode;

                var neededReboot = RebootDecision.IsRebootExitCode(result.ExitCode);
                if (neededReboot) rebootRequested = true;

                if (result.ExitCode == 0 || neededReboot)
                {
                    item.State = RunItemState.Succeeded;
                    item.Percent = 100;
                    item.Detail = item.Action == RunAction.Update
                        ? $"Updated {item.FromVersion} → {item.ToVersion} · {Format.Duration(item.Elapsed)}"
                        : $"Installed {item.ToVersion} · {Format.Duration(item.Elapsed)}";
                    if (neededReboot) item.Detail += " · needs a restart to finish";
                    ok++;
                    Emit($"{item.PackageId}: {item.Detail}");
                    _activity.Add(ActivityKind.Success,
                        item.Action == RunAction.Update
                            ? $"Updated {item.Name} to {item.ToVersion}"
                            : $"Installed {item.Name} {item.ToVersion}");
                }
                else if (ct.IsCancellationRequested)
                {
                    item.State = RunItemState.Cancelled;
                    item.Detail = "Cancelled";
                    cancelled = true;
                }
                else
                {
                    item.State = RunItemState.Failed;
                    item.Detail = WingetClient.DescribeExitCode(result.ExitCode, item.Scope);
                    failed++;
                    Log.Error($"{item.PackageId} failed: {item.Detail}");

                    // The whole point of a log is being able to see what actually happened.
                    // Exit codes alone are not enough to diagnose a broken install.
                    Log.Error($"{item.PackageId} exit code 0x{result.ExitCode:X8}. winget output follows:");
                    foreach (var line in LastLines(result.Combined, 40))
                        Log.Error($"    {line}");

                    Emit($"{item.PackageId}: FAILED — {item.Detail}");
                    _activity.Add(ActivityKind.Failure, $"{item.Name} failed — {item.Detail}");
                }
            }
            catch (OperationCanceledException)
            {
                item.State = RunItemState.Cancelled;
                item.Detail = "Cancelled";
                cancelled = true;
            }
            catch (Exception ex)
            {
                item.State = RunItemState.Failed;
                item.Detail = ex.Message;
                failed++;
                Log.Error($"{item.PackageId} threw", ex);
            }

            ItemChanged?.Invoke(item);
        }

        // Anything still queued when we stopped counts as skipped.
        foreach (var i in Items.Where(i => i.State == RunItemState.Queued))
        {
            i.State = RunItemState.Skipped;
            i.Detail = cancelled ? "Skipped — run stopped" : "Skipped";
            skipped++;
            ItemChanged?.Invoke(i);
        }

        stopwatch.Stop();

        var reboot = DecideReboot(rebootRequested, clean: failed == 0 && skipped == 0 && !cancelled);
        var summary = new RunSummary(ok, failed, skipped, cancelled, stopwatch.Elapsed,
                                     CurrentLogPath ?? "", reboot);

        Log.Info($"Run finished · {ok} succeeded, {failed} failed, {skipped} skipped · {Format.Duration(stopwatch.Elapsed)}");
        Emit($"Run finished · {ok} succeeded, {failed} failed, {skipped} skipped");
        Log.EndRunLog();

        IsRunning = false;
        _cts?.Dispose();
        _cts = null;

        Completed?.Invoke(summary);
        return summary;
    }

    /// <summary>
    /// Applies the "When an app is running" setting to one item. Returns false when the item
    /// should be skipped.
    ///
    /// This sits in the runner rather than in the Updates screen on purpose, so a catalogue
    /// install gets the same treatment as an update, and so the check happens immediately
    /// before the install rather than minutes earlier when the queue was built.
    /// </summary>
    private async Task<bool> ClearRunningAppAsync(RunItem item, CancellationToken ct)
    {
        var policy = _settings.Current.RunningAppPolicy;
        if (policy == RunningAppPolicy.Ask) return true;   // already answered on the Updates screen

        var process = RunningProcessDetector.FindRunning(item.PackageId, item.Name);
        var action = RunningAppGate.Decide(policy, process is not null);

        switch (action)
        {
            case RunningAppAction.Skip:
                item.Detail = $"Skipped — {item.Name} is running ({process})";
                Log.Info($"{item.PackageId} skipped: {process} is running and the policy is 'Always skip'.");
                Emit($"{item.PackageId}: skipped — {process} is running");
                _activity.Add(ActivityKind.Warning, $"{item.Name} skipped — it was open");
                return false;

            case RunningAppAction.CloseFirst:
                item.Detail = $"Asking {process} to close…";
                ItemChanged?.Invoke(item);
                Emit($"{item.PackageId}: {process} is running — asking it to close");

                var outcome = await RunningAppCloser.TryCloseAsync(process, ct: ct).ConfigureAwait(false);
                if (outcome == CloseOutcome.StillRunning)
                {
                    item.Detail = $"Skipped — {process} would not close";
                    Log.Warn($"{item.PackageId} skipped: {process} did not close within the grace period.");
                    Emit($"{item.PackageId}: skipped — {process} did not close");
                    _activity.Add(ActivityKind.Warning, $"{item.Name} skipped — it would not close");
                    return false;
                }

                Emit($"{item.PackageId}: {process} closed");
                return true;

            default:
                return true;
        }
    }

    /// <summary>
    /// Works out whether to restart, and acts on it if the user chose Automatic. The prompt
    /// case is left to the UI — a service has no business putting a dialog on screen.
    /// </summary>
    private RebootPlan DecideReboot(bool installerRequestedReboot, bool clean)
    {
        bool pending;
        try { pending = RebootProbe.IsRebootPending(); }
        catch (Exception ex) { Log.Warn("Reboot probe failed: " + ex.Message); pending = false; }

        var plan = RebootDecision.Decide(_settings.Current.RebootPolicy, pending,
                                         installerRequestedReboot, clean);

        if (plan.Action == RebootAction.None)
        {
            if (pending || installerRequestedReboot)
                Emit("A restart is pending, and your reboot setting is 'Never reboot' — nothing has been restarted.");
            return plan;
        }

        Emit($"A restart is needed: {plan.Reason}.");

        if (plan.Action == RebootAction.Automatic)
        {
            Emit($"Restarting in {plan.Delay.TotalMinutes:0} minute(s). Run 'shutdown /a' in a command prompt to cancel.");
            _activity.Add(ActivityKind.Warning, "Automatic restart scheduled after the run");
            RebootProbe.Restart(plan.Delay, plan.Reason);
        }

        return plan;
    }

    private async Task TryCreateRestorePointAsync(CancellationToken ct)
    {
        if (!Elevation.IsElevated)
        {
            Emit("Restore point skipped — needs administrator rights.");
            return;
        }

        Emit("Creating a system restore point…");
        try
        {
            var r = await ProcessRunner.RunAsync(
                "powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                "\"Checkpoint-Computer -Description 'AppGeek run' -RestorePointType MODIFY_SETTINGS\"",
                ct, timeout: TimeSpan.FromMinutes(3)).ConfigureAwait(false);

            Emit(r.Success
                ? "Restore point created."
                : "Restore point could not be created (System Protection may be off) — continuing.");
        }
        catch (Exception ex)
        {
            Emit("Restore point skipped: " + ex.Message);
        }
    }

    private void Emit(string line) => LogLine?.Invoke($"{DateTime.Now:HH:mm:ss}  {line}");



    private static IEnumerable<string> LastLines(string text, int count)
    {
        if (string.IsNullOrWhiteSpace(text)) return new[] { "(no output)" };

        var lines = text.Replace("\r\n", "\n").Split('\n')
                        .Select(WingetText.Clean)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

        return lines.Count <= count ? lines : lines.Skip(lines.Count - count);
    }
}
