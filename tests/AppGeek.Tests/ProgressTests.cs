using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// The scan progress bar exists because a silent four-minute winget query looks like a
/// hung application. It must never go backwards and never claim to be finished early.
/// </summary>
public static class ProgressTests
{
    public static void Run()
    {
        Check.Section("Scan progress");

        var seen = new List<int>();
        var sink = new Sink(p => seen.Add(p.Percent));

        var phase = new ScanPhase(sink, start: 20, end: 60, message: "Reading winget", step: 2, totalSteps: 4);
        phase.Begin();
        phase.Report(0.5);
        phase.Complete();

        Check.Equal("the phase starts at its lower bound", 20, seen.First());
        Check.Equal("the phase ends at its upper bound", 60, seen.Last());
        Check.That("progress never goes backwards",
            seen.Zip(seen.Skip(1), (a, b) => b >= a).All(x => x));

        Check.Section("Scan progress — work of unknown length");

        seen.Clear();
        var uncounted = new ScanPhase(sink, 0, 100, "Listing packages", 1, 1);
        for (int i = 1; i <= 500; i++) uncounted.ReportUncounted(i);

        Check.That("an unbounded phase still moves", seen.Last() > 0);
        Check.That("an unbounded phase never reaches 100% on its own", seen.All(p => p < 100));
        Check.That("an unbounded phase never goes backwards",
            seen.Zip(seen.Skip(1), (a, b) => b >= a).All(x => x));

        uncounted.Complete();
        Check.Equal("only Complete() reaches 100%", 100, seen.Last());

        Check.Equal("the step label reads naturally",
            "Step 2 of 4", new ScanProgress(50, "x", 2, 4).StepLabel);
    }

    private sealed class Sink : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> _on;
        public Sink(Action<ScanProgress> on) => _on = on;
        public void Report(ScanProgress value) => _on(value);
    }
}
