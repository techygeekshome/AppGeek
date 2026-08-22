namespace AppGeek.Models;

public enum InstallScope { Machine, User, Store }

/// <summary>One application discovered on this PC.</summary>
public sealed class InstalledApp
{
    public string DisplayName { get; set; } = "";
    public string? DisplayVersion { get; set; }
    public string? Publisher { get; set; }
    public DateTime? InstallDate { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public string? InstallLocation { get; set; }
    public string? UninstallString { get; set; }
    public string? QuietUninstallString { get; set; }
    public string? RegistryKey { get; set; }
    public InstallScope Scope { get; set; } = InstallScope.Machine;

    /// <summary>Package identifier from a source (e.g. "Google.Chrome"), when matched.</summary>
    public string? PackageId { get; set; }
    public string? SourceName { get; set; }

    public bool IsTracked => !string.IsNullOrWhiteSpace(PackageId);

    /// <summary>Stable key used for exclusions and de-duplication.</summary>
    public string Key => !string.IsNullOrWhiteSpace(PackageId)
        ? "id:" + PackageId!.ToLowerInvariant()
        : "name:" + DisplayName.ToLowerInvariant();

    public string SizeDisplay => Format.Bytes(EstimatedSizeBytes);

    /// <summary>Monogram and colour for the list tile, derived so every app looks deliberate.</summary>
    public string IconText => Services.IconFactory.Monogram(DisplayName);
    public string IconColour => Services.IconFactory.Colour(PackageId ?? DisplayName);
    public string ScopeDisplay => Scope switch
    {
        InstallScope.Machine => "Machine",
        InstallScope.User => "User",
        _ => "Store"
    };

    public override string ToString() => $"{DisplayName} {DisplayVersion}";
}

public static class Format
{
    public static string Bytes(long b)
    {
        if (b <= 0) return "—";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i <= 1 ? $"{v:0} {u[i]}" : $"{v:0.#} {u[i]}";
    }

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" :
        t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s" :
        $"{t.Seconds}s";
}
