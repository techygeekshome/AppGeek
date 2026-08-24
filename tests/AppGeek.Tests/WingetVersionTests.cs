using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// This was the one uncovered decision in the harness, and it is not a cosmetic one.
/// It gates two user-visible claims: "your Package Manager is too old" (which sends people
/// off to the Microsoft Store) and "an update is available" (which sends them to GitHub).
/// Both are wrong if this misreads a string, and neither fails loudly when it does.
/// </summary>
public static class WingetVersionTests
{
    public static void Run()
    {
        Check.Section("Reading a version out of what winget actually prints");

        // winget --version prints a leading v. This is the single most common real input.
        Check.Equal("winget's \"v1.6.3482\"", new Version(1, 6, 3482), WingetVersion.Parse("v1.6.3482"));
        Check.Equal("a bare two-part version gains a zero build",
            new Version(1, 4, 0), WingetVersion.Parse("1.4"));
        Check.Equal("surrounding whitespace is ignored",
            new Version(1, 6, 3482), WingetVersion.Parse("  v1.6.3482\r\n"));

        // Some winget builds append a channel suffix. The number in front is still the answer.
        Check.Equal("a preview suffix does not defeat it",
            new Version(1, 7, 10582), WingetVersion.Parse("v1.7.10582-preview"));

        Check.Section("Reading a version out of a GitHub release tag");

        Check.Equal("AppGeek's own tag format", new Version(1, 0, 1), WingetVersion.Parse("v1.0.1"));
        Check.Equal("a tag with no v", new Version(1, 0, 1), WingetVersion.Parse("1.0.1"));

        Check.Section("The floor check this feeds");

        // MinimumVersion is 1.4 because that is the release that added --disable-interactivity.
        // These two cases are the ones either side of the line, and getting them the wrong way
        // round means either a false "too old" warning or a silent hang in a redirected console.
        Check.That("1.3.2691 is below the 1.4 floor",
            WingetVersion.Parse("v1.3.2691") < WingetVersion.Minimum);
        Check.That("1.4.0 is not below the floor — it IS the floor",
            !(WingetVersion.Parse("v1.4.0") < WingetVersion.Minimum));
        Check.That("1.10 is not below 1.4 — this is not string ordering",
            !(WingetVersion.Parse("v1.10.0") < WingetVersion.Minimum));

        Check.Section("Nothing readable — must be null, never a guess");

        Check.Equal("null in, null out", null, WingetVersion.Parse(null));
        Check.Equal("empty string", null, WingetVersion.Parse(""));
        Check.Equal("whitespace only", null, WingetVersion.Parse("   "));
        Check.Equal("no digits at all", null, WingetVersion.Parse("not a version"));
        Check.Equal("a single number is not a version", null, WingetVersion.Parse("14"));
        Check.Equal("winget's indefinite form has no version to read",
            null, WingetVersion.Parse("Unknown"));

        Check.Section("Inputs that could throw, and must not");

        // int.Parse overflows here. If this ever escapes, it throws on the UI thread during
        // startup, because Check() runs before the main window is usable.
        Check.Equal("a digit run too long for int returns null rather than throwing",
            null, WingetVersion.Parse("99999999999.1"));
        Check.Equal("overflow in the build component too",
            null, WingetVersion.Parse("1.2.99999999999"));

        Check.Section("Documented quirks — pinned so a rewrite has to be deliberate");

        // The pattern is deliberately unanchored, and these are the consequences. None of
        // them is wrong for the two real callers, but all three would surprise someone
        // reusing this, so they are pinned rather than left to be rediscovered.
        Check.Equal("a fourth component is ignored, not rounded",
            new Version(1, 2, 3), WingetVersion.Parse("1.2.3.4"));
        Check.Equal("the FIRST version-like run wins, not the longest",
            new Version(1, 6, 3482),
            WingetVersion.Parse("v1.6.3482\nWindows: Windows.Desktop v10.0.22631.3155"));
        Check.Equal("digits glued to a word are still read",
            new Version(123, 4, 0), WingetVersion.Parse("abc123.4"));
    }
}
