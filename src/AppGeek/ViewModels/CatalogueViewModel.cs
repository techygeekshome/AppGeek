using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using AppGeek.Models;
using AppGeek.Services;

namespace AppGeek.ViewModels;

public sealed class CatalogueViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public CatalogueViewModel(ShellViewModel shell)
    {
        _shell = shell;

        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = FilterPredicate;
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CatalogueApp.Category)));
        View.SortDescriptions.Add(new SortDescription(nameof(CatalogueApp.Category), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(CatalogueApp.Popularity), ListSortDirection.Descending));

        InstallSelectedCommand = new AsyncRelayCommand(InstallSelectedAsync, () => SelectedCount > 0);
        ClearCommand = new RelayCommand(() => { foreach (var a in Items) a.IsSelected = false; RaiseCounts(); });
        SetCategoryCommand = new RelayCommand(p => Category = p as string ?? "All");
        RefreshCatalogueCommand = new AsyncRelayCommand(RefreshFromWebAsync);

        Refresh();
    }

    public ObservableCollection<CatalogueApp> Items { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ICollectionView View { get; }

    public AsyncRelayCommand InstallSelectedCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand SetCategoryCommand { get; }
    public AsyncRelayCommand RefreshCatalogueCommand { get; }

    private string _category = "All";
    public string Category
    {
        get => _category;
        set { if (Set(ref _category, value)) View.Refresh(); }
    }

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) View.Refresh(); }
    }

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public int AlreadyInstalledSelected => Items.Count(i => i.IsSelected && i.IsInstalled);

    public string CatalogueSummary =>
        $"{Items.Count} apps in catalogue · {_shell.Catalogue.SourceDescription}";

    public string SelectionSummary
    {
        get
        {
            if (SelectedCount == 0) return "Tick the apps you want, then install them all in one go";
            var mb = Items.Where(i => i.IsSelected).Sum(i => (long)i.ApproxSizeMb) * 1024L * 1024L;
            var skip = AlreadyInstalledSelected;
            var text = $"{SelectedCount} app{(SelectedCount == 1 ? "" : "s")} selected · {Format.Bytes(mb)}";
            if (skip > 0) text += $" · {skip} already installed and will be skipped";
            return text;
        }
    }

    public string InstallButtonLabel =>
        SelectedCount == 0 ? "Install" : $"Install {SelectedCount} app{(SelectedCount == 1 ? "" : "s")}";

    public void Refresh()
    {
        foreach (var i in Items) i.PropertyChanged -= OnItemChanged;
        Items.Clear();

        foreach (var a in _shell.Catalogue.Apps)
        {
            a.PropertyChanged += OnItemChanged;
            Items.Add(a);
        }

        Categories.Clear();
        Categories.Add("All");
        foreach (var c in _shell.Catalogue.Categories) Categories.Add(c);

        View.Refresh();
        RaiseCounts();
        Raise(nameof(CatalogueSummary));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CatalogueApp.IsSelected)) RaiseCounts();
    }

    private void RaiseCounts()
    {
        Raise(nameof(SelectedCount));
        Raise(nameof(AlreadyInstalledSelected));
        Raise(nameof(SelectionSummary));
        Raise(nameof(InstallButtonLabel));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private bool FilterPredicate(object o)
    {
        if (o is not CatalogueApp a) return false;

        if (!string.Equals(_category, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(a.Category, _category, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(_search)) return true;

        var s = _search.Trim();
        return a.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
            || a.Publisher.Contains(s, StringComparison.OrdinalIgnoreCase)
            || (a.Description ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
            || (a.WingetId ?? "").Contains(s, StringComparison.OrdinalIgnoreCase);
    }

    private async Task InstallSelectedAsync()
    {
        var chosen = Items.Where(i => i.IsSelected && !i.IsInstalled).ToList();
        if (chosen.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Everything you selected is already installed.",
                "Nothing to do", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var items = chosen.Select(a => new RunItem
        {
            PackageId = a.PreferredPackageId,
            Name = a.Name,
            SourceName = a.PreferredSource,
            Action = RunAction.Install,
            ToVersion = "latest",
            EstimatedBytes = a.ApproxSizeMb * 1024L * 1024L,
            IconText = string.IsNullOrWhiteSpace(a.IconText) ? IconFactory.Monogram(a.Name) : a.IconText,
            IconColour = a.IconColour,
            IsSecuritySensitive = SecurityRelevance.IsSecuritySensitive(a.Name, a.WingetId)
        });

        await _shell.StartRunAsync(items);
    }

    private async Task RefreshFromWebAsync()
    {
        var ok = await _shell.Catalogue.RefreshAsync(_shell.Settings.Current.CatalogueUrl);
        if (ok)
        {
            _shell.Catalogue.MarkInstalled(_shell.InstalledApps);
            Refresh();
        }
        else
        {
            System.Windows.MessageBox.Show(
                "The catalogue could not be refreshed. The built-in list is still in use.",
                "Catalogue", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }
}
