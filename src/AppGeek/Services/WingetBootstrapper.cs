using System.Diagnostics;

namespace AppGeek.Services;

public enum WingetStatus
{
    /// <summary>Present and new enough to use.</summary>
    Ready,

    /// <summary>Present but too old for the command-line flags AppGeek relies on.</summary>
    Outdated,

    /// <summary>Not found at all.</summary>
    Missing
}

public sealed record WingetCheck(WingetStatus Status, Version? Version, string Headline, string Detail)
{
    public bool IsReady => Status == WingetStatus.Ready;
}

/// <summary>
/// AppGeek's only external dependency is the Microsoft "App Installer" package, which
/// provides winget. It ships in the box on Windows 11 and on modern Windows 10, but it
/// can be absent on LTSC, freshly imaged or sysprepped machines, in Windows Sandbox,
/// and on accounts that have only just logged in for the first time.
///
/// Rather than failing with a cryptic error, AppGeek detects this and offers to fix it.
/// </summary>
public sealed class WingetBootstrapper
{
    /// <summary>
    /// 1.4 is the floor: that is the release that added --disable-interactivity, which
    /// AppGeek passes on every call to stop winget prompting inside a redirected console.
    /// </summary>
    public static Version MinimumVersion => WingetVersion.Minimum;

    private const string PackageFamilyName = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";
    private const string StoreProtocolUrl = "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1";
    private const string StoreWebUrl = "https://apps.microsoft.com/detail/9nblggh4nns1";

    /// <summary>Microsoft's own short link to the latest App Installer bundle.</summary>
    public const string ManualDownloadUrl = "https://aka.ms/getwinget";

    private readonly WingetClient _client;

    public WingetBootstrapper(WingetClient client) => _client = client;

    public WingetCheck Check()
    {
        if (!_client.IsAvailable)
        {
            return new WingetCheck(
                WingetStatus.Missing,
                null,
                "Windows Package Manager is not available on this PC",
                "AppGeek uses Microsoft's App Installer (winget) to install and update software. " +
                "It normally comes with Windows, but it can be missing on LTSC, freshly imaged or " +
                "sysprepped machines. AppGeek can try to fix this for you.");
        }

        var version = ParseVersion(_client.Version);

        if (version is not null && version < MinimumVersion)
        {
            return new WingetCheck(
                WingetStatus.Outdated,
                version,
                $"Windows Package Manager is out of date (found {version}, need {MinimumVersion} or newer)",
                "Older versions do not support the silent, non-interactive install options AppGeek " +
                "relies on. Updating App Installer from the Microsoft Store will fix this.");
        }

        return new WingetCheck(
            WingetStatus.Ready,
            version,
            "Windows Package Manager is ready",
            version is null ? "Version could not be read, but winget responded." : $"winget {version}");
    }

    /// <summary>
    /// Kept as the name the rest of the app already calls. The parsing itself moved to
    /// <see cref="WingetVersion"/> so it could be linked into the test project, which
    /// cannot reference this class — this one shells out to PowerShell and winget.
    /// </summary>
    public static Version? ParseVersion(string? raw) => WingetVersion.Parse(raw);

    /// <summary>
    /// Attempts the cheap, offline fix first: re-registering the App Installer package for
    /// the current user. This resolves by far the most common case, where the package is
    /// present on the machine but not registered for this account, so winget.exe is missing
    /// from the WindowsApps alias folder.
    /// </summary>
    public async Task<bool> TryReRegisterAsync(CancellationToken ct = default)
    {
        Log.Info("Attempting to re-register App Installer for the current user.");

        var result = await ProcessRunner.RunAsync(
            "powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
            $"\"Add-AppxPackage -RegisterByFamilyName -MainPackage {PackageFamilyName}\"",
            ct,
            timeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);

        if (!result.Success)
        {
            Log.Warn("Re-registration failed: " + Shorten(result.Combined));
            return false;
        }

        // The probe result is cached, so force a fresh look before reporting success.
        _client.Reprobe();
        var ok = _client.IsAvailable;
        Log.Info(ok
            ? "App Installer re-registered successfully."
            : "Re-registration completed without error, but winget is still not callable — the " +
              "package is registered and still broken. Repair or reinstall is needed, not another " +
              "re-registration.");
        return ok;
    }

    /// <summary>
    /// The official bootstrap route. Pulls the Microsoft.WinGet.Client module from the
    /// PowerShell Gallery and runs Repair-WinGetPackageManager. Needs internet access and
    /// takes a minute or two, so it is offered as an explicit second step rather than
    /// being run automatically.
    /// </summary>
    public async Task<bool> TryRepairViaPowerShellAsync(CancellationToken ct = default)
    {
        Log.Info("Attempting Repair-WinGetPackageManager bootstrap.");

        // -AllUsers requires administrator mode. Sending it from an unelevated process fails
        // every single time with "-AllUsers requires administrator mode", which turns the
        // repair into a button that can never work. Ask for what we can actually have.
        var allUsers = Elevation.IsElevated ? " -AllUsers" : "";
        if (!Elevation.IsElevated)
            Log.Info("Not elevated, so the repair is being attempted for the current user only. " +
                     "If it fails, restarting AppGeek as administrator gives it a better chance.");

        var script =
            "$ProgressPreference='SilentlyContinue';" +
            "Install-PackageProvider -Name NuGet -Force -Scope CurrentUser | Out-Null;" +
            "Install-Module -Name Microsoft.WinGet.Client -Force -Scope CurrentUser -Repository PSGallery | Out-Null;" +
            $"Repair-WinGetPackageManager{allUsers}";

        var result = await ProcessRunner.RunAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
            ct,
            timeout: TimeSpan.FromMinutes(10)).ConfigureAwait(false);

        if (!result.Success)
        {
            Log.Warn("PowerShell repair failed: " + Shorten(result.Combined));
            return false;
        }

        _client.Reprobe();
        var repaired = _client.IsAvailable;
        Log.Info(repaired
            ? "Repair-WinGetPackageManager succeeded and winget is callable again."
            : "Repair-WinGetPackageManager reported success but winget is still not callable. " +
              "Reinstalling App Installer from the Microsoft Store is the next step.");
        return repaired;
    }

    /// <summary>Opens the App Installer page in the Store app, falling back to the web listing.</summary>
    public static void OpenStore()
    {
        if (TryOpen(StoreProtocolUrl)) return;
        TryOpen(StoreWebUrl);
    }

    public static void OpenManualDownload() => TryOpen(ManualDownloadUrl);

    private static bool TryOpen(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open '{url}': {ex.Message}");
            return false;
        }
    }

    private static string Shorten(string s) =>
        string.IsNullOrWhiteSpace(s) ? "(no output)" :
        s.Length <= 400 ? s.Trim() : s[..400].Trim() + "…";
}
