using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using AppGeek.Models;
using AppGeek.Services;

// System.Diagnostics also defines an ActivityKind (distributed tracing); ours wins here.
using ActivityKind = AppGeek.Models.ActivityKind;

namespace AppGeek.ViewModels;

public sealed class InstalledViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public InstalledViewModel(ShellViewModel shell)
    {
        _shell = shell;

        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = FilterPredicate;
        View.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.DisplayName), ListSortDirection.Ascending));

        SetFilterCommand = new RelayCommand(p => Filter = p as string ?? "all");
        ExportCsvCommand = new RelayCommand(() => Export(false));
        ExportHtmlCommand = new RelayCommand(() => Export(true));
        OpenFolderCommand = new RelayCommand(p => OpenFolder(p as InstalledApp));
        UninstallCommand = new RelayCommand(p => Uninstall(p as InstalledApp));
        IgnoreCommand = new RelayCommand(p => Ignore(p as InstalledApp));
    }

    public ObservableCollection<InstalledApp> Items { get; } = new();
    public ICollectionView View { get; }

    public RelayCommand SetFilterCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand ExportHtmlCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand UninstallCommand { get; }
    public RelayCommand IgnoreCommand { get; }

    private string _filter = "all";
    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) View.Refresh(); }
    }

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) View.Refresh(); }
    }

    public int TotalCount => Items.Count;
    public int OutOfDateCount => _shell.Updates.Count;
    public int TrackedCount => Items.Count(a => a.IsTracked);
    public int UntrackedCount => Items.Count(a => !a.IsTracked);
    public int StoreCount => Items.Count(a => a.Scope == InstallScope.Store);

    public string TotalSizeDisplay => Format.Bytes(Items.Sum(a => a.EstimatedSizeBytes));
    public string HeaderSummary =>
        $"Everything AppGeek found on this PC — machine-wide, per-user and Microsoft Store";

    public void Refresh()
    {
        Items.Clear();
        foreach (var a in _shell.InstalledApps) Items.Add(a);
        View.Refresh();

        Raise(nameof(TotalCount));
        Raise(nameof(OutOfDateCount));
        Raise(nameof(TrackedCount));
        Raise(nameof(UntrackedCount));
        Raise(nameof(StoreCount));
        Raise(nameof(TotalSizeDisplay));
    }

    private bool FilterPredicate(object o)
    {
        if (o is not InstalledApp a) return false;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var s = _search.Trim();
            if (!a.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase) &&
                !(a.Publisher ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) &&
                !(a.PackageId ?? "").Contains(s, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return _filter switch
        {
            "outdated" => a.PackageId is not null &&
                          _shell.Updates.Any(u => string.Equals(u.PackageId, a.PackageId, StringComparison.OrdinalIgnoreCase)),
            "uptodate" => a.IsTracked &&
                          !_shell.Updates.Any(u => string.Equals(u.PackageId, a.PackageId, StringComparison.OrdinalIgnoreCase)),
            "untracked" => !a.IsTracked,
            "store" => a.Scope == InstallScope.Store,
            _ => true
        };
    }

    private void Export(bool html)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export installed applications",
            FileName = $"AppGeek-{Environment.MachineName}-{DateTime.Now:yyyyMMdd}",
            DefaultExt = html ? ".html" : ".csv",
            Filter = html ? "HTML report (*.html)|*.html" : "CSV spreadsheet (*.csv)|*.csv"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (html) ReportExporter.ExportHtml(dialog.FileName, Items, _shell.Updates);
            else ReportExporter.ExportCsv(dialog.FileName, Items, _shell.Updates);

            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Export failed", ex);
        }
    }

    private static void OpenFolder(InstalledApp? app)
    {
        var location = app?.InstallLocation;
        if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location)) return;
        try { Process.Start(new ProcessStartInfo(location) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Could not open folder: " + ex.Message); }
    }

    private void Uninstall(InstalledApp? app)
    {
        if (app is null) return;

        var command = app.QuietUninstallString ?? app.UninstallString;
        if (string.IsNullOrWhiteSpace(command))
        {
            System.Windows.MessageBox.Show(
                $"{app.DisplayName} does not publish an uninstall command.",
                "Cannot uninstall", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"Uninstall {app.DisplayName}?\n\nAppGeek will hand over to the application's own uninstaller.",
            "Confirm uninstall", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            Log.Info($"Uninstalling {app.DisplayName} via: {command}");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{command}\"")
            {
                UseShellExecute = true,
                Verb = Elevation.IsElevated ? "" : "runas"
            });
            _shell.Activity.Add(ActivityKind.Info, $"Started uninstall of {app.DisplayName}");
        }
        catch (Exception ex)
        {
            Log.Error("Uninstall failed to start", ex);
        }
    }

    private void Ignore(InstalledApp? app)
    {
        if (app is null) return;
        _shell.Settings.AddExclusion(new ExclusionRule
        {
            Key = app.Key,
            DisplayName = app.DisplayName,
            Kind = ExclusionKind.Ignore,
            Note = "Ignored from the Installed list"
        });
        _shell.InstalledApps.Remove(app);
        Items.Remove(app);
        Refresh();
    }
}
