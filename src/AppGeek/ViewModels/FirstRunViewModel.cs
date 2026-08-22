using System.Collections.ObjectModel;
using AppGeek.Models;
using AppGeek.Services;

namespace AppGeek.ViewModels;

public sealed class FirstRunViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public FirstRunViewModel(ShellViewModel shell)
    {
        _shell = shell;

        StartCommand = new AsyncRelayCommand(StartAsync);
        SkipCommand = new RelayCommand(() => _shell.CompleteFirstRun());

        foreach (var app in _shell.Catalogue.Apps.Where(a => a.Essential).OrderByDescending(a => a.Popularity).Take(6))
        {
            app.IsSelected = false;
            app.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(CatalogueApp.IsSelected)) return;
                Raise(nameof(SelectedCount));
                Raise(nameof(StartButtonLabel));
            };
            StarterPack.Add(app);
        }
    }

    public AsyncRelayCommand StartCommand { get; }
    public RelayCommand SkipCommand { get; }

    public ObservableCollection<CatalogueApp> StarterPack { get; } = new();

    public int SelectedCount => StarterPack.Count(a => a.IsSelected);

    public string StartButtonLabel =>
        SelectedCount == 0 ? "Scan this PC" : $"Scan and install {SelectedCount} app{(SelectedCount == 1 ? "" : "s")}";

    private async Task StartAsync()
    {
        var chosen = StarterPack.Where(a => a.IsSelected).ToList();
        _shell.CompleteFirstRun();
        await _shell.ScanAsync();

        if (chosen.Count == 0) return;

        var items = chosen.Select(a => new RunItem
        {
            PackageId = a.PreferredPackageId,
            Name = a.Name,
            SourceName = a.PreferredSource,
            Action = RunAction.Install,
            ToVersion = "latest",
            EstimatedBytes = a.ApproxSizeMb * 1024L * 1024L,
            IconText = a.IconText,
            IconColour = a.IconColour,
            IsSecuritySensitive = SecurityRelevance.IsSecuritySensitive(a.Name, a.WingetId)
        });

        await _shell.StartRunAsync(items);
    }
}
