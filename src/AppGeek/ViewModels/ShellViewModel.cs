using System.Collections.ObjectModel;
using System.Windows;
using AppGeek.Models;
using AppGeek.Services;

namespace AppGeek.ViewModels;

/// <summary>Owns the services, the navigation rail and the shared scan results.</summary>
public sealed class ShellViewModel : ObservableObject
{
    public SettingsService Settings { get; }
    public WingetClient Winget { get; }
    public CatalogueService Catalogue { get; }
    public ActivityService Activity { get; }
    public InventoryService Inventory { get; }
    public InstallRunner Runner { get; }
    public WingetBootstrapper Bootstrapper { get; }

    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();
    public ObservableCollection<InstalledApp> InstalledApps { get; } = new();
    public ObservableCollection<UpdateCandidate> Updates { get; } = new();

    public DashboardViewModel Dashboard { get; }
    public UpdatesViewModel UpdatesPage { get; }
    public CatalogueViewModel CataloguePage { get; }
    public InstalledViewModel InstalledPage { get; }
    public RunViewModel RunPage { get; }
    public SettingsViewModel SettingsPage { get; }
    public FirstRunViewModel FirstRun { get; }

    public ShellViewModel()
    {
        Settings = new SettingsService();
        Settings.Load();

        Winget = new WingetClient();
        Catalogue = new CatalogueService();
        Catalogue.Load();
        Activity = new ActivityService();
        Activity.Load();

        Bootstrapper = new WingetBootstrapper(Winget);
        Inventory = new InventoryService(Winget, Settings);
        Runner = new InstallRunner(Winget, Settings, Activity);

        Dashboard = new DashboardViewModel(this);
        UpdatesPage = new UpdatesViewModel(this);
        CataloguePage = new CatalogueViewModel(this);
        InstalledPage = new InstalledViewModel(this);
        RunPage = new RunViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        FirstRun = new FirstRunViewModel(this);

        BuildNav();

        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? "dashboard"));
        FixWingetCommand = new AsyncRelayCommand(FixWingetAsync, () => !IsFixingWinget);
        OpenStoreCommand = new RelayCommand(WingetBootstrapper.OpenStore);
        DismissDependencyCommand = new RelayCommand(() => DependencyDismissed = true);
        RecheckDependencyCommand = new RelayCommand(RefreshDependencyState);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        ShowAboutCommand = new RelayCommand(ShowAbout);

        RefreshDependencyState();

        CurrentPage = Settings.Current.FirstRunComplete ? Dashboard : (object)FirstRun;
        ShowNavigation = Settings.Current.FirstRunComplete;
        if (ShowNavigation) Select("dashboard");
    }

    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand FixWingetCommand { get; }
    public RelayCommand OpenStoreCommand { get; }
    public RelayCommand DismissDependencyCommand { get; }
    public RelayCommand RecheckDependencyCommand { get; }
    public AsyncRelayCommand CheckForUpdatesCommand { get; }
    public RelayCommand ShowAboutCommand { get; }

    private object _currentPage = null!;
    public object CurrentPage { get => _currentPage; set => Set(ref _currentPage, value); }

    private bool _showNavigation;
    public bool ShowNavigation { get => _showNavigation; set => Set(ref _showNavigation, value); }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set { if (Set(ref _isScanning, value)) { Raise(nameof(ShowStatusStrip)); RelayCommand.RaiseCanExecuteChanged(); } }
    }

    /// <summary>The strip carries either scan progress or the last update-check result.</summary>
    public bool ShowStatusStrip => IsScanning || HasStatusMessage;

    private string _scanStatus = "Not scanned yet";
    public string ScanStatus { get => _scanStatus; set => Set(ref _scanStatus, value); }

    private int _scanPercent;
    public int ScanPercent
    {
        get => _scanPercent;
        set { if (Set(ref _scanPercent, value)) Raise(nameof(ScanPercentText)); }
    }

    public string ScanPercentText => $"{ScanPercent}%";

    private string _scanStepLabel = "";
    public string ScanStepLabel { get => _scanStepLabel; set => Set(ref _scanStepLabel, value); }

    public string MachineDescription =>
        $"{Environment.MachineName} · {OsDescription()} · {(Elevation.IsElevated ? "running as administrator" : "running as standard user")}";

    public bool IsElevated => Elevation.IsElevated;

    // ---- Sidebar chrome, matching the rest of the Geek range ----------------------

    public string BrandName => AppInfo.Name;
    public string BrandBy => $"by {AppInfo.Publisher}";
    public string VersionText => $"v{AppInfo.CurrentVersionText}";
    public string AboutButtonLabel => $"About {AppInfo.Name}";

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        set { if (Set(ref _statusMessage, value)) { Raise(nameof(HasStatusMessage)); Raise(nameof(ShowStatusStrip)); } }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    private bool _isCheckingForUpdates;
    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        set { if (Set(ref _isCheckingForUpdates, value)) RelayCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>
    /// Manual only, and it never downloads or installs. An updater that silently
    /// self-updates is exactly the behaviour AppGeek refuses to inflict on other
    /// people's software.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        StatusMessage = "Checking for updates\u2026";
        try
        {
            var result = await UpdateChecker.CheckAsync();
            StatusMessage = result.Message;

            if (!result.UpdateAvailable || result.ReleaseUrl is null) return;

            var answer = MessageBox.Show(
                result.Message + "\n\nOpen the release page? AppGeek will not download or " +
                "install anything by itself.",
                "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
                AppInfo.OpenUrl(result.ReleaseUrl);
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            StatusMessage = "The update check could not be completed.";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private static void ShowAbout()
    {
        var about = new Views.AboutWindow { Owner = Application.Current?.MainWindow };
        about.ShowDialog();
    }

    // ---- App Installer / winget dependency state -------------------------------

    private WingetCheck _dependency = new(Services.WingetStatus.Ready, null, "", "");
    public WingetCheck Dependency { get => _dependency; private set => Set(ref _dependency, value); }

    private bool _dependencyDismissed;
    public bool DependencyDismissed
    {
        get => _dependencyDismissed;
        set { if (Set(ref _dependencyDismissed, value)) Raise(nameof(ShowDependencyBanner)); }
    }

    private bool _isFixingWinget;
    public bool IsFixingWinget
    {
        get => _isFixingWinget;
        set { if (Set(ref _isFixingWinget, value)) { Raise(nameof(FixButtonLabel)); RelayCommand.RaiseCanExecuteChanged(); } }
    }

    public string FixButtonLabel => IsFixingWinget ? "Working…" : "Fix this for me";

    public bool ShowDependencyBanner => !Dependency.IsReady && !DependencyDismissed;

    public void RefreshDependencyState()
    {
        Dependency = Bootstrapper.Check();
        Raise(nameof(ShowDependencyBanner));
        Raise(nameof(WingetStatus));
    }

    private async Task FixWingetAsync()
    {
        IsFixingWinget = true;
        try
        {
            // Step 1: the cheap offline fix — re-register the package for this user.
            if (await Bootstrapper.TryReRegisterAsync())
            {
                RefreshDependencyState();
                Activity.Add(ActivityKind.Success, "Repaired Windows Package Manager (App Installer)");
                MessageBox.Show(
                    "Windows Package Manager is now available. AppGeek is ready to use.",
                    "Fixed", MessageBoxButton.OK, MessageBoxImage.Information);
                await ScanAsync();
                return;
            }

            // Step 2: offer the Store, which is the route most people should take.
            var answer = MessageBox.Show(
                "AppGeek could not repair the existing installation.\n\n" +
                "The next step is to install Microsoft's 'App Installer' from the Store, which takes " +
                "about a minute. Open the Store now?\n\n" +
                "Choose No to try the advanced repair instead, which downloads the official " +
                "bootstrap module and needs an internet connection.",
                "App Installer needed", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Cancel) return;

            if (answer == MessageBoxResult.Yes)
            {
                WingetBootstrapper.OpenStore();
                MessageBox.Show(
                    "Once App Installer has finished installing, come back and press " +
                    "'Check again' in the banner.",
                    "AppGeek", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Step 3: the official PowerShell bootstrap.
            var repaired = await Bootstrapper.TryRepairViaPowerShellAsync();
            RefreshDependencyState();

            MessageBox.Show(
                repaired
                    ? "Windows Package Manager was installed successfully. AppGeek is ready to use."
                    : "The advanced repair did not succeed.\n\nYou can install App Installer manually " +
                      "from " + WingetBootstrapper.ManualDownloadUrl,
                "AppGeek", MessageBoxButton.OK,
                repaired ? MessageBoxImage.Information : MessageBoxImage.Warning);

            if (repaired) await ScanAsync();
        }
        finally
        {
            IsFixingWinget = false;
        }
    }

    private void BuildNav()
    {
        NavItems.Add(new NavItemViewModel("dashboard", "Dashboard", "\uE80F"));
        NavItems.Add(new NavItemViewModel("updates", "Updates", "\uE895"));
        NavItems.Add(new NavItemViewModel("catalogue", "Catalogue", "\uE80A"));
        NavItems.Add(new NavItemViewModel("installed", "Installed", "\uE71D"));
        NavItems.Add(new NavItemViewModel("tools", "TOOLS", "", isGroupHeader: true));
        NavItems.Add(new NavItemViewModel("run", "Activity", "\uE823"));
        NavItems.Add(new NavItemViewModel("settings", "Settings", "\uE713"));
    }

    public void Navigate(string key)
    {
        CurrentPage = key switch
        {
            "updates" => UpdatesPage,
            "catalogue" => CataloguePage,
            "installed" => InstalledPage,
            "run" => RunPage,
            "settings" => SettingsPage,
            _ => Dashboard
        };
        Select(key);
    }

    private void Select(string key)
    {
        foreach (var n in NavItems) n.IsSelected = n.Key == key;
    }

    public void CompleteFirstRun()
    {
        Settings.Current.FirstRunComplete = true;
        Settings.Save();
        ShowNavigation = true;
        Navigate("dashboard");
    }

    /// <summary>Runs a full scan and pushes the results into the shared collections.</summary>
    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;

        try
        {
            ScanPercent = 0;
            ScanStepLabel = "";

            // Progress<T> marshals back to the UI thread it was created on.
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanPercent = p.Percent;
                ScanStatus = p.Message;
                ScanStepLabel = p.StepLabel;
            });

            var result = await Inventory.ScanAsync(progress).ConfigureAwait(true);

            InstalledApps.Clear();
            foreach (var a in result.Installed) InstalledApps.Add(a);

            Updates.Clear();
            foreach (var u in result.Updates) Updates.Add(u);

            Catalogue.MarkInstalled(result.Installed);
            CataloguePage.Refresh();
            InstalledPage.Refresh();
            UpdatesPage.Refresh();
            Dashboard.Refresh();

            var updateNav = NavItems.FirstOrDefault(n => n.Key == "updates");
            if (updateNav is not null)
                updateNav.Badge = result.Updates.Count > 0 ? result.Updates.Count.ToString() : null;

            var installedNav = NavItems.FirstOrDefault(n => n.Key == "installed");
            if (installedNav is not null)
            {
                installedNav.Badge = result.Installed.Count.ToString();
                installedNav.BadgeIsAccent = false;
            }

            ScanPercent = 100;
            ScanStatus = $"Scanned {result.Installed.Count} apps · {result.Updates.Count} updates available";
            Activity.Add(ActivityKind.Info,
                $"Scan completed · {result.Updates.Count} update{(result.Updates.Count == 1 ? "" : "s")} found");
            Dashboard.Refresh();
        }
        catch (Exception ex)
        {
            Log.Error("Scan failed", ex);
            ScanStatus = "Scan failed — see the log for details";
            ScanStepLabel = "";
            MessageBox.Show(
                "AppGeek could not finish the scan.\n\n" + ex.Message,
                "Scan failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsScanning = false;
            Raise(nameof(MachineDescription));
        }
    }

    /// <summary>
    /// Starts an install run. If AppGeek is not elevated it relaunches itself with a
    /// single UAC prompt rather than prompting once per package.
    /// </summary>
    public async Task StartRunAsync(IEnumerable<RunItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        // AppGeek requests administrator rights in its manifest, so by the time a run
        // can be started it is already elevated. If that somehow is not the case,
        // say so plainly rather than silently attempting an install that will fail.
        if (!Elevation.IsElevated)
        {
            MessageBox.Show(
                "AppGeek is not running with administrator rights, so it cannot install or " +
                "update software.\n\nClose it and start it again, accepting the Windows prompt.",
                "Administrator rights needed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Navigate("run");
        await RunPage.ExecuteAsync(list);
    }

    private static string OsDescription()
    {
        try
        {
            var caption = Environment.OSVersion.VersionString;
            var build = Environment.OSVersion.Version.Build;
            return build >= 22000 ? $"Windows 11 (build {build})" : $"Windows 10 (build {build})";
        }
        catch { return "Windows"; }
    }
}
