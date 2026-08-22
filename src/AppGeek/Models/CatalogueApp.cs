using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AppGeek.Models;

/// <summary>An entry in the AppGeek catalogue (the Ninite-style "tick and install" list).</summary>
public sealed class CatalogueApp : INotifyPropertyChanged
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("publisher")] public string Publisher { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "Other";
    [JsonPropertyName("wingetId")] public string? WingetId { get; set; }
    [JsonPropertyName("storeId")] public string? StoreId { get; set; }
    [JsonPropertyName("iconText")] public string IconText { get; set; } = "";
    [JsonPropertyName("iconColour")] public string IconColour { get; set; } = "#2E78D8";
    [JsonPropertyName("approxSizeMb")] public int ApproxSizeMb { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    [JsonPropertyName("popularity")] public int Popularity { get; set; }
    [JsonPropertyName("essential")] public bool Essential { get; set; }

    [JsonIgnore] public string PreferredPackageId => WingetId ?? StoreId ?? Id;
    [JsonIgnore] public string PreferredSource => WingetId is not null ? "winget" : "msstore";

    private bool _isInstalled;
    [JsonIgnore]
    public bool IsInstalled
    {
        get => _isInstalled;
        set { if (_isInstalled != value) { _isInstalled = value; OnPropertyChanged(); OnPropertyChanged(nameof(FooterText)); } }
    }

    private bool _isSelected;
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    [JsonIgnore]
    public string FooterText
    {
        get
        {
            var size = ApproxSizeMb > 0 ? $"{ApproxSizeMb} MB" : "";
            if (IsInstalled) return string.IsNullOrEmpty(size) ? "Installed ✓" : $"{size} · installed ✓";
            return size;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class CatalogueFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("updated")] public string? Updated { get; set; }
    [JsonPropertyName("apps")] public List<CatalogueApp> Apps { get; set; } = new();
}
