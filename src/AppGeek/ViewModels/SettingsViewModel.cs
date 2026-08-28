using System.Collections.ObjectModel;
using System.Diagnostics;
using AppGeek.Models;
using AppGeek.Services;

namespace AppGeek.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly AppSettings _s;

    public SettingsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _s = shell.Settings.Current;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        RemoveExclusionCommand = new RelayCommand(p => RemoveExclusion(p as ExclusionRule));
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync);
        RefreshCatalogueCommand = new AsyncRelayCommand(RefreshCatalogueAsync);

        RefreshExclusions();
    }

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand RemoveExclusionCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public AsyncRelayCommand ExportDiagnosticsCommand { get; }
    public AsyncRelayCommand RefreshCatalogueCommand { get; }

    public ObservableCollection<ExclusionRule> Exclusions { get; } = new();

    // ---- Scanning ----
    public bool AutoScan
    {
        get => _s.AutoScan;
        set { _s.AutoScan = value; Raise(); Raise(nameof(SchedulePreview)); }
    }
    public bool NotifyOnUpdates { get => _s.NotifyOnUpdates; set { _s.NotifyOnUpdates = value; Raise(); } }
    public bool IncludeStoreApps { get => _s.IncludeStoreApps; set { _s.IncludeStoreApps = value; Raise(); } }
    public bool IncludeUnknownVersions { get => _s.IncludeUnknownVersions; set { _s.IncludeUnknownVersions = value; Raise(); } }

    public List<string> ScheduleOptions { get; } = new()
    { "Daily at 03:00", "Daily at 12:00", "Weekly on Sunday", "Every time AppGeek starts", "Manually only" };

    public string ScanSchedule
    {
        get => _s.ScanSchedule;
        set { _s.ScanSchedule = value; Raise(); Raise(nameof(SchedulePreview)); }
    }

    /// <summary>What the chosen option actually means, spelled out under the dropdown.</summary>
    public string SchedulePreview
    {
        get
        {
            if (!_s.AutoScan) return "Automatic scanning is off.";
            var plan = Services.ScanSchedule.Parse(_s.ScanSchedule);
            return plan.NeedsScheduledTask
                ? plan.Describe() + " — registered with Windows Task Scheduler as \"" +
                  Services.ScanSchedule.TaskName + "\". It only scans; it never installs."
                : plan.Describe() + ".";
        }
    }

    private string _scheduleStatus = "";
    public string ScheduleStatus => _scheduleStatus;

    // ---- Sources ----
    public bool UseWinget { get => _s.UseWinget; set { _s.UseWinget = value; Raise(); } }
    public bool UseMicrosoftStore { get => _s.UseMicrosoftStore; set { _s.UseMicrosoftStore = value; Raise(); } }
    public bool UseTghCatalogue { get => _s.UseTghCatalogue; set { _s.UseTghCatalogue = value; Raise(); } }
    public string CatalogueUrl { get => _s.CatalogueUrl; set { _s.CatalogueUrl = value; Raise(); } }

    public string WingetStatus => _shell.Winget.IsAvailable
        ? $"Connected · {_shell.Winget.Version}"
        : "Not found — install 'App Installer' from the Microsoft Store";

    public string CatalogueStatus =>
        $"{_shell.Catalogue.Apps.Count} apps · {_shell.Catalogue.SourceDescription}" +
        (_shell.Catalogue.LastSynced is { } t ? $" · synced {t:dd MMM HH:mm}" : "");

    // ---- Install behaviour ----
    public List<string> RunningAppOptions { get; } = new() { "Ask each time", "Always close", "Always skip" };

    public string RunningAppPolicyText
    {
        get => _s.RunningAppPolicy switch
        {
            Models.RunningAppPolicy.AlwaysClose => "Always close",
            Models.RunningAppPolicy.AlwaysSkip => "Always skip",
            _ => "Ask each time"
        };
        set
        {
            _s.RunningAppPolicy = value switch
            {
                "Always close" => Models.RunningAppPolicy.AlwaysClose,
                "Always skip" => Models.RunningAppPolicy.AlwaysSkip,
                _ => Models.RunningAppPolicy.Ask
            };
            Raise();
        }
    }

    public List<string> RebootOptions { get; } = new() { "Prompt me at the end", "Never reboot", "Reboot automatically" };

    public string RebootPolicyText
    {
        get => _s.RebootPolicy switch
        {
            Models.RebootPolicy.Never => "Never reboot",
            Models.RebootPolicy.Automatic => "Reboot automatically",
            _ => "Prompt me at the end"
        };
        set
        {
            _s.RebootPolicy = value switch
            {
                "Never reboot" => Models.RebootPolicy.Never,
                "Reboot automatically" => Models.RebootPolicy.Automatic,
                _ => Models.RebootPolicy.PromptAtEnd
            };
            Raise();
        }
    }

    public bool CreateRestorePoint { get => _s.CreateRestorePoint; set { _s.CreateRestorePoint = value; Raise(); } }
    public bool KeepInstallers { get => _s.KeepInstallers; set { _s.KeepInstallers = value; Raise(); } }

    public string CachePath => AppPaths.CacheDir;
    public string LogPath => AppPaths.LogDir;
    public string VersionText => $"AppGeek {typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"} · part of the TechyGeeksHome Geek series";

    public void RefreshExclusions()
    {
        Exclusions.Clear();
        foreach (var e in _s.Exclusions.OrderBy(e => e.DisplayName)) Exclusions.Add(e);
        Raise(nameof(ExclusionSummary));
    }

    public string ExclusionSummary =>
        Exclusions.Count == 0 ? "No rules" : $"{Exclusions.Count} rule{(Exclusions.Count == 1 ? "" : "s")}";

    /// <summary>
    /// Saves, then brings the Windows scheduled task into line with what was just saved.
    /// Doing it here rather than on every keystroke means one schtasks call per Save, and it
    /// keeps the task and the settings file from drifting apart.
    /// </summary>
    private async Task SaveAsync()
    {
        _shell.Settings.Save();

        var outcome = await ScheduledScanService.ApplyAsync(_s).ConfigureAwait(true);
        _scheduleStatus = outcome.Ok
            ? "Background scan: " + outcome.Message
            : "Background scan could not be scheduled — " + outcome.Message;
        Raise(nameof(ScheduleStatus));

        System.Windows.MessageBox.Show(
            outcome.Ok
                ? "Settings saved.\n\n" + _scheduleStatus
                : "Settings saved, but the background scan could not be scheduled.\n\n" + outcome.Message,
            "AppGeek", System.Windows.MessageBoxButton.OK,
            outcome.Ok ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
    }

    private void RemoveExclusion(ExclusionRule? rule)
    {
        if (rule is null) return;
        _shell.Settings.RemoveExclusion(rule);
        RefreshExclusions();
    }

    /// <summary>
    /// Bundles the logs, settings and environment into one zip that can be attached to a
    /// bug report. Nothing is uploaded — the file is written where the user chooses.
    /// </summary>
    private async Task ExportDiagnosticsAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export AppGeek diagnostics",
            FileName = $"AppGeek-diagnostics-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmm}",
            DefaultExt = ".zip",
            Filter = "Zip archive (*.zip)|*.zip"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var path = await DiagnosticsExporter.ExportAsync(dialog.FileName, _shell.Winget, _shell.Settings);

            System.Windows.MessageBox.Show(
                "Diagnostics written to:\n\n" + path +
                "\n\nIt contains AppGeek's logs, your settings and details of this PC. " +
                "Nothing has been uploaded anywhere.",
                "Diagnostics exported", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("Diagnostics export failed", ex);
            System.Windows.MessageBox.Show("The diagnostics could not be written.\n\n" + ex.Message,
                "Export failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private static void OpenLogFolder()
    {
        try { Process.Start(new ProcessStartInfo(AppPaths.LogDir) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Could not open the log folder: " + ex.Message); }
    }

    private async Task RefreshCatalogueAsync()
    {
        await _shell.Catalogue.RefreshAsync(CatalogueUrl);
        _shell.CataloguePage.Refresh();
        Raise(nameof(CatalogueStatus));
    }
}
