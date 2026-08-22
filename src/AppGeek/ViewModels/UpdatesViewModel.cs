using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using AppGeek.Models;
using AppGeek.Services;

namespace AppGeek.ViewModels;

public sealed class UpdatesViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public UpdatesViewModel(ShellViewModel shell)
    {
        _shell = shell;

        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = FilterPredicate;

        ScanCommand = new AsyncRelayCommand(() => _shell.ScanAsync(), () => !_shell.IsScanning);
        InstallSelectedCommand = new AsyncRelayCommand(InstallSelectedAsync, () => SelectedCount > 0);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        PinCommand = new RelayCommand(p => Pin(p as UpdateCandidate));
        IgnoreCommand = new RelayCommand(p => Ignore(p as UpdateCandidate));
        SetFilterCommand = new RelayCommand(p => Filter = p as string ?? "all");
    }

    public ObservableCollection<UpdateCandidate> Items { get; } = new();
    public ICollectionView View { get; }

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand InstallSelectedCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand PinCommand { get; }
    public RelayCommand IgnoreCommand { get; }
    public RelayCommand SetFilterCommand { get; }

    private string _filter = "all";
    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) { View.Refresh(); Raise(nameof(FilterAll)); Raise(nameof(FilterSecurity)); Raise(nameof(FilterMajor)); Raise(nameof(FilterPinned)); } }
    }

    public bool FilterAll => _filter == "all";
    public bool FilterSecurity => _filter == "security";
    public bool FilterMajor => _filter == "major";
    public bool FilterPinned => _filter == "pinned";

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) View.Refresh(); }
    }

    public int TotalCount => Items.Count;
    public int SecurityCount => Items.Count(i => i.IsSecuritySensitive);
    public int MajorCount => Items.Count(i => i.IsMajorVersionChange);
    public int PinnedCount => Items.Count(i => i.IsPinned);
    public int SelectedCount => Items.Count(i => i.IsSelected);

    public string SelectionSummary
    {
        get
        {
            if (SelectedCount == 0) return "Nothing selected";
            var bytes = Items.Where(i => i.IsSelected).Sum(i => i.EstimatedDownloadBytes);
            return $"{SelectedCount} app{(SelectedCount == 1 ? "" : "s")} selected · {Format.Bytes(bytes)}";
        }
    }

    public string HeaderSummary =>
        TotalCount == 0
            ? "Everything AppGeek tracks is up to date"
            : $"{TotalCount} of {_shell.InstalledApps.Count(a => a.IsTracked)} tracked apps have a newer version available";

    public string InstallButtonLabel =>
        SelectedCount == 0 ? "Update" : $"Update {SelectedCount} app{(SelectedCount == 1 ? "" : "s")}";

    public bool IsEmpty => Items.Count == 0;

    public void Refresh()
    {
        foreach (var i in Items) i.PropertyChanged -= OnItemChanged;
        Items.Clear();
        foreach (var u in _shell.Updates)
        {
            u.PropertyChanged += OnItemChanged;
            Items.Add(u);
        }
        View.Refresh();
        RaiseCounts();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateCandidate.IsSelected)) RaiseCounts();
    }

    private void RaiseCounts()
    {
        Raise(nameof(TotalCount));
        Raise(nameof(SecurityCount));
        Raise(nameof(MajorCount));
        Raise(nameof(PinnedCount));
        Raise(nameof(SelectedCount));
        Raise(nameof(SelectionSummary));
        Raise(nameof(HeaderSummary));
        Raise(nameof(InstallButtonLabel));
        Raise(nameof(IsEmpty));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private bool FilterPredicate(object o)
    {
        if (o is not UpdateCandidate u) return false;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var s = _search.Trim();
            if (!u.Name.Contains(s, StringComparison.OrdinalIgnoreCase) &&
                !u.PackageId.Contains(s, StringComparison.OrdinalIgnoreCase) &&
                !(u.Publisher ?? "").Contains(s, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return _filter switch
        {
            "security" => u.IsSecuritySensitive,
            "major" => u.IsMajorVersionChange,
            "pinned" => u.IsPinned,
            _ => true
        };
    }

    private void SetAll(bool value)
    {
        foreach (var i in Items)
        {
            if (value && (i.IsPinned || i.IsRunning)) continue;  // never auto-tick these
            i.IsSelected = value;
        }
        RaiseCounts();
    }

    private async Task InstallSelectedAsync()
    {
        var chosen = Items.Where(i => i.IsSelected).ToList();
        if (chosen.Count == 0) return;

        var running = chosen.Where(c => c.IsRunning).ToList();
        if (running.Count > 0 && _shell.Settings.Current.RunningAppPolicy == RunningAppPolicy.Ask)
        {
            var names = string.Join(", ", running.Select(r => r.Name));
            var answer = System.Windows.MessageBox.Show(
                $"These apps are running and will most likely fail to update:\n\n{names}\n\n" +
                "Close them yourself and click Yes to continue, or No to skip them.",
                "Apps are running",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);

            if (answer == System.Windows.MessageBoxResult.Cancel) return;
            if (answer == System.Windows.MessageBoxResult.No)
                chosen = chosen.Where(c => !c.IsRunning).ToList();
        }

        await _shell.StartRunAsync(chosen.Select(ToRunItem));
    }

    public static RunItem ToRunItem(UpdateCandidate u) => new()
    {
        PackageId = u.PackageId,
        Name = u.Name,
        SourceName = u.SourceName,
        Action = RunAction.Update,
        FromVersion = u.CurrentVersion,
        ToVersion = u.AvailableVersion,
        EstimatedBytes = u.EstimatedDownloadBytes,
        IconText = u.IconText,
        IconColour = u.IconColour,
        IsSecuritySensitive = u.IsSecuritySensitive,
        Scope = u.InstalledScope,
        InstallLocation = u.InstallLocation
    };

    private void Pin(UpdateCandidate? u)
    {
        if (u is null) return;
        _shell.Settings.AddExclusion(new ExclusionRule
        {
            Key = "id:" + u.PackageId.ToLowerInvariant(),
            DisplayName = u.Name,
            Kind = ExclusionKind.Pin,
            PinnedVersion = u.CurrentVersion,
            Note = "Pinned from the Updates list"
        });
        u.IsPinned = true;
        u.PinnedVersion = u.CurrentVersion;
        u.IsSelected = false;
        View.Refresh();
        RaiseCounts();
    }

    private void Ignore(UpdateCandidate? u)
    {
        if (u is null) return;
        _shell.Settings.AddExclusion(new ExclusionRule
        {
            Key = "id:" + u.PackageId.ToLowerInvariant(),
            DisplayName = u.Name,
            Kind = ExclusionKind.Ignore,
            Note = "Ignored from the Updates list"
        });
        _shell.Updates.Remove(u);
        Items.Remove(u);
        RaiseCounts();
    }
}
