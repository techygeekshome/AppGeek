using System.Text.RegularExpressions;

namespace AppGeek.Services;

/// <summary>
/// Loose version comparison. Real-world DisplayVersion values are messy
/// ("8u421", "25.001.20458", "1.2.3-beta"), so this normalises to a numeric
/// tuple and compares component by component.
/// </summary>
public static partial class VersionCompare
{
    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();

    public static int Compare(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return 0;
        if (string.IsNullOrWhiteSpace(a)) return -1;
        if (string.IsNullOrWhiteSpace(b)) return 1;

        if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return 0;

        var na = Parts(a);
        var nb = Parts(b);
        int len = Math.Max(na.Count, nb.Count);
        for (int i = 0; i < len; i++)
        {
            long va = i < na.Count ? na[i] : 0;
            long vb = i < nb.Count ? nb[i] : 0;
            if (va != vb) return va.CompareTo(vb);
        }

        // Numerically identical ("1.2" vs "1.2.0", "3.1" vs "3.1-beta"): treat as equal.
        // Falling back to a string comparison here would invent phantom updates.
        return 0;
    }

    public static bool IsNewer(string? candidate, string? current) => Compare(candidate, current) > 0;

    /// <summary>
    /// True when winget has not actually told us a version. `winget list` prints things like
    /// "Unknown" and "&gt; 3.13.5" (meaning "newer than anything I know about"), neither of which
    /// can be compared with anything.
    /// </summary>
    public static bool IsIndefinite(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return true;
        var v = version.Trim();
        return v.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith('>') || v.StartsWith('<');
    }

    /// <summary>
    /// True when two version strings plausibly describe the same installed release.
    ///
    /// Exact equality is too strict to use as corroboration for a name match. winget reports
    /// "10.0.11" for a runtime the registry calls "10.0.11.50000", and "3.14.7" for a Python
    /// the registry calls "3.14.7150.0". Both are the same product, and exact matching threw
    /// both away.
    ///
    /// Comparing only major and minor keeps the useful signal — a genuinely different release
    /// such as 1.51 against 1.52 is still rejected — without demanding that two sources agree
    /// on a build number they format differently.
    /// </summary>
    public static bool ProbablySameRelease(string? a, string? b)
    {
        if (IsIndefinite(a) || IsIndefinite(b)) return true;

        var na = Parts(a);
        var nb = Parts(b);
        if (na.Count == 0 || nb.Count == 0) return true;

        // Only one component to go on: it has to match exactly.
        if (na.Count == 1 || nb.Count == 1) return na[0] == nb[0];

        return na[0] == nb[0] && na[1] == nb[1];
    }

    /// <summary>True when the leading component differs, e.g. 24.09 -> 25.01.</summary>
    public static bool IsMajorChange(string? from, string? to)
    {
        var a = Parts(from);
        var b = Parts(to);
        if (a.Count == 0 || b.Count == 0) return false;
        return a[0] != b[0];
    }

    private static List<long> Parts(string? s)
    {
        var list = new List<long>();
        if (string.IsNullOrWhiteSpace(s)) return list;
        foreach (Match m in Numbers().Matches(s))
        {
            if (long.TryParse(m.Value, out var v)) list.Add(v);
            if (list.Count >= 6) break;
        }
        return list;
    }
}
