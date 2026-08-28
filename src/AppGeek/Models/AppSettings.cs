using System.Text.Json.Serialization;

namespace AppGeek.Models;

public enum RunningAppPolicy { Ask, AlwaysClose, AlwaysSkip }
public enum RebootPolicy { PromptAtEnd, Never, Automatic }
public enum ExclusionKind { Ignore, Pin }

public sealed class ExclusionRule
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("kind")] public ExclusionKind Kind { get; set; } = ExclusionKind.Ignore;
    [JsonPropertyName("pinnedVersion")] public string? PinnedVersion { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }

    [JsonIgnore]
    public string Summary => Kind == ExclusionKind.Pin
        ? $"Pinned to {PinnedVersion} — never offered for update"
        : "Hidden from scans and reports";
}

public sealed class AppSettings
{
    // Scanning
    [JsonPropertyName("autoScan")] public bool AutoScan { get; set; } = true;
    [JsonPropertyName("scanScheduleCron")] public string ScanSchedule { get; set; } = "Daily at 03:00";
    [JsonPropertyName("notifyOnUpdates")] public bool NotifyOnUpdates { get; set; } = true;
    [JsonPropertyName("includeStoreApps")] public bool IncludeStoreApps { get; set; } = true;
    [JsonPropertyName("includeUnknownVersions")] public bool IncludeUnknownVersions { get; set; } = true;

    // Sources
    [JsonPropertyName("useWinget")] public bool UseWinget { get; set; } = true;
    [JsonPropertyName("useMicrosoftStore")] public bool UseMicrosoftStore { get; set; } = true;
    [JsonPropertyName("useTghCatalogue")] public bool UseTghCatalogue { get; set; } = true;
    /// <summary>
    /// Optional. The catalogue is embedded in the exe and works offline; this is only
    /// where a newer copy is fetched from, so the app list can change without shipping a
    /// new build. Served straight from the GitHub repo, so there is nothing to host.
    /// </summary>
    [JsonPropertyName("catalogueUrl")] public string CatalogueUrl { get; set; } = DefaultCatalogueUrl;

    public const string DefaultCatalogueUrl =
        "https://raw.githubusercontent.com/techygeekshome/AppGeek/main/catalogue.json";

    /// <summary>A URL that was shipped as a default but never actually existed.</summary>
    public const string RetiredCatalogueUrl =
        "https://techygeekshome.info/appgeek/catalogue.json";

    // Install behaviour
    [JsonPropertyName("runningAppPolicy")] public RunningAppPolicy RunningAppPolicy { get; set; } = RunningAppPolicy.Ask;
    [JsonPropertyName("createRestorePoint")] public bool CreateRestorePoint { get; set; } = true;
    [JsonPropertyName("rebootPolicy")] public RebootPolicy RebootPolicy { get; set; } = RebootPolicy.PromptAtEnd;
    [JsonPropertyName("keepInstallers")] public bool KeepInstallers { get; set; }

    // Rules
    [JsonPropertyName("exclusions")] public List<ExclusionRule> Exclusions { get; set; } = new();

    // Housekeeping
    [JsonPropertyName("firstRunComplete")] public bool FirstRunComplete { get; set; }
    [JsonPropertyName("lastScanUtc")] public DateTime? LastScanUtc { get; set; }
}
