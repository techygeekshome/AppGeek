using System.Diagnostics;
using AppGeek.Models;

namespace AppGeek.Services;

/// <summary>
/// Wraps the Windows Package Manager CLI.
///
/// The CLI is used rather than the COM API (Microsoft.Management.Deployment) purely
/// to keep v1 dependency-free and easy to build. The service boundary here is
/// deliberately narrow so it can be swapped for the COM API later without the rest
/// of the app noticing.
/// </summary>
public sealed class WingetClient
{
    private string? _exePath;
    private bool _probed;

    private const string CommonArgs = "--disable-interactivity --accept-source-agreements";

    public string? ExePath
    {
        get { Probe(); return _exePath; }
    }

    public bool IsAvailable => ExePath is not null;

    public string? Version { get; private set; }

    /// <summary>
    /// Clears the cached probe result and looks for winget again. Used after the
    /// bootstrapper repairs a missing App Installer, so the app notices immediately
    /// instead of asking the user to restart it.
    /// </summary>
    public void Reprobe()
    {
        _probed = false;
        _exePath = null;
        Version = null;
        Probe();
    }

    private void Probe()
    {
        if (_probed) return;
        _probed = true;

        // winget lives in the per-user WindowsApps alias folder, which is on PATH for
        // interactive sessions but not always for services or scheduled tasks.
        var candidates = new List<string>
        {
            "winget.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Microsoft", "WindowsApps", "winget.exe")
        };

        foreach (var c in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(c, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(8000);
                if (p.ExitCode == 0)
                {
                    _exePath = c;
                    Version = WingetText.Clean(output).Trim();
                    Log.Info($"winget found at '{c}' ({Version})");
                    return;
                }
            }
            catch { /* try the next candidate */ }
        }

