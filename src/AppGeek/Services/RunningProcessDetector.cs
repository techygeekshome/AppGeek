using System.Diagnostics;

namespace AppGeek.Services;

/// <summary>
/// Works out whether an app is currently running, so an update can warn the user
/// instead of silently failing or killing something mid-use.
/// </summary>
public static class RunningProcessDetector
{
    private static readonly Dictionary<string, string[]> KnownProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google.Chrome"] = new[] { "chrome" },
        ["Mozilla.Firefox"] = new[] { "firefox" },
        ["Microsoft.Edge"] = new[] { "msedge" },
        ["Brave.Brave"] = new[] { "brave" },
        ["Adobe.Acrobat.Reader.64-bit"] = new[] { "AcroRd32", "Acrobat" },
        ["Notepad++.Notepad++"] = new[] { "notepad++" },
        ["VideoLAN.VLC"] = new[] { "vlc" },
        ["Zoom.Zoom"] = new[] { "Zoom" },
        ["SlackTechnologies.Slack"] = new[] { "slack" },
        ["Microsoft.VisualStudioCode"] = new[] { "Code" },
        ["Valve.Steam"] = new[] { "steam" },
        ["Discord.Discord"] = new[] { "Discord" },
        ["Spotify.Spotify"] = new[] { "Spotify" },
        ["7zip.7zip"] = new[] { "7zFM" },

        // Executable names rarely resemble display names, which is why this table exists at
        // all: "OBS Studio" runs as "obs64", and the generic fallback below has no chance of
        // guessing that. Upgrading an app while it is open fails with "files are currently
        // in use", so it is worth catching before the run starts.
        ["OBSProject.OBSStudio"] = new[] { "obs64", "obs32", "obs" },
        ["HandBrake.HandBrake"] = new[] { "HandBrake" },
        ["GitHub.GitHubDesktop"] = new[] { "GitHubDesktop" },
        ["Plex.Plex"] = new[] { "Plex" },
        ["Plex.PlexMediaServer"] = new[] { "Plex Media Server" },
        ["Microsoft.Teams"] = new[] { "ms-teams", "Teams" },
        ["Microsoft.Outlook"] = new[] { "olk", "OUTLOOK" },
        ["Telegram.TelegramDesktop"] = new[] { "Telegram" },
        ["64Gram.64Gram"] = new[] { "64Gram", "Telegram" },
        ["Mozilla.Thunderbird"] = new[] { "thunderbird" },
        ["Audacity.Audacity"] = new[] { "audacity" },
        ["GIMP.GIMP"] = new[] { "gimp-2.10", "gimp" },
        ["BlenderFoundation.Blender"] = new[] { "blender" },
        ["qBittorrent.qBittorrent"] = new[] { "qbittorrent" },
        ["OpenWhisperSystems.Signal"] = new[] { "Signal" },
        ["Dropbox.Dropbox"] = new[] { "Dropbox" },
        ["Docker.DockerDesktop"] = new[] { "Docker Desktop" },
        ["Postman.Postman"] = new[] { "Postman" },
        ["Obsidian.Obsidian"] = new[] { "Obsidian" },
        ["Rufus.Rufus"] = new[] { "rufus" },
        ["JRSoftware.InnoSetup.7"] = new[] { "Compil32", "ISCC" }
    };

    /// <summary>Returns the friendly process name if a matching process is running, else null.</summary>
    public static string? FindRunning(string? packageId, string? displayName)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(packageId) && KnownProcesses.TryGetValue(packageId!, out var known))
            candidates.AddRange(known);

        foreach (var c in candidates)
        {
            try
            {
                if (Process.GetProcessesByName(c).Length > 0) return c + ".exe";
            }
            catch { /* access denied on some processes is fine */ }
        }

        return FindByName(displayName);
    }

    /// <summary>
    /// Last resort for an app that is not in the table above. Executable names rarely match
    /// display names — "OBS Studio" runs as "obs64", "Notepad++" as "notepad++" — so this
    /// looks for a running process whose name, once an architecture suffix is stripped, is a
    /// prefix of the display name with its spaces removed.
    ///
    /// This is allowed to be a little generous. A false positive costs the user one extra
    /// "this app is running, carry on?" prompt; a false negative costs them a failed upgrade
    /// and, on a worse day, an application that was replaced while it was open.
    /// </summary>
    private static string? FindByName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;

        var collapsed = new string(displayName!.Where(char.IsLetterOrDigit).ToArray());
        if (collapsed.Length < 4) return null;

        Process[] running;
        try { running = Process.GetProcesses(); }
        catch { return null; }

        try
        {
            foreach (var p in running)
            {
                string name;
                try { name = p.ProcessName; }
                catch { continue; }

                var stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (stem.Length < 3) continue;

                var stemCollapsed = new string(stem.Where(char.IsLetterOrDigit).ToArray());
                if (stemCollapsed.Length < 3) continue;

                if (collapsed.StartsWith(stemCollapsed, StringComparison.OrdinalIgnoreCase))
                    return name + ".exe";
            }
        }
        finally
        {
            foreach (var p in running) { try { p.Dispose(); } catch { } }
        }

        return null;
    }
}
