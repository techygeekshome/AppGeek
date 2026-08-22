using AppGeek.Models;

namespace AppGeek.Services;

/// <summary>
/// Decides which winget package, if any, an installed application corresponds to.
///
/// Deliberately kept free of any Windows-only dependency so it can be exercised directly
/// by tests — this is the piece where a wrong answer damages someone's machine.
/// </summary>
public static class PackageMatcher
{
    /// <summary>
    /// Attaches winget package IDs to registry entries.
    ///
    /// This is the most dangerous piece of matching in the app: whatever ID ends up on an
    /// InstalledApp is what gets handed to "winget upgrade --id". Bind the wrong ID and the
    /// app cheerfully installs unrelated software over something that was working.
    ///
    /// So the rules are deliberately strict, and an unmatched app is a far better outcome
    /// than a wrongly matched one — it just means AppGeek does not offer to update it.
    /// </summary>
    public static void MatchPackageIds(List<InstalledApp> installed, List<WingetRow> listed)
    {
        if (listed.Count == 0) return;

        // A name that appears more than once cannot identify anything. Drop those outright
        // rather than letting the last row silently win.
        var byName = listed
            .Where(r => !string.IsNullOrWhiteSpace(r.Get(WingetColumn.Name)))
            .GroupBy(r => r.Get(WingetColumn.Name), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);

        var ambiguous = listed.Count - byName.Count;
        if (ambiguous > 0)
            Log.Debug($"{ambiguous} winget row(s) had duplicate names and were not used for matching.");

        int matched = 0, rejected = 0;

        foreach (var app in installed)
        {
            if (app.PackageId is not null) continue;

            var row = FindRow(app, byName, ref rejected);
            if (row is null) continue;

            var id = row.Get(WingetColumn.Id);

            if (string.IsNullOrWhiteSpace(id) ||
                id.StartsWith("MSIX", StringComparison.OrdinalIgnoreCase))
                continue;

            // winget truncates over-long values with an ellipsis. A truncated ID is not an
            // ID — using it would either fail or, worse, resolve to something else.
            if (ContainsEllipsis(id))
            {
                Log.Debug($"Ignoring truncated package ID '{id}' for '{app.DisplayName}'.");
                rejected++;
                continue;
            }

            app.PackageId = id;
            var src = row.Get(WingetColumn.Source);
            app.SourceName = string.IsNullOrWhiteSpace(src) ? "winget" : src;
            matched++;
        }

        Log.Info($"Matched {matched} applications to a package source " +
                 $"({rejected} candidate(s) rejected as not confident enough).");
    }

    private static WingetRow? FindRow(InstalledApp app, Dictionary<string, WingetRow> byName, ref int rejected)
    {
        // 1. An exact name match is the only match we fully trust.
        if (byName.TryGetValue(app.DisplayName, out var exact))
            return VersionAgrees(app, exact, strict: false, ref rejected) ? exact : null;

        // 2. Otherwise the only acceptable case is a name winget itself truncated, where the
        //    surviving prefix is long enough to be meaningful. "Plex" must never match
        //    "Plex Media Server", and "Git" must never match "GitHub Desktop".
        const int minimumPrefix = 12;

        var candidates = byName.Keys
            .Where(ContainsEllipsis)
            .Select(k => (Key: k, Prefix: TrimEllipsis(k)))
            .Where(x => x.Prefix.Length >= minimumPrefix &&
                        app.DisplayName.StartsWith(x.Prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count != 1)
        {
            if (candidates.Count > 1) rejected++;
            return null;
        }

        // A truncated prefix is a weaker signal than an exact name, so the version has to
        // agree properly rather than merely being the same release line.
        var row = byName[candidates[0].Key];
        return VersionAgrees(app, row, strict: true, ref rejected) ? row : null;
    }

    /// <summary>
    /// winget reads its "installed version" from the same Add/Remove Programs entry we do,
    /// so when both sides report a version they must agree. A disagreement means the row is
    /// describing a different product, and the match is thrown away.
    /// </summary>
    private static bool VersionAgrees(InstalledApp app, WingetRow row, bool strict, ref int rejected)
    {
        var theirs = row.Get(WingetColumn.Version);
        var ours = app.DisplayVersion;

        if (string.IsNullOrWhiteSpace(theirs) || string.IsNullOrWhiteSpace(ours)) return true;
        if (ContainsEllipsis(theirs)) return true;

        // "Unknown", and winget's "> 3.13.5" form, tell us nothing to compare against.
        if (VersionCompare.IsIndefinite(theirs)) return true;

        if (strict
            ? VersionCompare.Compare(theirs, ours) == 0
            : VersionCompare.ProbablySameRelease(theirs, ours))
            return true;

        Log.Debug($"Rejected match for '{app.DisplayName}': winget reports version '{theirs}', " +
                  $"the registry reports '{ours}'.");
        rejected++;
        return false;
    }

    public static bool ContainsEllipsis(string s) => s.Contains('\u2026') || s.Contains("...");

    private static string TrimEllipsis(string s) =>
        s.Replace("\u2026", "").Replace("...", "").TrimEnd();
}
