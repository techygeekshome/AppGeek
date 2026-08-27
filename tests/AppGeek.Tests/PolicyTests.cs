using AppGeek.Models;
using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// The two install-behaviour settings that used to be stored and then quietly ignored:
/// what to do when the app being updated is open, and what to do about a restart.
///
/// Both are decisions with a safety edge — one can close somebody's work, the other can
/// restart their PC — so both were written as pure functions specifically to be pinned down
/// here rather than tried out on a live machine.
/// </summary>
public static class PolicyTests
{
    public static void Run()
    {
        Check.Section("When an app is running");

        Check.Equal("an app that is not running is never held up",
            RunningAppAction.Proceed, RunningAppGate.Decide(RunningAppPolicy.AlwaysSkip, isRunning: false));
        Check.Equal("'always close' does nothing when the app is closed already",
            RunningAppAction.Proceed, RunningAppGate.Decide(RunningAppPolicy.AlwaysClose, isRunning: false));

        Check.Equal("'always skip' skips a running app",
            RunningAppAction.Skip, RunningAppGate.Decide(RunningAppPolicy.AlwaysSkip, isRunning: true));
        Check.Equal("'always close' asks it to close first",
            RunningAppAction.CloseFirst, RunningAppGate.Decide(RunningAppPolicy.AlwaysClose, isRunning: true));

        // 'Ask' is answered on the Updates screen before the run starts. Asking a second time
        // mid-run, per package, would be worse than not asking at all.
        Check.Equal("'ask' has already been answered by the time a run starts",
            RunningAppAction.Proceed, RunningAppGate.Decide(RunningAppPolicy.Ask, isRunning: true));

        Check.Section("Restart handling");

        Check.Equal("nothing pending means no restart",
            RebootAction.None,
            RebootDecision.Decide(RebootPolicy.Automatic, pendingReboot: false,
                                  installerRequestedReboot: false, runWasClean: true).Action);

        Check.Equal("'never reboot' means never, even when Windows is asking",
            RebootAction.None,
            RebootDecision.Decide(RebootPolicy.Never, pendingReboot: true,
                                  installerRequestedReboot: true, runWasClean: true).Action);

        Check.Equal("'prompt at the end' prompts when Windows has a restart pending",
            RebootAction.Prompt,
            RebootDecision.Decide(RebootPolicy.PromptAtEnd, pendingReboot: true,
                                  installerRequestedReboot: false, runWasClean: true).Action);

        Check.Equal("'prompt at the end' prompts when an installer asked",
            RebootAction.Prompt,
            RebootDecision.Decide(RebootPolicy.PromptAtEnd, pendingReboot: false,
                                  installerRequestedReboot: true, runWasClean: true).Action);

        Check.Equal("'reboot automatically' does so after a clean run",
            RebootAction.Automatic,
            RebootDecision.Decide(RebootPolicy.Automatic, pendingReboot: true,
                                  installerRequestedReboot: false, runWasClean: true).Action);

        // The important one. A run that failed or was stopped has left something half-done
        // and the user has not read the log yet; restarting on top of that is the last thing
        // AppGeek should do on its own.
        Check.Equal("a failed run downgrades an automatic restart to a prompt",
            RebootAction.Prompt,
            RebootDecision.Decide(RebootPolicy.Automatic, pendingReboot: true,
                                  installerRequestedReboot: true, runWasClean: false).Action);

        var auto = RebootDecision.Decide(RebootPolicy.Automatic, true, false, true);
        Check.That("an automatic restart is delayed, so it can be cancelled",
            auto.Delay >= TimeSpan.FromMinutes(1));
        Check.That("the reason is spelled out rather than left blank",
            !string.IsNullOrWhiteSpace(auto.Reason));

        Check.Section("Restart exit codes");

        Check.That("3010 is a success that wants a restart", RebootDecision.IsRebootExitCode(3010));
        Check.That("1641 is a success that has begun a restart", RebootDecision.IsRebootExitCode(1641));
        Check.That("winget's own reboot code is recognised",
            RebootDecision.IsRebootExitCode(unchecked((int)0x8A150109)));

        Check.That("0 is not a reboot code", !RebootDecision.IsRebootExitCode(0));
        Check.That("a genuine failure is not mistaken for a reboot request",
            !RebootDecision.IsRebootExitCode(unchecked((int)0x8A150011)));
        Check.That("access denied is not a reboot request",
            !RebootDecision.IsRebootExitCode(unchecked((int)0x80070005)));
    }
}
