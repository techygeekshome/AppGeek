using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// Installers write the uninstall registry keys by hand and a fair number get it wrong.
/// A stray quote is cosmetic but it is visible on every row of the installed list.
/// </summary>
public static class RegistryTextTests
{
    public static void Run()
    {
        Check.Section("Registry values — tidy what installers got wrong");

        // The case seen in the wild: a lone leading quote on a publisher name.
        Check.Equal("a lone leading quote is dropped", "Anthropic", RegistryText.Clean("\"Anthropic"));
        Check.Equal("a lone trailing quote is dropped", "Anthropic", RegistryText.Clean("Anthropic\""));
        Check.Equal("a wrapping pair is unwrapped", "Anthropic", RegistryText.Clean("\"Anthropic\""));
        Check.Equal("doubled wrapping is unwrapped", "Anthropic", RegistryText.Clean("\"\"Anthropic\"\""));
        Check.Equal("single quotes are handled too", "Anthropic", RegistryText.Clean("'Anthropic'"));
        Check.Equal("whitespace is trimmed", "Google LLC", RegistryText.Clean("  Google LLC  "));

        Check.Section("Registry values — leave legitimate text alone");

        Check.Equal("an apostrophe inside a name survives", "Bob's Software", RegistryText.Clean("Bob's Software"));
        Check.Equal("balanced inner quotes survive", "The \"Fast\" Installer", RegistryText.Clean("The \"Fast\" Installer"));
        Check.Equal("an ordinary name is untouched", "OBS Studio", RegistryText.Clean("OBS Studio"));
        Check.Equal("a path is untouched", "C:\\Program Files\\App", RegistryText.Clean("C:\\Program Files\\App"));

        Check.Section("Registry values — nothing is nothing");

        Check.Equal("null stays null", null, RegistryText.Clean(null));
        Check.Equal("empty becomes null", null, RegistryText.Clean(""));
        Check.Equal("whitespace becomes null", null, RegistryText.Clean("   "));
        Check.Equal("a lone quote becomes null", null, RegistryText.Clean("\""));
        Check.Equal("an empty quoted string becomes null", null, RegistryText.Clean("\"\""));
    }
}
