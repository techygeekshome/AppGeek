namespace AppGeek.Services;

/// <summary>
/// A progress report from a scan: an overall percentage, the phase being worked on,
/// and a human-readable message.
///
/// The percentages are driven by real signals — how many registry hives have been
/// read, how many lines winget has actually printed — rather than a timer pretending
/// to be progress. Where a phase has no natural denominator (a single winget call),
/// the percentage approaches the end of that phase asymptotically as output arrives,
/// so the bar always moves while work is happening and never overshoots.
/// </summary>
public sealed record ScanProgress(int Percent, string Message, int Step, int TotalSteps)
{
    public string StepLabel => $"Step {Step} of {TotalSteps}";
}

/// <summary>Maps work inside one phase onto that phase's slice of the overall bar.</summary>
public sealed class ScanPhase
{
    public ScanPhase(IProgress<ScanProgress>? sink, int start, int end, string message, int step, int totalSteps)
    {
        _sink = sink;
        _start = start;
        _end = end;
        _message = message;
        _step = step;
        _totalSteps = totalSteps;
    }

    private readonly IProgress<ScanProgress>? _sink;
    private readonly int _start;
    private readonly int _end;
    private readonly string _message;
    private readonly int _step;
    private readonly int _totalSteps;

    /// <summary>Reports the start of the phase.</summary>
    public void Begin() => Report(0);

    /// <summary>Reports a known fraction (0..1) through this phase.</summary>
    public void Report(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var percent = (int)Math.Round(_start + (_end - _start) * fraction);
        _sink?.Report(new ScanProgress(percent, _message, _step, _totalSteps));
    }

    /// <summary>
    /// Reports progress for work with no known total, using the count of real events
    /// seen so far. Rises quickly at first and then flattens, approaching but never
    /// reaching the end of the phase.
    /// </summary>
    public void ReportUncounted(int eventsSoFar, double halfLife = 40)
    {
        var fraction = 1 - Math.Exp(-eventsSoFar / Math.Max(1, halfLife));
        Report(fraction * 0.92);
    }

    /// <summary>Reports the phase as finished.</summary>
    public void Complete() => Report(1);

    public string Message => _message;
}
