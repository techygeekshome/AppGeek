using AppGeek.Services;

namespace AppGeek.Tests;

public static class VersionTests
{
    public static void Run()
    {
        Check.Section("Version comparison");

        Check.That("2.0 is newer than 1.9", VersionCompare.IsNewer("2.0", "1.9"));
        Check.That("1.10 is newer than 1.9 — this is not string ordering",
            VersionCompare.IsNewer("1.10", "1.9"));
        Check.That("1.0.1 is newer than 1.0", VersionCompare.IsNewer("1.0.1", "1.0"));
        Check.That("equal versions are not newer", !VersionCompare.IsNewer("1.0", "1.0"));
        Check.That("an older version is not newer", !VersionCompare.IsNewer("1.0", "2.0"));
        Check.Equal("trailing zeroes do not change the comparison", 0, VersionCompare.Compare("1.0", "1.0.0"));

        Check.Section("Major version changes");

        Check.That("1.x to 2.x is a major change", VersionCompare.IsMajorChange("1.9.9", "2.0.0"));
        Check.That("a patch bump is not a major change", !VersionCompare.IsMajorChange("1.0.0", "1.0.1"));
        Check.That("a minor bump is not a major change", !VersionCompare.IsMajorChange("1.0.0", "1.1.0"));

        Check.Section("Versions winget will not commit to");

        Check.That("\"Unknown\" is indefinite", VersionCompare.IsIndefinite("Unknown"));
        Check.That("winget's \"> 3.13.5\" form is indefinite", VersionCompare.IsIndefinite("> 3.13.5"));
        Check.That("an empty value is indefinite", VersionCompare.IsIndefinite("  "));
        Check.That("a real version is not indefinite", !VersionCompare.IsIndefinite("3.13.5"));

        Check.Section("Same release? — build numbers formatted two different ways");

        // All three of these cost a correct match under exact comparison.
        Check.That("10.0.11 and 10.0.11.50000 are the same runtime release",
            VersionCompare.ProbablySameRelease("10.0.11", "10.0.11.50000"));
        Check.That("3.14.7 and Python's 3.14.7150.0 are the same release",
            VersionCompare.ProbablySameRelease("3.14.7", "3.14.7150.0"));
        Check.That("an indefinite version cannot disagree with anything",
            VersionCompare.ProbablySameRelease("> 3.13.5", "3.14.7150.0"));

        // And a genuine difference, which must stay rejected.
        Check.That("1.51.0 and 1.52.0 are NOT the same release",
            !VersionCompare.ProbablySameRelease("1.51.0", "1.52.0"));

        Check.That("a major difference is never the same release",
            !VersionCompare.ProbablySameRelease("1.0.0", "2.0.0"));
        Check.That("a single component must match exactly",
            !VersionCompare.ProbablySameRelease("23", "24"));
    }
}
