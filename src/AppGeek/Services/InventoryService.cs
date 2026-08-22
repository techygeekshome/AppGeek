using AppGeek.Models;

namespace AppGeek.Services;

public sealed record ScanResult(
    List<InstalledApp> Installed,
    List<UpdateCandidate> Updates,
    DateTime CompletedUtc);

/// <summary>
/// Puts the pieces together: registry inventory, optional Store inventory, winget
/// package-ID matching, and the list of available updates with exclusions applied.
/// </summary>
public sealed class InventoryService
{
    private readonly RegistryInventoryService _registry = new();
    private readonly AppxInventoryService _appx = new();
    private readonly WingetClient _winget;
    private readonly SettingsService _settings;

    public InventoryService(WingetClient winget, SettingsService settings)
    {
        _winget = winget;
        _settings = settings;
    }

    public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null,
                                            CancellationToken ct = default)
    {
        // Four phases, each owning a slice of the bar. The slices are weighted by how
        // long each phase actually takes: the two winget calls dominate a real scan.
        const int total = 4;
        var readPhase   = new ScanPhase(progress, 0, 22, "Reading installed programs\u2026", 1, total);
        var storePhase  = new ScanPhase(progress, 22, 34, "Reading Microsoft Store apps\u2026", 2, total);
        var matchPhase  = new ScanPhase(progress, 34, 64, "Matching against the package catalogue\u2026", 3, total);
        var updatePhase = new ScanPhase(progress, 64, 98, "Checking for updates\u2026", 4, total);

        readPhase.Begin();
        var installed = await _registry.ScanAsync(readPhase, ct).ConfigureAwait(false);
        readPhase.Complete();
        Log.Info($"Registry scan found {installed.Count} applications.");

        if (_settings.Current.IncludeStoreApps)
        {
            var store = await _appx.ScanAsync(storePhase, ct).ConfigureAwait(false);
            // Don't list a Store app twice if it also has an uninstall entry.
            foreach (var s in store)
            {
                if (!installed.Any(i => string.Equals(i.DisplayName, s.DisplayName, StringComparison.OrdinalIgnoreCase)))
                    installed.Add(s);
            }
            Log.Info($"Store scan added {store.Count} packages.");
        }
        storePhase.Complete();

        if (_settings.Current.UseWinget && _winget.IsAvailable)
        {
            matchPhase.Begin();
            var listed = await _winget.ListAsync(matchPhase, ct).ConfigureAwait(false);
            MatchPackageIds(installed, listed);
        }
        matchPhase.Complete();

        updatePhase.Begin();
        var updates = await BuildUpdatesAsync(installed, updatePhase, ct).ConfigureAwait(false);
        updatePhase.Complete();

        installed = installed
            .Where(a => !_settings.IsIgnored(a.Key))
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _settings.Current.LastScanUtc = DateTime.UtcNow;
        _settings.Save();

        progress?.Report(new ScanProgress(100, "Scan complete", total, total));
        return new ScanResult(installed, updates, DateTime.UtcNow);
    }

    /// <summary>Attaches winget package IDs to registry entries by matching display names.</summary>
    private static void MatchPackageIds(List<InstalledApp> installed, List<WingetRow> listed) =>
        PackageMatcher.MatchPackageIds(installed, listed);

    private async Task<List<UpdateCandidate>> BuildUpdatesAsync(List<InstalledApp> installed,
                                                               ScanPhase phase, CancellationToken ct)
    {
        var updates = new List<UpdateCandidate>();
        if (!_settings.Current.UseWinget || !_winget.IsAvailable) return updates;

        var rows = await _winget.UpgradesAsync(_settings.Current.IncludeUnknownVersions, phase, ct)
                                .ConfigureAwait(false);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var id = row.Get(WingetColumn.Id);
            var name = row.Get(WingetColumn.Name);
            var current = row.Get(WingetColumn.Version);
            var available = row.Get(WingetColumn.Available);

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(available)) continue;
            // winget prints "Unknown" for apps whose installed version it cannot read.
            if (available.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) continue;

            // A truncated ID is not an ID. Never hand one to "winget upgrade".
            if (PackageMatcher.ContainsEllipsis(id))
            {
                Log.Warn($"Skipping '{name}': winget truncated its package ID to '{id}', " +
                         "so it cannot be targeted safely.");
                continue;
            }

            var key = "id:" + id.ToLowerInvariant();
            if (_settings.IsIgnored(key)) continue;

            var match = installed.FirstOrDefault(a =>
                string.Equals(a.PackageId, id, StringComparison.OrdinalIgnoreCase));

            var pin = _settings.FindPin(key);
            var source = row.Get(WingetColumn.Source);

            var candidate = new UpdateCandidate
            {
                PackageId = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name,
                Publisher = match?.Publisher,
                CurrentVersion = string.IsNullOrWhiteSpace(current) ? (match?.DisplayVersion ?? "?") : current,
                AvailableVersion = available,
                SourceName = string.IsNullOrWhiteSpace(source) ? "winget" : source,
                EstimatedDownloadBytes = match?.EstimatedSizeBytes ?? 0,
                InstalledScope = match?.Scope,
                InstallLocation = match?.InstallLocation,
                IsSecuritySensitive = SecurityRelevance.IsSecuritySensitive(name, id),
                IsPinned = pin is not null,
                PinnedVersion = pin?.PinnedVersion,
                IconText = IconFactory.Monogram(name),
                IconColour = IconFactory.Colour(id)
            };

            candidate.IsMajorVersionChange =
                VersionCompare.IsMajorChange(candidate.CurrentVersion, candidate.AvailableVersion);

            var running = RunningProcessDetector.FindRunning(id, candidate.Name);
            candidate.IsRunning = running is not null;
            candidate.RunningProcessName = running;

            // Pinned and running apps are listed but not ticked by default.
            candidate.IsSelected = !candidate.IsPinned && !candidate.IsRunning;

            updates.Add(candidate);
        }

        return updates
            .OrderByDescending(u => u.IsSecuritySensitive)
            .ThenBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
