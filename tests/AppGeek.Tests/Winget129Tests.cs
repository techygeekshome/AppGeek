using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// Regression tests built from real winget 1.29.290 output on a live machine, after AppGeek
/// offered two updates where winget itself said three.
///
/// Two separate defects were behind that, and both are pinned down here. The fixtures are
/// trimmed copies of the actual output, spacing preserved exactly - the spacing IS the bug,
/// so do not tidy it up.
/// </summary>
public static class Winget129Tests
{
    // Note the SINGLE space between "Available" and "Source". That one character is what
    // broke it. Everything else here is ordinary.
    private const string ListOutput =
        "Name                      Id                      Version        Available Source\n" +
        "---------------------------------------------------------------------------------\n" +
        "1Password                 AgileBits.1Password     > 8.12.34.34             winget\n" +
        "64Gram Desktop            64Gram.64Gram           1.2.6          1.2.7     winget\n" +
        "Amazon Music              MSIX\\AmazonMobile_9.5.0 9.5.2.0                        \n";

    // "winget upgrade" prints a second table for packages that need explicit targeting.
    private const string UpgradeOutput =
        "Name                      Id                      Version        Available Source\n" +
        "---------------------------------------------------------------------------------\n" +
        "Inno Setup 7.1.0 (32-bit) JRSoftware.InnoSetup.7  7.1.0 (32-bit) 7.1.0     winget\n" +
        "Screaming Frog SEO Spider ScreamingFrog.SEOSpider 24.0           24.3      winget\n" +
        "3 upgrades available.\n" +
        "\n" +
        "The following packages have an upgrade available, but require explicit targeting for upgrade:\n" +
        "Name           Id            Version Available Source\n" +
        "-----------------------------------------------------\n" +
        "64Gram Desktop 64Gram.64Gram 1.2.6   1.2.7     winget\n";

    public static void Run()
    {
        Check.Section("winget 1.29 - the Source column");

        var list = WingetText.ParseTable(ListOutput);
        Check.Equal("three applications are read", 3, list.Count);

        var gram = list.FirstOrDefault(r => r.Get(WingetColumn.Name) == "64Gram Desktop");
        Check.That("64Gram Desktop is found", gram is not null);

        // Before the fix this was "1.2.7     winget" - a version string with the source
        // glued onto it, which no comparison could read, so no update was ever offered.
        Check.Equal("the available version is only the version",
            "1.2.7", gram?.Get(WingetColumn.Available));
        Check.Equal("the source is read into its own column",
            "winget", gram?.Get(WingetColumn.Source));
        Check.Equal("the installed version is unaffected",
            "1.2.6", gram?.Get(WingetColumn.Version));

        // winget marks some rows with "> ", meaning it could not pin the installed version
        // down. That marker must survive: PackageMatcher treats an indefinite version as
        // "nothing to compare against" and keeps the match. Stripping it made the version
        // look definite, it then disagreed with the registry, and the match was silently
        // thrown away - which MatcherTests caught immediately.
        var onePassword = list.FirstOrDefault(r => r.Get(WingetColumn.Name) == "1Password");
        Check.Equal("a '>' marker is preserved, because the matcher depends on it",
            "> 8.12.34.34", onePassword?.Get(WingetColumn.Version));
        Check.Equal("a row with no available version leaves it empty rather than borrowing the source",
            "", onePassword?.Get(WingetColumn.Available));
        Check.Equal("...and still reports its source",
            "winget", onePassword?.Get(WingetColumn.Source));

        Check.Section("winget 1.29 - the second upgrade table");

        var upgrades = WingetText.ParseTable(UpgradeOutput);

        // The whole point: winget said three, AppGeek used to see two.
        Check.Equal("all three upgrades are read, not just the first table", 3, upgrades.Count);
        Check.That("the package needing explicit targeting is included",
            upgrades.Any(r => r.Get(WingetColumn.Id) == "64Gram.64Gram"));
        Check.That("the ordinary upgrades are still there",
            upgrades.Any(r => r.Get(WingetColumn.Id) == "JRSoftware.InnoSetup.7") &&
            upgrades.Any(r => r.Get(WingetColumn.Id) == "ScreamingFrog.SEOSpider"));

        // "3 upgrades available." parsed as an application before the fix.
        Check.That("the summary line is not mistaken for an application",
            !upgrades.Any(r => r.Get(WingetColumn.Name).Contains("upgrades available")));

        Check.Section("winget 1.29 - what must NOT change");

        // The positional fallback for translated headers has to survive. A two-word heading
        // that is not an English column name must stay one column.
        const string german =
            "Name                 Kennung              Version   Verfügbar Quelle\n" +
            "--------------------------------------------------------------------\n" +
            "Mozilla Firefox      Mozilla.Firefox      141.0     142.0     winget\n";

        var de = WingetText.ParseTable(german);
        Check.Equal("a translated table still yields a row", 1, de.Count);
        Check.Equal("a translated name column still reads", "Mozilla Firefox", de[0].Get(WingetColumn.Name));
        Check.Equal("a translated id column still reads", "Mozilla.Firefox", de[0].Get(WingetColumn.Id));
        Check.Equal("a translated version column still reads", "141.0", de[0].Get(WingetColumn.Version));

        // Genuinely duplicated names must still be dropped by the matcher - that rule is what
        // stops a wrong package being bound to an installed app, and it is not what broke
        // 64Gram.
        const string dupes =
            "Name                 Id                   Version   Available Source\n" +
            "--------------------------------------------------------------------\n" +
            "WindowsAppRuntime.2  MSIX\\Runtime.2_1     2.0.1                     \n" +
            "WindowsAppRuntime.2  MSIX\\Runtime.2_2     2.0.2                     \n";

        var dupRows = WingetText.ParseTable(dupes);
        Check.Equal("duplicate rows are still parsed, and left for the matcher to reject", 2, dupRows.Count);
    }
}
