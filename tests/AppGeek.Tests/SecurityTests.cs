using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// Security-relevant apps are flagged so an out-of-date browser is visibly more urgent than
/// an out-of-date archiver. A false negative here is worse than a false positive.
/// </summary>
public static class SecurityTests
{
    public static void Run()
    {
        Check.Section("Security relevance");

        Check.That("a browser is security sensitive",
            SecurityRelevance.IsSecuritySensitive("Google Chrome", "Google.Chrome"));
        Check.That("a PDF reader is security sensitive",
            SecurityRelevance.IsSecuritySensitive("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit"));
        Check.That("a runtime is security sensitive",
            SecurityRelevance.IsSecuritySensitive("Java 8 Update 411", "Oracle.JavaRuntimeEnvironment"));
        Check.That("null input does not throw", !SecurityRelevance.IsSecuritySensitive(null, null));
    }
}
