using AppGeek.Models;

namespace AppGeek.Services;

public enum RebootAction { None, Prompt, Automatic }

public sealed record RebootPlan(RebootAction Action, TimeSpan Delay, string Reason)
{
    public static readonly RebootPlan None = new(RebootAction.None, TimeSpan.Zero, "");
}

/// <summary>
/// Decides what to do about a restart once a run has finished. Pure, so every rule below is
/// covered by the test harness rather than by trying it on somebody's PC.
/// </summary>
public static class RebootDecision
{
    /// <summary>
    /// Long enough to read the message, close a document and cancel; short enough that an
    /// unattended machine still gets its restart. <c>shutdown /a</c> aborts it.
    /// </summary>
    public static readonly TimeSpan AutomaticDelay = TimeSpan.FromMinutes(2);

    /// <param name="policy">What the user chose in Settings.</param>
    /// <param name="pendingReboot">Windows itself is reporting a restart is pending.</param>
    /// <param name="installerRequestedReboot">An installer in this run returned a "reboot required" code.</param>
    /// <param name="runWasClean">Every item succeeded and the run was not stopped.</param>
    public static RebootPlan Decide(RebootPolicy policy, bool pendingReboot,
                                    bool installerRequestedReboot, bool runWasClean)
    {
        if (!pendingReboot && !installerRequestedReboot) return RebootPlan.None;

        var reason = installerRequestedReboot
            ? "an installer in this run asked for a restart"
            : "Windows is reporting a restart as pending";

        switch (policy)
        {
            case RebootPolicy.Never:
                return RebootPlan.None;

            case RebootPolicy.Automatic when runWasClean:
                return new RebootPlan(RebootAction.Automatic, AutomaticDelay, reason);

            // A run that failed or was stopped is the worst possible moment to restart a
            // machine on its own: something is half-done and the user has not seen why yet.
            // Fall back to asking rather than doing.
            case RebootPolicy.Automatic:
                return new RebootPlan(RebootAction.Prompt, TimeSpan.Zero,
                    reason + ", but the run did not finish cleanly so AppGeek will not restart on its own");

            default:
                return new RebootPlan(RebootAction.Prompt, TimeSpan.Zero, reason);
        }
    }

    /// <summary>
    /// Exit codes that mean "this worked, but Windows needs a restart to finish it".
    /// 3010 and 1641 are the two standard Windows Installer values; 0x8A150109 is winget's
    /// own equivalent. All three are successes, and none of them should be shown to the user
    /// as a failed update.
    /// </summary>
    public static bool IsRebootExitCode(int code) => unchecked((uint)code) switch
    {
        3010 => true,       // ERROR_SUCCESS_REBOOT_REQUIRED
        1641 => true,       // ERROR_SUCCESS_REBOOT_INITIATED
        0x8A150109 => true, // APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_REQUIRED_TO_FINISH
        _ => false
    };
}
