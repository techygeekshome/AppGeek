using System.Text;
using System.Text.RegularExpressions;

namespace AppGeek.Services;

/// <summary>
/// Helpers for taming winget's console output. winget draws animated progress using
/// carriage returns, spinner characters and block-drawing glyphs, all of which arrive
/// inside a single "line" from the process redirect.
/// </summary>
public static partial class WingetText
{
    [GeneratedRegex(@"[▀-▟■-◿─-╿]")]
    private static partial Regex BlockGlyphs();

    [GeneratedRegex(@"\u001b\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscapes();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ColumnGap();

    [GeneratedRegex(@"(\d{1,3})\s*%")]
    private static partial Regex PercentPattern();

    private static readonly char[] SpinnerChars = { '-', '\\', '|', '/' };

    /// <summary>Splits a redirected line on carriage returns and drops pure-noise fragments.</summary>
    public static IEnumerable<string> SplitProgressLine(string raw)
    {
        foreach (var part in raw.Split('\r', StringSplitOptions.RemoveEmptyEntries))
        {
            // Leading whitespace matters when slicing table columns, but not here,
            // so trim both ends before deciding whether this fragment is just noise.
            var clean = Clean(part).Trim();
            if (clean.Length == 0) continue;
            if (clean.Length <= 2 && clean.All(c => SpinnerChars.Contains(c))) continue;
            yield return clean;
        }
    }

    /// <summary>Removes ANSI escapes, control characters and progress-bar block glyphs.</summary>
    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = AnsiEscapes().Replace(s, "");

        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsControl(c) && c != '\t') continue;
            sb.Append(c);
        }
        return BlockGlyphs().Replace(sb.ToString(), "").TrimEnd();
    }

    /// <summary>Pulls a percentage out of a progress fragment, or null.</summary>
    public static int? ParsePercent(string s)
    {
        var m = PercentPattern().Match(s);
        if (!m.Success) return null;
        return int.TryParse(m.Groups[1].Value, out var v) && v is >= 0 and <= 100 ? v : null;
    }

    /// <summary>
    /// Parses winget's fixed-width table output.
    /// Column boundaries are taken from the header row rather than assumed, and columns
    /// are matched by name where possible and by position otherwise, so this keeps
    /// working on non-English installs where the headers are translated.
    /// </summary>
    public static List<WingetRow> ParseTable(string output)
    {
        var rows = new List<WingetRow>();
        if (string.IsNullOrWhiteSpace(output)) return rows;

        var lines = output
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => Clean(l.Contains('\r') ? l.Split('\r').Last() : l))
            .ToList();

        // winget emits more than one table. "winget upgrade" prints the ordinary upgrades,
        // then a second table under "The following packages have an upgrade available, but
        // require explicit targeting for upgrade:". Reading only the first one silently
        // hides those packages - which is exactly what happened to 64Gram Desktop: winget
        // said "3 upgrades available", AppGeek offered two.
        //
        // Those packages are safe for AppGeek to offer, because explicit targeting is all
        // it ever does: every call goes out as --id "<id>" --exact.
        int cursor = 0;
        while (cursor < lines.Count)
        {
            var sepIndex = IndexOfSeparator(lines, cursor);
            if (sepIndex <= 0) break;

            // The header is the nearest non-empty line above the separator.
            int headerIndex = -1;
            for (int i = sepIndex - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(lines[i])) { headerIndex = i; break; }
            }

            if (headerIndex < 0) { cursor = sepIndex + 1; continue; }

            var columns = ReadColumns(lines[headerIndex]);
            if (columns.Count < 2) { cursor = sepIndex + 1; continue; }

            int i2 = sepIndex + 1;
            for (; i2 < lines.Count; i2++)
            {
                var line = lines[i2];
                if (string.IsNullOrWhiteSpace(line)) break;          // table ended
                if (IsSeparator(line)) break;                        // the next table started

                var row = new WingetRow { Columns = columns };
                for (int c = 0; c < columns.Count; c++)
                {
                    int start = columns[c].Start;
                    int end = c + 1 < columns.Count ? columns[c + 1].Start : line.Length;
                    // The "> " winget puts in front of some versions is deliberately left
                    // alone. It means winget could not pin the installed version down, and
                    // PackageMatcher relies on spotting it: an indefinite version is treated
                    // as "nothing to compare", so the match is kept rather than thrown away.
                    // Stripping it here turned it into a definite version that then
                    // disagreed with the registry, and silently lost the match.
                    row.Values[c] = Slice(line, start, end);
                }

                if (!IsDataRow(row)) continue;
                rows.Add(row);
            }

            cursor = i2 + 1;
        }

        return rows;
    }

    private static bool IsSeparator(string line)
    {
        var t = line.Trim();
        return t.Length >= 10 && t.All(c => c == '-');
    }

    private static int IndexOfSeparator(List<string> lines, int from)
    {
        for (int i = from; i < lines.Count; i++)
            if (IsSeparator(lines[i])) return i;
        return -1;
    }

    /// <summary>
    /// A real row names something and says at least one other thing about it. This rejects
    /// the summary line winget prints straight after the rows - "3 upgrades available." -
    /// which otherwise parses as an application called "3 upgrades available.".
    /// </summary>
    private static bool IsDataRow(WingetRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Get(WingetColumn.Name))) return false;

        for (int i = 0; i < row.Columns.Count; i++)
        {
            if (row.Columns[i].Role == WingetColumn.Name) continue;
            if (row.Values.TryGetValue(i, out var v) && !string.IsNullOrWhiteSpace(v)) return true;
        }

        return false;
    }


    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length) return "";
        end = Math.Min(end, line.Length);
        if (end <= start) return "";
        return line[start..end].Trim();
    }

    private static readonly string[] KnownHeadings =
        { "Name", "Id", "Version", "Available", "Source", "Match" };

    /// <summary>
    /// Recovers a column that the two-space rule merged into its neighbour.
    ///
    /// winget does not always leave two spaces between headings. On winget 1.29 the list
    /// header ends "...Available Source" with a SINGLE space, so the splitter produced one
    /// column called "Available Source". That title matches no known heading, so it fell
    /// through to the positional fallback, was labelled Available, and Source ceased to
    /// exist - taking the source of every installed app with it, and leaving the available
    /// version reading "1.2.7     winget", which no version comparison can make sense of.
    ///
    /// Only an exact trailing English heading is split off, so a translated two-word heading
    /// is left alone.
    /// </summary>
    private static List<ColumnSpec> SplitSingleSpacedHeadings(List<ColumnSpec> specs)
    {
        var result = new List<ColumnSpec>();

        foreach (var spec in specs)
        {
            var title = spec.Title;
            var start = spec.Start;
            var pending = new List<ColumnSpec>();

            // Work from the right: "Version Available Source" splits twice.
            while (true)
            {
                var space = title.LastIndexOf(' ');
                if (space <= 0) break;

                var tail = title[(space + 1)..];
                if (!KnownHeadings.Contains(tail, StringComparer.OrdinalIgnoreCase)) break;

                pending.Insert(0, new ColumnSpec(tail, start + space + 1));
                title = title[..space].TrimEnd();
            }

            result.Add(new ColumnSpec(title, start));
            result.AddRange(pending);
        }

        return result;
    }

    private static List<ColumnSpec> ReadColumns(string header)
    {
        var specs = new List<ColumnSpec>();
        int pos = 0;
        while (pos < header.Length)
        {
            while (pos < header.Length && header[pos] == ' ') pos++;
            if (pos >= header.Length) break;

            int start = pos;
            // A column title runs until two or more consecutive spaces.
            var gap = ColumnGap().Match(header, pos);
            int end = gap.Success ? gap.Index : header.Length;
            specs.Add(new ColumnSpec(header[start..end].Trim(), start));
            pos = gap.Success ? gap.Index + gap.Length : header.Length;
        }

        specs = SplitSingleSpacedHeadings(specs);

        // Assign semantic roles: by known English title first, else by position.
        for (int i = 0; i < specs.Count; i++)
        {
            var role = specs[i].Title.ToLowerInvariant() switch
            {
                "name" => WingetColumn.Name,
                "id" => WingetColumn.Id,
                "version" => WingetColumn.Version,
                "available" => WingetColumn.Available,
                "source" => WingetColumn.Source,
                "match" => WingetColumn.Match,
                _ => WingetColumn.Unknown
            };
            specs[i] = specs[i] with { Role = role };
        }

        // Any column whose title we did not recognise is almost certainly a translated
        // header, so give it the next unused role from winget's documented column order.
        var canonical = new[]
        {
            WingetColumn.Name, WingetColumn.Id, WingetColumn.Version,
            WingetColumn.Available, WingetColumn.Source
        };
        var taken = specs.Select(sp => sp.Role).ToHashSet();

        for (int i = 0; i < specs.Count; i++)
        {
            if (specs[i].Role != WingetColumn.Unknown) continue;

            var next = canonical.FirstOrDefault(r => !taken.Contains(r), WingetColumn.Unknown);
            if (next == WingetColumn.Unknown) continue;

            specs[i] = specs[i] with { Role = next };
            taken.Add(next);
        }

        return specs;
    }
}

public enum WingetColumn { Unknown, Name, Id, Version, Available, Source, Match }

public readonly record struct ColumnSpec(string Title, int Start, WingetColumn Role = WingetColumn.Unknown);

public sealed class WingetRow
{
    public Dictionary<int, string> Values { get; } = new();
    public List<ColumnSpec> Columns { get; set; } = new();

    public string Get(WingetColumn column)
    {
        for (int i = 0; i < Columns.Count; i++)
            if (Columns[i].Role == column)
                return Values.TryGetValue(i, out var v) ? v : "";
        return "";
    }

    public override string ToString() =>
        $"{Get(WingetColumn.Name)} | {Get(WingetColumn.Id)} | {Get(WingetColumn.Version)} -> {Get(WingetColumn.Available)}";
}
