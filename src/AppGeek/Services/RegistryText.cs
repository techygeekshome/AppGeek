namespace AppGeek.Services;

/// <summary>
/// Tidying for the strings read out of Windows' uninstall registry keys.
///
/// Kept separate from RegistryInventoryService, which depends on Microsoft.Win32.Registry and
/// therefore cannot be exercised off Windows. The parsing decision is pure, so it lives here
/// and is covered by tests. Same reasoning as InstallScopePolicy.
/// </summary>
public static class RegistryText
{
    /// <summary>
    /// Cleans a value written into the registry by an installer.
    ///
    /// Installers write these by hand and plenty get it wrong. Stray wrapping quotes are the
    /// most common fault, which is how an application ends up displaying a publisher of
    /// <c>"Anthropic</c> rather than <c>Anthropic</c>.
    ///
    /// Whitespace is trimmed, a quote pair wrapping the whole value is removed, and a single
    /// unbalanced quote at either end is dropped. A value that legitimately contains balanced
    /// quotes is left alone — <c>Bob's Software</c> and <c>The "Fast" Installer</c> both
    /// survive intact.
    /// </summary>
    public static string? Clean(string? raw)
    {
        if (raw is null) return null;

        var v = raw.Trim();
        if (v.Length == 0) return null;

        // A matched pair wrapping the entire value. Loop, because doubled wrapping happens.
        while (v.Length >= 2 &&
               ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
        {
            v = v[1..^1].Trim();
            if (v.Length == 0) return null;
        }

        // A lone unbalanced quote at one end — the case actually seen in the wild.
        if ((v[0] == '"' || v[0] == '\'') && Count(v, v[0]) == 1)
        {
            v = v[1..].Trim();
            if (v.Length == 0) return null;
        }

        if ((v[^1] == '"' || v[^1] == '\'') && Count(v, v[^1]) == 1)
        {
            v = v[..^1].Trim();
            if (v.Length == 0) return null;
        }

        return v.Length == 0 ? null : v;
    }

    private static int Count(string s, char c)
    {
        int n = 0;
        foreach (var ch in s) if (ch == c) n++;
        return n;
    }
}
