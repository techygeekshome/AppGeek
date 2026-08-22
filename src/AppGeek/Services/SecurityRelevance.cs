using System.Text.RegularExpressions;

namespace AppGeek.Services;

/// <summary>
/// Flags the classes of software where an out-of-date version is a security
/// problem rather than just a missing feature: anything that renders untrusted
/// content from the internet, plus the usual runtime suspects.
///
/// Matching is done on whole words rather than substrings. A naive Contains()
/// check flags "Microsoft Solitaire Collection" because it contains "air".
/// </summary>
public static partial class SecurityRelevance
{
    [GeneratedRegex(@"[^a-z0-9+#]+")]
    private static partial Regex WordSplitter();

    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // Browsers and anything else that renders remote content
        "chrome", "chromium", "firefox", "edge", "msedge", "brave", "vivaldi", "opera",
        "librewolf", "waterfox", "safari",

        // Document readers
        "acrobat", "foxit", "sumatrapdf", "nitro", "pdfsam",

        // Runtimes
        "java", "jre", "jdk", "openjdk", "temurin", "flash", "air", "silverlight",
        "node", "nodejs", "python", "php", "perl", "ruby",

        // Communication
        "zoom", "teams", "slack", "discord", "webex", "thunderbird", "outlook", "skype",

        // Media and archive handlers (common parser-vulnerability surface)
        "vlc", "winrar", "winzip", "7zip", "7-zip", "peazip", "irfanview", "xnview",

        // Networking, transfer and remote access
        "openssl", "openssh", "putty", "filezilla", "winscp", "wireshark", "curl",
        "teamviewer", "anydesk", "vnc", "tightvnc", "ultravnc", "rustdesk", "logmein",

        // Office suites and editors that open untrusted files
        "libreoffice", "openoffice", "notepad", "notepad++", "onlyoffice",

        // Security tooling itself
        "openvpn", "wireguard", "keepass", "keepassxc", "bitwarden", "lastpass", "veracrypt"
    };

    /// <summary>Multi-word names that only make sense as a phrase.</summary>
    private static readonly string[] Phrases =
    {
        "adobe reader", "acrobat reader", "visual c++", "microsoft edge"
    };

    public static bool IsSecuritySensitive(string? name, string? packageId = null)
    {
        var haystack = ((name ?? "") + " " + (packageId ?? "")).ToLowerInvariant();
        if (haystack.Trim().Length == 0) return false;

        if (Phrases.Any(haystack.Contains)) return true;

        foreach (var token in WordSplitter().Split(haystack))
        {
            if (token.Length == 0) continue;
            if (Words.Contains(token)) return true;
        }

        return false;
    }
}
