using System.Globalization;

namespace AppGeek.Services;

/// <summary>
/// Produces the monogram + colour used for app tiles, so an app without a
/// bundled icon still looks deliberate rather than blank.
/// </summary>
public static class IconFactory
{
    private static readonly string[] Palette =
    {
        "#2E78D8", "#D9534F", "#E8862B", "#4C8B3F", "#7A4FBF",
        "#17AEB7", "#D2602D", "#4A4A4A", "#8A5A2B", "#3E7AA6"
    };

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google.Chrome"] = "#D9534F",
        ["Mozilla.Firefox"] = "#E8862B",
        ["Adobe.Acrobat.Reader.64-bit"] = "#7A4FBF",
        ["7zip.7zip"] = "#4A4A4A",
        ["Notepad++.Notepad++"] = "#4C8B3F",
        ["Microsoft.PowerToys"] = "#2E78D8",
        ["VideoLAN.VLC"] = "#E8862B",
        ["Oracle.JavaRuntimeEnvironment"] = "#17AEB7"
    };

    public static string Colour(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Palette[0];
        if (Known.TryGetValue(key, out var c)) return c;

        unchecked
        {
            int hash = 17;
            foreach (var ch in key) hash = hash * 31 + char.ToLowerInvariant(ch);
            return Palette[Math.Abs(hash) % Palette.Length];
        }
    }

    /// <summary>Two-letter monogram, e.g. "Google Chrome" -> "GC", "7-Zip" -> "7Z".</summary>
    public static string Monogram(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "??";

        var words = name.Split(new[] { ' ', '-', '_', '.', '+' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length > 0 && char.IsLetterOrDigit(w[0]))
                        .ToList();

        if (words.Count == 0) return name.Trim()[..Math.Min(2, name.Trim().Length)].ToUpperInvariant();
        if (words.Count == 1)
        {
            var w = words[0];
            return (w.Length >= 2 ? w[..2] : w).ToUpperInvariant();
        }
        return (char.ToUpperInvariant(words[0][0]).ToString(CultureInfo.InvariantCulture)
              + char.ToUpperInvariant(words[1][0])).ToString();
    }
}
