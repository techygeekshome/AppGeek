using System.Text.Json;
using AppGeek.Models;

namespace AppGeek.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public AppSettings Current { get; private set; } = new();

    public event Action<AppSettings>? Changed;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded is not null)
                {
                    Current = loaded;
                    Migrate(Current);
                    Log.Debug("Settings loaded.");
                    return Current;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Settings could not be read, falling back to defaults: " + ex.Message);
        }

        Current = new AppSettings();
        return Current;
    }

    /// <summary>Quietly moves settings on when a shipped default turns out to be wrong.</summary>
    private static void Migrate(AppSettings settings)
    {
        if (string.Equals(settings.CatalogueUrl, AppSettings.RetiredCatalogueUrl,
                          StringComparison.OrdinalIgnoreCase))
        {
            settings.CatalogueUrl = AppSettings.DefaultCatalogueUrl;
            Log.Info("Catalogue URL migrated to the repository copy; the old one was never published.");
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, Options));
            Log.Debug("Settings saved.");
            Changed?.Invoke(Current);
        }
        catch (Exception ex)
        {
            Log.Error("Settings could not be saved", ex);
        }
    }

    public bool IsIgnored(string key) =>
        Current.Exclusions.Any(e => e.Kind == ExclusionKind.Ignore &&
                                    string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    public ExclusionRule? FindPin(string key) =>
        Current.Exclusions.FirstOrDefault(e => e.Kind == ExclusionKind.Pin &&
                                               string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    public void AddExclusion(ExclusionRule rule)
    {
        Current.Exclusions.RemoveAll(e => string.Equals(e.Key, rule.Key, StringComparison.OrdinalIgnoreCase));
        Current.Exclusions.Add(rule);
        Save();
    }

    public void RemoveExclusion(ExclusionRule rule)
    {
        Current.Exclusions.RemoveAll(e => string.Equals(e.Key, rule.Key, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
