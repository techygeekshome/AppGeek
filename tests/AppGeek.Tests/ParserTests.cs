using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// winget has no stable machine-readable output, so AppGeek parses a fixed-width console
/// table. Five real defects were found in this parser before it ever shipped; these tests
/// are what stops the sixth.
/// </summary>
public static class ParserTests
{
    public static void Run()
    {
        Check.Section("winget table parsing");

        var rows = WingetText.ParseTable(string.Join("\n",
            "Name           Id             Version   Available  Source",
            "--------------------------------------------------------",
            "Google Chrome  Google.Chrome  128.0.1   129.0.2    winget",
            "7-Zip          7zip.7zip      23.01     24.08      winget"));

        Check.Equal("both data rows are read", 2, rows.Count);
        Check.Equal("the name column is read", "Google Chrome", rows[0].Get(WingetColumn.Name));
        Check.Equal("the id column is read", "Google.Chrome", rows[0].Get(WingetColumn.Id));
        Check.Equal("the installed version is read", "128.0.1", rows[0].Get(WingetColumn.Version));
        Check.Equal("the available version is read", "129.0.2", rows[0].Get(WingetColumn.Available));
        Check.Equal("the source is read", "winget", rows[0].Get(WingetColumn.Source));

        Check.Section("winget table parsing — real-world noise");

        // winget prints a progress spinner before the table, on the same line, using CR.
        rows = WingetText.ParseTable(string.Join("\n",
            "  \\\r  |\r  /\rName  Id        Version",
            "-----------------------------",
            "7-Zip 7zip.7zip 23.01"));
        Check.Equal("a spinner carriage-returned onto the header line is stripped",
            1, rows.Count);

        rows = WingetText.ParseTable(string.Join("\n",
            "Name  Id        Version",
            "-----------------------",
            "7-Zip 7zip.7zip 23.01",
            "",
            "3 upgrades available."));
        Check.Equal("the trailing summary text is not read as a row", 1, rows.Count);

        rows = WingetText.ParseTable(string.Join("\n",
            "Name  Id        Version",
            "-----------------------",
            "7-Zip 7zip.7zip 23.01",
            "-----------------------",
            "Other Other.App 1.0"));
        Check.Equal("a second table below the first is not merged into it", 1, rows.Count);

        Check.Equal("empty output yields no rows", 0, WingetText.ParseTable("").Count);
        Check.Equal("output with no separator yields no rows",
            0, WingetText.ParseTable("Nothing to see here").Count);

        Check.Section("winget table parsing — translated headers");

        // On a non-English Windows the headers are translated, so roles have to fall back
        // to winget's documented column order. This was a live bug.
        rows = WingetText.ParseTable(string.Join("\n",
            "Nome           ID             Versione  Disponibile  Origine",
            "-----------------------------------------------------------",
            "Google Chrome  Google.Chrome  128.0.1   129.0.2      winget"));
        Check.Equal("a translated name header still yields the name",
            "Google Chrome", rows[0].Get(WingetColumn.Name));
        Check.Equal("a translated version header still yields the version",
            "128.0.1", rows[0].Get(WingetColumn.Version));
        Check.Equal("a translated available header still yields the available version",
            "129.0.2", rows[0].Get(WingetColumn.Available));

        Check.Section("Progress percentage parsing");

        Check.Equal("a plain percentage is read", 42, WingetText.ParsePercent("  42% downloading"));
        Check.Equal("100% is read", 100, WingetText.ParsePercent("100%"));
        Check.Equal("a line with no percentage yields nothing", null, WingetText.ParsePercent("Starting install"));
        Check.Equal("an impossible percentage is refused", null, WingetText.ParsePercent("420%"));
    }
}
