using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using AppGeek.Models;
using AppGeek.Services;

// System.Diagnostics also defines an ActivityKind (distributed tracing); ours wins here.
using ActivityKind = AppGeek.Models.ActivityKind;

namespace AppGeek.ViewModels;

public sealed class RunViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly StringBuilder _log = new();

    public RunViewModel(ShellViewModel shell)
    {
        _shell = shell;

        _shell.Runner.LogLine += line => OnUi(() =>
        {
            _log.AppendLine(line);
            if (_log.Length > 40000) _log.Remove(0, 20000);
            Raise(nameof(LogText));
        });

        _shell.Runner.ItemChanged += _ => OnUi(RaiseProgress);
        _shell.Runner.Completed += summary => OnUi(() => OnCompleted(summary));

        StopCommand = new RelayCommand(() => _shell.Runner.Cancel(), () => IsRunning);
        OpenLogCommand = new RelayCommand(OpenLog);
        BackCommand = new RelayCommand(() => _shell.Navigate("dashboard"));
        RescanCommand = new AsyncRelayCommand(() => _shell.ScanAsync(), () => !IsRunning);
    }

    public ObservableCollection<RunItem> Items => _shell.Runner.Items;

    public RelayCommand StopCommand { get; }
    public RelayCommand OpenLogCommand { get; }
    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }

    public string LogText => _log.ToString();

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set { if (Set(ref _isRunning, value)) Raise(nameof(HasFinished)); } }

    public bool HasFinished => !_isRunning && Items.Count > 0;

    private DateTime _started;

    private bool _shutdownWhenDone;
    public bool ShutdownWhenDone { get => _shutdownWhenDone; set => Set(ref _shutdownWhenDone, value); }

    public int DoneCount => Items.Count(i => i.State is RunItemState.Succeeded or RunItemState.Failed or RunItemState.Skipped);
    public int SucceededCount => Items.Count(i => i.State == RunItemState.Succeeded);
    public int FailedCount => Items.Count(i => i.State == RunItemState.Failed);
    public int SkippedCount => Items.Count(i => i.State == RunItemState.Skipped);

    public double OverallProgress
    {
        get
        {
            if (Items.Count == 0) return 0;
            double sum = Items.Sum(i => i.State switch
            {
                RunItemState.Succeeded => 100.0,
                RunItemState.Failed or RunItemState.Skipped or RunItemState.Cancelled => 100.0,
                RunItemState.Running => i.Percent,
                _ => 0.0
            });
            return sum / Items.Count;
        }
    }

    public string Title => Items.Count == 0
        ? "Activity"
        : IsRunning
            ? $"Installing — {DoneCount} of {Items.Count} complete"
            : $"Finished — {SucceededCount} succeeded, {FailedCount} failed, {SkippedCount} skipped";

    public string Subtitle => IsRunning
        ? "You can leave this running. AppGeek will tell you when it is done."
        : Items.Count == 0
            ? "Install and update runs appear here, with a full log of what happened."
            : $"Run finished in {Format.Duration(DateTime.Now - _started)}";

    public string FooterSummary =>
        Items.Count == 0
            ? "No run yet"
            : $"{SucceededCount} succeeded · {FailedCount} failed · {SkippedCount} skipped";

    public async Task ExecuteAsync(IEnumerable<RunItem> items)
    {
        _log.Clear();
        Raise(nameof(LogText));

        _started = DateTime.Now;
        IsRunning = true;
        RaiseProgress();

        await _shell.Runner.RunAsync(items);
    }

    private void OnCompleted(RunSummary summary)
    {
        IsRunning = false;
        RaiseProgress();

        if (summary.Failed > 0)
        {
            _shell.Activity.Add(ActivityKind.Warning,
                $"Run finished with {summary.Failed} failure{(summary.Failed == 1 ? "" : "s")}");
        }

        // Refresh the inventory so the dashboard reflects what just changed.
        _ = _shell.ScanAsync();

        if (ShutdownWhenDone && summary.Failed == 0 && !summary.Cancelled)
        {
            Log.Info("Shutdown requested after run.");
            try { Process.Start(new ProcessStartInfo("shutdown", "/s /t 60") { CreateNoWindow = true, UseShellExecute = false }); }
            catch (Exception ex) { Log.Warn("Shutdown could not be scheduled: " + ex.Message); }
        }
    }

    private void RaiseProgress()
    {
        Raise(nameof(DoneCount));
        Raise(nameof(SucceededCount));
        Raise(nameof(FailedCount));
        Raise(nameof(SkippedCount));
        Raise(nameof(OverallProgress));
        Raise(nameof(Title));
        Raise(nameof(Subtitle));
        Raise(nameof(FooterSummary));
        Raise(nameof(HasFinished));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void OpenLog()
    {
        var path = _shell.Runner.CurrentLogPath ?? Log.DailyFile;
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(AppPaths.LogDir) { UseShellExecute = true });
        }
        catch (Exception ex) { Services.Log.Warn("Could not open the log: " + ex.Message); }
    }

    private static void OnUi(Action action)
    {
        var app = Application.Current;
        if (app is null) { action(); return; }
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.Invoke(action);
    }
}
