using System.Diagnostics;

namespace AppGeek.Services;

public enum CloseOutcome
{
    /// <summary>Nothing of that name was running to begin with.</summary>
    NotRunning,

    /// <summary>It closed on its own after being asked.</summary>
    Closed,

    /// <summary>It was asked and did not go. Nothing was forced.</summary>
    StillRunning
}

/// <summary>
/// Asks a running application to close before its update runs.
///
/// It asks. It does not kill.
///
/// <see cref="Process.CloseMainWindow"/> posts WM_CLOSE, which is the same thing clicking the
/// X does: the app gets to prompt about unsaved work and shut down properly. If it is still
/// there when the grace period is up, AppGeek gives up and skips the update. That is the
/// right trade — a skipped update costs the user one more click later, whereas killing an
/// editor mid-document costs them their work, and killing something mid-write is how the
/// 2026-08-21 incident happened. Nothing in AppGeek force-terminates anything.
/// </summary>
public static class RunningAppCloser
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(20);

    /// <param name="processName">Either "chrome" or "chrome.exe"; both are accepted.</param>
    public static async Task<CloseOutcome> TryCloseAsync(string? processName, TimeSpan? grace = null,
                                                         CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(processName)) return CloseOutcome.NotRunning;

        var name = processName!.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        Process[] found;
        try { found = Process.GetProcessesByName(name); }
        catch (Exception ex)
        {
            Log.Warn($"Could not look for '{name}': {ex.Message}");
            return CloseOutcome.NotRunning;
        }

        if (found.Length == 0) return CloseOutcome.NotRunning;

        try
        {
            foreach (var p in found)
            {
                try
                {
                    // No main window means no polite way to ask — a tray helper or a service.
                    // Leave it alone rather than reaching for Kill.
                    if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
                }
                catch (Exception ex) { Log.Debug($"CloseMainWindow on {name} failed: {ex.Message}"); }
            }

            var deadline = DateTime.UtcNow + (grace ?? DefaultGrace);
            while (DateTime.UtcNow < deadline)
            {
                if (ct.IsCancellationRequested) break;
                if (!AnyAlive(name)) return CloseOutcome.Closed;
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }

            return AnyAlive(name) ? CloseOutcome.StillRunning : CloseOutcome.Closed;
        }
        finally
        {
            foreach (var p in found) { try { p.Dispose(); } catch { } }
        }
    }

    private static bool AnyAlive(string name)
    {
        Process[] alive;
        try { alive = Process.GetProcessesByName(name); }
        catch { return false; }

        try { return alive.Length > 0; }
        finally { foreach (var p in alive) { try { p.Dispose(); } catch { } } }
    }
}
