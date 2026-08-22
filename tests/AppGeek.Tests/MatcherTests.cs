using AppGeek.Models;
using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// PackageMatcher decides what ID gets handed to "winget upgrade --id". A wrong answer here
/// installs unrelated software over something that was working, so an unmatched app is
/// always the better failure.
/// </summary>
public static class MatcherTests
{
    public static void Run()
    {
        Check.Section("Package matching — never match the wrong product");

        // A naive prefix test matches "Plex" against "Plex Media Server" — two different
        // products, one of which would then be "upgraded" with the other.
        var apps = Installed(("Plex Media Server", "1.41.0"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name  Id            Version  Source",
            "Plex  Plex.Plex     1.41.0   winget"));
        Check.Equal("'Plex' does not match 'Plex Media Server'", null, apps[0].PackageId);

        apps = Installed(("GitHub Desktop", "3.4.2"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name  Id            Version  Source",
            "Git   Git.Git       3.4.2    winget"));
        Check.Equal("'Git' does not match 'GitHub Desktop'", null, apps[0].PackageId);

        Check.Section("Package matching — accept what should be accepted");

        apps = Installed(("Google Chrome", "128.0.6613.120"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name           Id             Version          Source",
            "Google Chrome  Google.Chrome  128.0.6613.120   winget"));
        Check.Equal("an exact name match with an agreeing version is taken",
            "Google.Chrome", apps[0].PackageId);
        Check.Equal("the source travels with the match", "winget", apps[0].SourceName);

        // winget truncates long names. A long surviving prefix is trustworthy.
        apps = Installed(("Microsoft Visual Studio Code", "1.92.0"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name                     Id                        Version  Source",
            "Microsoft Visual Stud…   Microsoft.VisualStudioCode 1.92.0  winget"));
        Check.Equal("a long truncated prefix matches",
            "Microsoft.VisualStudioCode", apps[0].PackageId);

        Check.Section("Package matching — reject what cannot be trusted");

        apps = Installed(("Notepad++", "8.6.9"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name       Id             Version  Source",
            "Notepad++  Notepad++.Not… 8.6.9    winget"));
        Check.Equal("a truncated package ID is refused", null, apps[0].PackageId);

        apps = Installed(("7-Zip", "23.01"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name   Id            Version  Source",
            "7-Zip  7zip.7zip     19.00    winget"));
        Check.Equal("a version disagreement throws the match away", null, apps[0].PackageId);

        apps = Installed(("Zoom", "6.1.0"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name  Id             Version  Source",
            "Zoom  Zoom.Zoom      6.1.0    winget",
            "Zoom  Zoom.ZoomAdmin 6.1.0    winget"));
        Check.Equal("a duplicated name identifies nothing and is dropped", null, apps[0].PackageId);

        apps = Installed(("Some Store App", "1.0"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name            Id                Version  Source",
            "Some Store App  MSIX\\SomeStoreApp 1.0      msstore"));
        Check.Equal("an MSIX pseudo-ID is not a package ID", null, apps[0].PackageId);

        apps = Installed(("Slack", "4.39.90"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name   Id                       Version  Source",
            "Slack  SlackTechnologies.Slack  Unknown  winget"));
        Check.Equal("an unknown version on winget's side is not a disagreement",
            "SlackTechnologies.Slack", apps[0].PackageId);

        Check.Section("Package matching — build-number formatting is not a disagreement");

        // Three cases where winget and the registry format the same release differently.
        // Exact version matching rejected all three, losing correct matches.
        apps = Installed(("Microsoft Windows Desktop Runtime 10.0.11 (x64)", "10.0.11.50000"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name                                             Id                                  Version  Source",
            "Microsoft Windows Desktop Runtime 10.0.11 (x64)  Microsoft.DotNet.DesktopRuntime.10  10.0.11  winget"));
        Check.Equal("a runtime reported as 10.0.11 still matches registry 10.0.11.50000",
            "Microsoft.DotNet.DesktopRuntime.10", apps[0].PackageId);

        apps = Installed(("Python 3.14.7 (64-bit)", "3.14.7150.0"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name                   Id                 Version  Source",
            "Python 3.14.7 (64-bit)  Python.Python.3.14  3.14.7   winget"));
        Check.Equal("Python's odd registry build number does not break the match",
            "Python.Python.3.14", apps[0].PackageId);

        apps = Installed(("Python Launcher", "3.14.7150.0"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name             Id                  Version    Source",
            "Python Launcher  Python.Launcher     > 3.13.5   winget"));
        Check.Equal("winget's \"> 3.13.5\" is not treated as a disagreement",
            "Python.Launcher", apps[0].PackageId);

        // A genuinely different release. This one must stay rejected.
        apps = Installed(("eSpeak NG Text-to-Speech 64-bit", "1.51.0"));
        PackageMatcher.MatchPackageIds(apps, Table(
            "Name                             Id            Version  Source",
            "eSpeak NG Text-to-Speech 64-bit  eSpeak.eSpeak  1.52.0   winget"));
        Check.Equal("a genuine release difference is still refused", null, apps[0].PackageId);

        Check.Section("Package matching — ellipsis detection");

        Check.That("a unicode ellipsis is detected", PackageMatcher.ContainsEllipsis("Microsoft…"));
        Check.That("three full stops are detected", PackageMatcher.ContainsEllipsis("Microsoft..."));
        Check.That("an ordinary name is not flagged", !PackageMatcher.ContainsEllipsis("Microsoft Edge"));
    }

    private static List<InstalledApp> Installed(params (string Name, string Version)[] apps) =>
        apps.Select(a => new InstalledApp { DisplayName = a.Name, DisplayVersion = a.Version }).ToList();

    /// <summary>Builds real winget table text so the parser is exercised alongside the matcher.</summary>
    private static List<WingetRow> Table(string header, params string[] rows)
    {
        var separator = new string('-', header.Length);
        return WingetText.ParseTable(string.Join("\n", new[] { header, separator }.Concat(rows)));
    }
}
