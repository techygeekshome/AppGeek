using System.Reflection;
using System.Text.Json;
using AppGeek.Models;

namespace AppGeek.Services;

/// <summary>
/// Supplies the browsable app catalogue. The shipped copy is embedded in the exe so
/// the app works offline on a fresh PC; a newer copy can be pulled from the
/// TechyGeeksHome URL and cached, which lets the list change without a new release.
/// </summary>
public sealed class CatalogueService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public List<CatalogueApp> Apps { get; private set; } = new();
    public DateTime? LastSynced { get; private set; }
    public string SourceDescription { get; private set; } = "built-in";

    public IReadOnlyList<string> Categories =>
        Apps.Select(a => a.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public void Load()
    {
        // Prefer a cached download, fall back to the embedded copy.
        if (TryLoadFile(AppPaths.CatalogueCacheFile, "cached download")) return;
        LoadEmbedded();
    }

    private bool TryLoadFile(string path, string description)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var file = JsonSerializer.Deserialize<CatalogueFile>(File.ReadAllText(path), Options);
            if (file?.Apps is not { Count: > 0 }) return false;

            Apps = file.Apps;
            SourceDescription = description;
            LastSynced = File.GetLastWriteTime(path);
            Log.Info($"Catalogue loaded from {description}: {Apps.Count} apps.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Catalogue at {path} unreadable: {ex.Message}");
            return false;
        }
    }

    private void LoadEmbedded()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resource = asm.GetManifestResourceNames()
                              .FirstOrDefault(n => n.EndsWith("catalogue.json", StringComparison.OrdinalIgnoreCase));
            if (resource is null) { Log.Warn("Embedded catalogue missing."); return; }

            using var stream = asm.GetManifestResourceStream(resource)!;
            var file = JsonSerializer.Deserialize<CatalogueFile>(stream, Options);
            Apps = file?.Apps ?? new List<CatalogueApp>();
            SourceDescription = "built-in";
            Log.Info($"Catalogue loaded from built-in copy: {Apps.Count} apps.");
        }
        catch (Exception ex)
        {
            Log.Error("Embedded catalogue could not be read", ex);
            Apps = new List<CatalogueApp>();
        }
    }

    /// <summary>Downloads a fresh catalogue. Failure is non-fatal: the old list stays in use.</summary>
    public async Task<bool> RefreshAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AppGeek/1.0 (+techygeekshome.info)");
            var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);

            var file = JsonSerializer.Deserialize<CatalogueFile>(json, Options);
            if (file?.Apps is not { Count: > 0 })
            {
                Log.Warn("Downloaded catalogue was empty; keeping the current one.");
                return false;
            }

            await File.WriteAllTextAsync(AppPaths.CatalogueCacheFile, json, ct).ConfigureAwait(false);
            Apps = file.Apps;
            SourceDescription = "downloaded";
            LastSynced = DateTime.Now;
            Log.Info($"Catalogue refreshed: {Apps.Count} apps.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Catalogue refresh failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>Ticks the "installed" flag on catalogue entries using the current inventory.</summary>
    public void MarkInstalled(IEnumerable<InstalledApp> installed)
    {
        var ids = new HashSet<string>(
            installed.Where(a => a.PackageId is not null).Select(a => a.PackageId!),
            StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(
            installed.Select(a => a.DisplayName), StringComparer.OrdinalIgnoreCase);

        foreach (var app in Apps)
        {
            app.IsInstalled =
                (app.WingetId is not null && ids.Contains(app.WingetId)) ||
                (app.StoreId is not null && ids.Contains(app.StoreId)) ||
                names.Any(n => n.StartsWith(app.Name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