        Log.Warn("winget was not found on this PC. Install 'App Installer' from the Microsoft Store.");
    }

    /// <summary>Everything winget can see as installed, used to attach package IDs to registry entries.</summary>
    public async Task<List<WingetRow>> ListAsync(ScanPhase? phase = null, CancellationToken ct = default)
    {
        if (!IsAvailable) return new List<WingetRow>();

        // winget gives no progress of its own, but it does stream output. Counting
        // the lines as they arrive is a real signal that work is happening, which is
        // what stops the UI looking frozen during a slow catalogue query.
        int lines = 0;
        var r = await ProcessRunner.RunAsync(ExePath!, $"list {CommonArgs}", ct,
            onOutputLine: _ => phase?.ReportUncounted(++lines),
            timeout: TimeSpan.FromMinutes(4)).ConfigureAwait(false);

        if (!r.Success) Log.Warn($"winget list exited {r.ExitCode}: {Trim(r.StdErr)}");
        phase?.Complete();
        return WingetText.ParseTable(r.StdOut);
    }

    /// <summary>Packages with a newer version available.</summary>
    public async Task<List<WingetRow>> UpgradesAsync(bool includeUnknown, ScanPhase? phase = null,
                                                     CancellationToken ct = default)
    {
        if (!IsAvailable) return new List<WingetRow>();

        var args = $"upgrade {CommonArgs}" + (includeUnknown ? " --include-unknown" : "");
        int lines = 0;
        var r = await ProcessRunner.RunAsync(ExePath!, args, ct,
            onOutputLine: _ => phase?.ReportUncounted(++lines),
            timeout: TimeSpan.FromMinutes(4)).ConfigureAwait(false);

        if (!r.Success && r.ExitCode != 0)
            Log.Debug($"winget upgrade exited {r.ExitCode}");
        phase?.Complete();
        return WingetText.ParseTable(r.StdOut);
    }

    public async Task<List<WingetRow>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(query)) return new List<WingetRow>();
        var r = await ProcessRunner.RunAsync(ExePath!, $"search \"{query}\" {CommonArgs}", ct,
            timeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        return WingetText.ParseTable(r.StdOut);
    }

    /// <summary>Installs or upgrades a package silently, reporting progress as winget emits it.</summary>
    public async Task<ProcessResult> ExecuteAsync(
        RunAction action,
        string packageId,
        string source,
        InstallScope? installedScope,
        Action<string, int?> onProgress,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
            return new ProcessResult(-1, "", "winget is not available on this PC.");

        var verb = action == RunAction.Update ? "upgrade" : "install";
        var args =
            $"{verb} --id \"{packageId}\" --exact --silent " +
            $"--accept-package-agreements --accept-source-agreements --disable-interactivity";

        if (!string.IsNullOrWhiteSpace(source))
            args += $" --source {source}";

        // ---------------------------------------------------------------------------------
        // Pin the install scope. Do not remove this.
        //
        // AppGeek runs elevated, so winget picks the machine-wide installer by default.
        // Run that against an app that is installed per-user and it is not an upgrade at
        // all: it installs a *second*, machine-wide copy under Program Files, and the
        // per-user copy under %LOCALAPPDATA% is left orphaned or removed. Every shortcut
        // the user already has still points into %LOCALAPPDATA%, so they lose their icon
        // and stop launching. This is not theoretical — it has been observed on browsers
        // and media apps, which are commonly per-user installs.
        //
        // Pinning --scope to whatever is already installed is what keeps an upgrade an
        // upgrade. If no installer exists at that scope, winget fails with 0x8A150011 and
        // we report it honestly — a refused update is always better than a moved app.
        // ---------------------------------------------------------------------------------
        if (action == RunAction.Update)
        {
            var scopeFlag = InstallScopePolicy.ScopeFlag(installedScope, source);
            if (scopeFlag is not null)
            {
                args += $" --scope {scopeFlag}";
            }
            else if (installedScope is null)
            {
                Log.Warn($"{packageId}: the installed scope could not be determined, so the " +
                         "upgrade runs unscoped. If this app turns out to be a per-user " +
                         "install, winget may relocate it.");
            }
        }

        Log.Info($"{packageId}: installed scope {installedScope?.ToString() ?? "unknown"}");
        Log.Info($"winget {args}");

        // No cancellation token, and NeverKill: once an installer is running it is allowed
        // to finish. Stopping a run prevents the next package from starting; it must never
        // interrupt the one in flight.
        return await ProcessRunner.RunAsync(
            ExePath!, args, CancellationToken.None,
            onOutputLine: line =>
            {
                var pct = WingetText.ParsePercent(line);
                onProgress(line, pct);
            },
            timeout: TimeSpan.FromMinutes(30),
            abortPolicy: ProcessAbortPolicy.NeverKill).ConfigureAwait(false);
    }

    /// <summary>
    /// As DescribeExitCode, but able to explain the failures the scope pin causes on purpose.
    /// </summary>
    public static string DescribeExitCode(int code, InstallScope? scope) =>
        InstallScopePolicy.DescribeScopeRefusal(code, scope) ?? DescribeExitCode(code);

    /// <summary>Maps winget's documented exit codes onto something a human can act on.</summary>
    public static string DescribeExitCode(int code) => unchecked((uint)code) switch
    {
        0 => "Completed successfully.",
        0x8A150011 => "No applicable installer found for this system.",
        0x8A150014 => "Another installation is already in progress.",
        0x8A150056 => "The package is already installed and up to date.",
        0x8A15002B => "No package matched that identifier.",
        0x8A150109 => "The install needs a restart to finish.",
        // winget's own wording is "Files modified by the installer are currently in use":
        // the application was running and the installer refused rather than replacing files
        // underneath it. Common, and useless as a raw hex code.
        0x8A150111 => "The app is open, and its files are in use. Close it and try again.",
        0x80070005 => "Access denied — administrator rights are required.",
        0x80070652 => "Another Windows Installer operation is in progress.",
        _ => code == 0 ? "Completed successfully." : $"Installer returned exit code 0x{code:X8}."
    };

    private static string Trim(string s) =>
        s.Length <= 300 ? s.Trim() : s[..300].Trim() + "…";
}
