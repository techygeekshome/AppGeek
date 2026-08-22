using AppGeek.Models;
using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// Regression tests for install-scope pinning.
///
/// AppGeek runs elevated. Left to itself, winget then chooses the machine-wide installer,
/// which turns "upgrade Chrome" into "install a second copy of Chrome somewhere else and
/// orphan the one the user actually had" — leaving dead shortcuts and an app that will not
/// start. These are the tests that must never be allowed to go red.
/// </summary>
public static class ScopeTests
{
    public static void Run()
    {
        Check.Section("Install scope — an upgrade must stay an upgrade");

        // The core of the fix.
        Check.Equal("a per-user app pins winget to the user scope",
            "user", InstallScopePolicy.ScopeFlag(InstallScope.User, "winget"));

        Check.Equal("a machine-wide app pins winget to the machine scope",
            "machine", InstallScopePolicy.ScopeFlag(InstallScope.Machine, "winget"));

        // Null is not one thing. Each of these is a different reason, and all three must
        // produce no --scope flag rather than a wrong one.
        Check.Equal("an unknown scope sends no flag rather than guessing",
            null, InstallScopePolicy.ScopeFlag(null, "winget"));

        Check.Equal("a Store app sends no flag — Store installs have no scope",
            null, InstallScopePolicy.ScopeFlag(InstallScope.Store, "winget"));

        Check.Equal("msstore as a source sends no flag — the source rejects it",
            null, InstallScopePolicy.ScopeFlag(InstallScope.User, "msstore"));

        Check.Equal("msstore is matched case-insensitively",
            null, InstallScopePolicy.ScopeFlag(InstallScope.Machine, "MSStore"));

        Check.Equal("a missing source still pins the scope",
            "user", InstallScopePolicy.ScopeFlag(InstallScope.User, null));

        Check.Section("Install scope — refusals are explained, not swallowed");

        const int NoInstaller = unchecked((int)0x8A150011);
        const int AccessDenied = unchecked((int)0x80070005);

        Check.That("a per-user app with no per-user installer is a scope refusal",
            InstallScopePolicy.IsScopeRefusal(NoInstaller, InstallScope.User));

        Check.That("a machine app with no machine installer is a scope refusal",
            InstallScopePolicy.IsScopeRefusal(NoInstaller, InstallScope.Machine));

        Check.That("the same exit code is NOT a scope refusal when the scope was unknown",
            !InstallScopePolicy.IsScopeRefusal(NoInstaller, null));

        Check.That("an unrelated failure is never dressed up as a scope refusal",
            !InstallScopePolicy.IsScopeRefusal(AccessDenied, InstallScope.User));

        var userText = InstallScopePolicy.DescribeScopeRefusal(NoInstaller, InstallScope.User);
        Check.That("the per-user refusal tells the user their shortcuts were the reason",
            userText is not null && userText.Contains("shortcuts"));

        Check.That("the refusal reads as a deliberate choice, not a crash",
            userText is not null && userText.Contains("on purpose"));

        Check.That("a non-refusal gets no scope explanation at all",
            InstallScopePolicy.DescribeScopeRefusal(AccessDenied, InstallScope.User) is null);

        Check.Section("Install scope — wording used in the run log");

        Check.Equal("per-user reads as per-user", "per-user", InstallScopePolicy.Describe(InstallScope.User));
        Check.Equal("machine reads as machine-wide", "machine-wide", InstallScopePolicy.Describe(InstallScope.Machine));
        Check.Equal("unknown is stated, not hidden", "unknown-scope", InstallScopePolicy.Describe(null));
    }
}
