using AppGeek.Models;

namespace AppGeek.Services;

public enum RunningAppAction
{
    /// <summary>Nothing in the way — start the install.</summary>
    Proceed,

    /// <summary>The app is open and the user asked for those to be left alone.</summary>
    Skip,

    /// <summary>Ask the app to close itself first, then install if it went quietly.</summary>
    CloseFirst
}

/// <summary>
/// The "When an app is running" setting, as a decision rather than a side effect, so it can
/// be tested without a running Windows desktop.
///
/// Note that <see cref="RunningAppPolicy.Ask"/> resolves to Proceed here. By the time a run
/// has started the question has already been put to the user on the Updates screen, and
/// anything still in the queue is something they said yes to.
/// </summary>
public static class RunningAppGate
{
    public static RunningAppAction Decide(RunningAppPolicy policy, bool isRunning)
    {
        if (!isRunning) return RunningAppAction.Proceed;

        return policy switch
        {
            RunningAppPolicy.AlwaysSkip => RunningAppAction.Skip,
            RunningAppPolicy.AlwaysClose => RunningAppAction.CloseFirst,
            _ => RunningAppAction.Proceed
        };
    }
}
