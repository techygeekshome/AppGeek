using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AppGeek.Models;

public enum RunItemState { Queued, Running, Succeeded, Failed, Skipped, Cancelled }
public enum RunAction { Install, Update, Uninstall }

/// <summary>A single unit of work inside an install/update run.</summary>
public sealed class RunItem : INotifyPropertyChanged
{
    public string PackageId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SourceName { get; init; } = "winget";
    public RunAction Action { get; init; } = RunAction.Install;
    public string? FromVersion { get; init; }
    public string? ToVersion { get; init; }
    public long EstimatedBytes { get; init; }
    public string IconText { get; init; } = "";
    public string IconColour { get; init; } = "#2E78D8";
    public bool IsSecuritySensitive { get; init; }

    /// <summary>
    /// The scope the app is already installed at. Upgrades are pinned to it so winget
    /// cannot relocate the app to the other scope. Null for fresh installs, and for
    /// upgrades where the registry entry could not be identified.
    /// </summary>
    public InstallScope? Scope { get; init; }

    /// <summary>Where the installed copy lives, if known. Written to the run log before we touch it.</summary>
    public string? InstallLocation { get; init; }

    private RunItemState _state = RunItemState.Queued;
    public RunItemState State
    {
        get => _state;
        set { if (_state != value) { _state = value; Raise(); Raise(nameof(StatusText)); Raise(nameof(IsRunning)); } }
    }

    private int _percent;
    public int Percent { get => _percent; set { if (_percent != value) { _percent = value; Raise(); } } }

    private string _detail = "Queued";
    public string Detail { get => _detail; set { if (_detail != value) { _detail = value; Raise(); } } }

    private int _exitCode;
    public int ExitCode { get => _exitCode; set { _exitCode = value; Raise(); } }

    public TimeSpan Elapsed { get; set; }

    public bool IsRunning => State == RunItemState.Running;

    public string StatusText => State switch
    {
        RunItemState.Queued => "Waiting",
        RunItemState.Running => $"{Percent}%",
        RunItemState.Succeeded => "Done",
        RunItemState.Failed => "Failed",
        RunItemState.Skipped => "Skipped",
        _ => "Cancelled"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
