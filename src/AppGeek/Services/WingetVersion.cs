using System.Text.RegularExpressions;

namespace AppGeek.Services;

/// <summary>
/// Reads a <see cref="Version"/> out of the free-form version strings AppGeek has to deal
/// with: whatever <c>winget --version</c> prints, and whatever tag name GitHub returns for
/// the latest release.
///
/// This lives on its own, with no dependency on winget, WPF or the registry, purely so the
/// test project can link it in — the same reason <c>InstallScopePolicy</c> exists. The
/// decision it makes is small but it gates two things that matter: whether AppGeek tells
/// somebody their Package Manager is too old, and whether it tells them an update exists.
/// </summary>
public static partial class WingetVersion
{
    /// <summary>
    /// 1.4 is the floor: that is the release that added <c>--disable-interactivity</c>, which
    /// AppGeek passes on every call to stop winget prompting inside a redirected console.
    /// It lives here rather than on the bootstrapper so the floor and the parser that feeds
    /// it can be tested together — a version that parses correctly but compares wrongly
    /// against the floor is the same bug from the user's point of view.
    /// </summary>
    public static readonly Version Minimum = new(1, 4);

    /// <summary>
    /// Deliberately loose. It looks for the first <c>N.N</c> or <c>N.N.N</c> anywhere in the
    /// string rather than anchoring, because the inputs are not under our control:
    /// <c>winget --version</c> prints <c>v1.6.3482</c>, GitHub tags are <c>v1.0.1</c>, and
    /// some winget builds print a preview suffix after the number.
    /// </summary>
    [GeneratedRegex(@"(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex Pattern();

    public static Version? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var m = Pattern().Match(raw);
        if (!m.Success) return null;

        try
        {
            var major = int.Parse(m.Groups[1].Value);
            var minor = int.Parse(m.Groups[2].Value);
            var build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
            return new Version(major, minor, build);
        }
        catch
        {
            // int.Parse overflows on absurdly long digit runs, and Version rejects negatives.
            // Either way the answer is the same: we do not know what version this is, and
            // guessing is worse than saying so. Callers treat null as "could not read it"
            // and carry on rather than blocking the user.
            return null;
        }
    }
}
