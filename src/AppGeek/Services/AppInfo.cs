using System.Diagnostics;
using System.Reflection;

namespace AppGeek.Services;

public sealed record Credit(string Name, string Licence, string Url);

/// <summary>
/// One place for the app's identity, matching the shared metadata object the rest of
/// the Geek range uses. The About window and the update checker both read from here,
/// so they can never disagree about what shipped.
/// </summary>
public static class AppInfo
{
    public const string Name = "AppGeek";
    public const string Tagline = "Free app updater and installer for Windows";
    public const string Publisher = "TechyGeeksHome";

    public const string Description =
        "Scan everything installed on this PC, see what is out of date, and update it in one " +
        "run. Browse a catalogue of common Windows software and install it silently — no " +
        "hunting for download links, no next-next-finish.";

    public const string LicenceLine =
        "Free to use, including at work. No paid tier, no subscription, no upsell. Nothing " +
        "about your machine is uploaded anywhere. If it saved you time, a donation is welcome " +
        "but never expected.";

    public const string LicenceName = "GNU General Public License v3.0";
    public const string LicenceUrl = "https://www.gnu.org/licenses/gpl-3.0.html";

    /// <summary>
    /// The GPL asks that a program with a graphical interface show this notice
    /// somewhere prominent — the About box is the conventional place.
    /// </summary>
    public const string LicenceNotice =
        "AppGeek is free software: you can redistribute it and modify it under the terms of " +
        "the GNU General Public License version 3, as published by the Free Software " +
        "Foundation. It comes with ABSOLUTELY NO WARRANTY.";

    public const string GitHubOwner = "techygeekshome";
    public const string GitHubRepo = "AppGeek";

    public const string WebsiteUrl = "https://techygeekshome.info";
    public const string ProductUrl = "https://techygeekshome.info/appgeek/";
    public const string SourceUrl = "https://github.com/techygeekshome/AppGeek";
    public const string IssuesUrl = "https://github.com/techygeekshome/AppGeek/issues";

    /// <summary>The standard donation link for the whole range.</summary>
    public const string DonateUrl = "https://ko-fi.com/techygeekshome";

    public static readonly Credit[] Credits =
    {
        new(".NET 8 and WPF", "MIT", "https://dotnet.microsoft.com/"),
        new("Windows Package Manager (winget)", "MIT", "https://github.com/microsoft/winget-cli")
    };

    public static readonly Credit Licence = new(LicenceName, "GPL-3.0", LicenceUrl);

    /// <summary>Read from the assembly, so it always matches the binary that shipped.</summary>
    public static string CurrentVersionText =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static string VersionLine => $"Version {CurrentVersionText}  ·  {Publisher}";

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open '{url}': {ex.Message}");
        }
    }
}
