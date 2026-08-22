using System.Collections.ObjectModel;
using AppGeek.Models;
using AppGeek.Services;

namespace AppGeek.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public DashboardViewModel(ShellViewModel shell)
    {
        _shell = shell;

        ScanCommand = new AsyncRelayCommand(() => _shell.ScanAsync(), () => !_shell.IsScanning);
        UpdateEverythingCommand = new AsyncRelayCommand(UpdateEverythingAsync,
            () => _shell.Updates.Any(u => !u.IsPinned));
        GoToUpdatesCommand = new RelayCommand(() => _shell.Navigate("updates"));
        GoToCatalogueCommand = new RelayCommand(() => _shell.Navigate("catalogue"));
        ExportReportCommand = new RelayCommand(ExportReport);

        Refresh();
    }

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand UpdateEverythingCommand { get; }
    public RelayCommand GoToUpdatesCommand { get; }
    public RelayCommand GoToCatalogueCommand { get; }
    public RelayCommand ExportReportCommand { get; }

    public ObservableCollection<UpdateCandidate> TopUpdates { get; } = new();
    public ObservableCollection<ActivityEntry> RecentActivity { get; } = new();

    public int UpdateCount => _shell.Updates.Count;
    public int SecurityCount => _shell.Updates.Count(u => u.IsSecuritySensitive);
    public int InstalledCount => _shell.InstalledApps.Count;
    public int TrackedCount => _shell.InstalledApps.Count(a => a.IsTracked);
    public int UpToDateCount => Math.Max(0, TrackedCount - UpdateCount);

    public string DownloadSizeDisplay =>
        Format.Bytes(_shell.Updates.Sum(u => u.EstimatedDownloadBytes));

    public string SecuritySubtitle
    {
        get
        {
            var names = _shell.Updates.Where(u => u.IsSecuritySensitive).Take(3).Select(u => u.Name).ToList();
            return names.Count == 0 ? "Nothing urgent" : string.Join(", ", names);
        }
    }

    public string MachineDescription => _shell.MachineDescription;

    /// <summary>The shell owns the scan; the dashboard just displays its progress.</summary>
    public ShellViewModel Shell => _shell;

    public string LastScanDisplay
    {
        get
        {
            var last = _shell.Settings.Current.LastScanUtc;
            if (last is null) return "never scanned";
            var ago = DateTime.UtcNow - last.Value;
            if (ago.TotalMinutes < 1) return "scanned just now";
            if (ago.TotalMinutes < 60) return $"scanned {(int)ago.TotalMinutes} minutes ago";
            if (ago.TotalHours < 24) return $"scanned {(int)ago.TotalHours} hours ago";
            return $"scanned {last.Value.ToLocalTime():dd MMM HH:mm}";
        }
    }

    public string UpdateEverythingLabel =>
        UpdateCount == 0 ? "Everything is up to date"
                         : $"Update everything ({UpdateCount} apps · {DownloadSizeDisplay})";

    public bool HasUpdates => UpdateCount > 0;

    public void Refresh()
    {
        TopUpdates.Clear();
        foreach (var u in _shell.Updates.Take(3)) TopUpdates.Add(u);

        RecentActivity.Clear();
        foreach (var a in _shell.Activity.Entries.Take(5)) RecentActivity.Add(a);

        Raise(nameof(UpdateCount));
        Raise(nameof(SecurityCount));
        Raise(nameof(InstalledCount));
        Raise(nameof(TrackedCount));
        Raise(nameof(UpToDateCount));
        Raise(nameof(DownloadSizeDisplay));
        Raise(nameof(SecuritySubtitle));
        Raise(nameof(LastScanDisplay));
        Raise(nameof(MachineDescription));
        Raise(nameof(UpdateEverythingLabel));
        Raise(nameof(HasUpdates));
    }

    private async Task UpdateEverythingAsync()
    {
        var items = _shell.Updates
            .Where(u => !u.IsPinned)
            .Select(UpdatesViewModel.ToRunItem)
            .ToList();
        await _shell.StartRunAsync(items);
    }

    private void ExportReport()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export inventory report",
            FileName = $"AppGeek-{Environment.MachineName}-{DateTime.Now:yyyyMMdd}",
            DefaultExt = ".html",
            Filter = "HTML report (*.html)|*.html|CSV spreadsheet (*.csv)|*.csv"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var path = dialog.FileName;
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                ReportExporter.ExportCsv(path, _shell.InstalledApps, _shell.Updates);
            else
                ReportExporter.ExportHtml(path, _shell.InstalledApps, _shell.Updates);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("Export failed", ex);
            System.Windows.MessageBox.Show("The report could not be written.\n\n" + ex.Message,
                "Export failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }
}
