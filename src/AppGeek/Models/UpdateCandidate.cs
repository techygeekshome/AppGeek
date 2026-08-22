using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AppGeek.Models;

/// <summary>An installed app that has a newer version available.</summary>
public sealed class UpdateCandidate : INotifyPropertyChanged
{
    public string PackageId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Publisher { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
    public string SourceName { get; set; } = "winget";
    public long EstimatedDownloadBytes { get; set; }

    /// <summary>
    /// How the app is installed right now, taken from the registry inventory. Null when we
    /// could not work it out. An upgrade must be pinned to this, or winget will happily
    /// install a second copy at the other scope — see the note in WingetClient.ExecuteAsync.
    /// </summary>
    public InstallScope? InstalledScope { get; set; }

    /// <summary>Where the installed copy lives. Logged before an upgrade so a bad one is diagnosable.</summary>
    public string? InstallLocation { get; set; }

    /// <summary>Vendor is in our security-sensitive list (browsers, PDF readers, runtimes, etc).</summary>
    public bool IsSecuritySensitive { get; set; }
    public bool IsMajorVersionChange { get; set; }
    public bool IsPinned { get; set; }
    public string? PinnedVersion { get; set; }

    /// <summary>Set when the app currently has a running process that would block a silent update.</summary>
    public bool IsRunning { get; set; }
    public string? RunningProcessName { get; set; }

    public string IconText { get; set; } = "";
    public string IconColour { get; set; } = "#2E78D8";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string SizeDisplay => Format.Bytes(EstimatedDownloadBytes);
    public string SubtitleDisplay
    {
        get
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(Publisher)) bits.Add(Publisher!);
            bits.Add(SourceName);
            if (!string.IsNullOrWhiteSpace(PackageId)) bits.Add(PackageId);
            if (IsRunning) bits.Add($"{RunningProcessName} is running — will be skipped unless closed");
            if (IsPinned) bits.Add($"pinned to {PinnedVersion}");
            return string.Join("  ·  ", bits);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
