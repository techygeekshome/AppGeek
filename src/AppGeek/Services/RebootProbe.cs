using System.Diagnostics;
using Microsoft.Win32;

namespace AppGeek.Services;

/// <summary>
/// Asks Windows whether a restart is already pending, and carries one out when the user has
/// asked for that.
///
/// The three keys checked below are the ones Windows itself sets, and they are what every
/// "is a reboot pending" script has looked at for twenty years. Reading them is cheap and
/// side-effect free — unlike WMI's Win32_Product, which is why that class is banned in this
/// codebase.
/// </summary>
public static class RebootProbe
{
    /// <summary>True when Windows is already holding a restart, whoever asked for it.</summary>
    public static bool IsRebootPending()
    {
        foreach (var (path, value) in Signals)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key is null) continue;

                if (value is null) return true;              // the key existing is the signal

                var v = key.GetValue(value);
                if (v is string[] { Length: > 0 }) return true;
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return true;
            }
            catch (Exception ex)
            {
                Log.Debug($"Reboot probe could not read {path}: {ex.Message}");
            }
        }

        return false;
    }

    private static readonly (string Path, string? Value)[] Signals =
    {
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", null),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", null),
        (@"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations")
    };

    /// <summary>
    /// Schedules a restart with a visible countdown. Windows shows its own warning, and
    /// <c>shutdown /a</c> cancels it — which is why the delay is not zero.
    /// </summary>
    public static bool Restart(TimeSpan delay, string reason)
    {
        var seconds = Math.Max(30, (int)delay.TotalSeconds);
        var message = Sanitise($"AppGeek is restarting this PC because {reason}. Run 'shutdown /a' to cancel.");

        try
        {
            Log.Info($"Restart scheduled in {seconds}s — {reason}");
            Process.Start(new ProcessStartInfo("shutdown.exe", $"/r /t {seconds} /c \"{message}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Restart could not be scheduled", ex);
            return false;
        }
    }

    public static bool Abort()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/a")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            Log.Info("Pending restart aborted.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Restart could not be aborted: " + ex.Message);
            return false;
        }
    }

    /// <summary>shutdown.exe caps the comment at 512 characters, and a stray quote breaks it.</summary>
    private static string Sanitise(string text)
    {
        var clean = text.Replace('"', '\'');
        return clean.Length <= 500 ? clean : clean[..500];
    }
}
