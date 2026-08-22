using System.Globalization;
using AppGeek.Models;
using Microsoft.Win32;

namespace AppGeek.Services;

/// <summary>
/// Reads the Add/Remove Programs data straight out of the uninstall keys.
///
/// Deliberately does NOT use WMI's Win32_Product: querying that class triggers an
/// MSI consistency check (self-repair) on every installed product, which is slow
/// and can silently reconfigure the user's software. The registry is the correct
/// source and is what Windows itself displays in Apps and Features.
/// </summary>
public sealed class RegistryInventoryService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public Task<List<InstalledApp>> ScanAsync(ScanPhase? phase = null, CancellationToken ct = default) =>
        Task.Run(() => Scan(phase, ct), ct);

    public List<InstalledApp> Scan(ScanPhase? phase = null, CancellationToken ct = default)
    {
        var results = new List<InstalledApp>();

        // Four (hive, view) combinations, each an equal slice of this phase.
        var sources = new (RegistryHive Hive, RegistryView View, InstallScope Scope)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, InstallScope.Machine),
            (RegistryHive.LocalMachine, RegistryView.Registry32, InstallScope.Machine),
            (RegistryHive.CurrentUser,  RegistryView.Registry64, InstallScope.User),
            (RegistryHive.CurrentUser,  RegistryView.Registry32, InstallScope.User),
        };

        for (int i = 0; i < sources.Length; i++)
        {
            var (hive, view, scope) = sources[i];
            int index = i;
            Read(hive, view, scope, results, ct,
                 // Within a source, report the fraction of its subkeys read.
                 inner => phase?.Report((index + inner) / (double)sources.Length));
            phase?.Report((i + 1) / (double)sources.Length);
        }

        // The same product can appear under both registry views; keep one of each.
        return results
            .GroupBy(a => (a.DisplayName.ToLowerInvariant(), a.DisplayVersion ?? "", a.Scope))
            .Select(g => g.First())
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void Read(RegistryHive hive, RegistryView view, InstallScope scope,
                             List<InstalledApp> into, CancellationToken ct,
                             Action<double>? onFraction = null)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallPath);
            if (uninstall is null) return;

            var subKeyNames = uninstall.GetSubKeyNames();
            int done = 0;

            foreach (var subName in subKeyNames)
            {
                ct.ThrowIfCancellationRequested();

                // Reporting every key would flood the UI thread; every 25 is plenty.
                if (++done % 25 == 0 && subKeyNames.Length > 0)
                    onFraction?.Invoke(done / (double)subKeyNames.Length);

                try
                {
                    using var key = uninstall.OpenSubKey(subName);
                    if (key is null) continue;

                    var app = FromKey(key, subName, scope, hive, view);
                    if (app is not null) into.Add(app);
                }
                catch (Exception ex)
                {
                    Log.Debug($"Skipped uninstall key {subName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read {hive}\\{UninstallPath} ({view}): {ex.Message}");
        }
    }

    private static InstalledApp? FromKey(RegistryKey key, string subName, InstallScope scope,
                                         RegistryHive hive, RegistryView view)
    {
        var name = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name)) return null;

        // Hidden plumbing, and Windows/driver updates, are not "applications".
        if (ToInt(key.GetValue("SystemComponent")) == 1) return null;
        var releaseType = key.GetValue("ReleaseType") as string;
        if (!string.IsNullOrWhiteSpace(releaseType) &&
            releaseType.Contains("update", StringComparison.OrdinalIgnoreCase)) return null;
        if (key.GetValue("ParentKeyName") is string p && !string.IsNullOrWhiteSpace(p)) return null;

        var uninstallString = key.GetValue("UninstallString") as string;
        var quiet = key.GetValue("QuietUninstallString") as string;
        if (string.IsNullOrWhiteSpace(uninstallString) && string.IsNullOrWhiteSpace(quiet)) return null;

        return new InstalledApp
        {
            DisplayName = name!.Trim(),
            DisplayVersion = (key.GetValue("DisplayVersion") as string)?.Trim(),
            Publisher = (key.GetValue("Publisher") as string)?.Trim(),
            InstallDate = ParseInstallDate(key.GetValue("InstallDate") as string),
            // EstimatedSize is stored in KB.
            EstimatedSizeBytes = ToInt(key.GetValue("EstimatedSize")) * 1024L,
            InstallLocation = (key.GetValue("InstallLocation") as string)?.Trim(),
            UninstallString = uninstallString,
            QuietUninstallString = quiet,
            RegistryKey = $"{hive}\\{UninstallPath}\\{subName} ({view})",
            Scope = scope
        };
    }

    private static int ToInt(object? o)
    {
        if (o is null) return 0;
        try { return Convert.ToInt32(o, CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    private static DateTime? ParseInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Almost always yyyyMMdd.
        if (DateTime.TryParseExact(raw.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d)) return d;
        return DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d2) ? d2 : null;
    }
}
