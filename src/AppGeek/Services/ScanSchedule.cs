using System.Globalization;
using System.Text.RegularExpressions;

namespace AppGeek.Services;

public enum ScanScheduleKind
{
    /// <summary>No background scan. The user scans when they choose to.</summary>
    Manual,

    /// <summary>Scan when AppGeek is launched. Handled in-process; no scheduled task.</summary>
    AtStartup,

    Daily,
    Weekly
}

/// <summary>What a schedule option from Settings actually means, once parsed.</summary>
public sealed record ScanSchedulePlan(ScanScheduleKind Kind, TimeSpan TimeOfDay, DayOfWeek Day = DayOfWeek.Sunday)
{
    public static readonly ScanSchedulePlan Manual = new(ScanScheduleKind.Manual, TimeSpan.Zero);

    /// <summary>Only Daily and Weekly need Windows to wake AppGeek up. The rest we handle ourselves.</summary>
    public bool NeedsScheduledTask => Kind is ScanScheduleKind.Daily or ScanScheduleKind.Weekly;

    public string Describe() => Kind switch
    {
        ScanScheduleKind.Daily => $"Every day at {Format(TimeOfDay)}",
        ScanScheduleKind.Weekly => $"Every {Day} at {Format(TimeOfDay)}",
        ScanScheduleKind.AtStartup => "Each time AppGeek starts",
        _ => "Only when you ask"
    };

    private static string Format(TimeSpan t) =>
        t.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// Turns the Settings dropdown text into a schedule, and a schedule into a schtasks.exe
/// command line.
///
/// Deliberately kept free of Windows types so it can be tested on any machine — the part
/// that shells out lives in <see cref="ScheduledScanService"/>.
/// </summary>
public static class ScanSchedule
{
    /// <summary>
    /// Flat, no folder. schtasks can create tasks inside a folder, but only if the folder
    /// already exists, and it does not create one for you. A top-level name avoids an
    /// entire class of "the task silently was not registered" support mail.
    /// </summary>
    public const string TaskName = "AppGeek Scan";

    /// <summary>The switch the scheduled task passes back to AppGeek. Scan only — never installs.</summary>
    public const string ScanArgument = "--scan";

    private static readonly TimeSpan DefaultTime = new(3, 0, 0);

    private static readonly Regex TimeOfDay =
        new(@"(?<h>[01]?\d|2[0-3])\s*[:.]\s*(?<m>[0-5]\d)", RegexOptions.Compiled);

    /// <summary>
    /// Reads one of the Settings options — "Daily at 03:00", "Weekly on Sunday",
    /// "Every time AppGeek starts", "Manually only" — and anything close enough to them.
    /// Unrecognised text is Manual: never invent a schedule the user did not ask for.
    /// </summary>
    public static ScanSchedulePlan Parse(string? option)
    {
        if (string.IsNullOrWhiteSpace(option)) return ScanSchedulePlan.Manual;

        var text = option.Trim();

        if (text.Contains("manual", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("never", StringComparison.OrdinalIgnoreCase))
            return ScanSchedulePlan.Manual;

        if (text.Contains("start", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("launch", StringComparison.OrdinalIgnoreCase))
            return new ScanSchedulePlan(ScanScheduleKind.AtStartup, TimeSpan.Zero);

        var time = ParseTime(text) ?? DefaultTime;

        if (text.Contains("week", StringComparison.OrdinalIgnoreCase))
            return new ScanSchedulePlan(ScanScheduleKind.Weekly, time, ParseDay(text) ?? DayOfWeek.Sunday);

        if (text.Contains("dai", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("every day", StringComparison.OrdinalIgnoreCase))
            return new ScanSchedulePlan(ScanScheduleKind.Daily, time);

        return ScanSchedulePlan.Manual;
    }

    private static TimeSpan? ParseTime(string text)
    {
        var m = TimeOfDay.Match(text);
        if (!m.Success) return null;
        return new TimeSpan(int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture),
                            int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture), 0);
    }

    private static DayOfWeek? ParseDay(string text)
    {
        foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>())
            if (text.Contains(d.ToString(), StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    /// <summary>schtasks wants Mondays as MON.</summary>
    public static string ShortDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MON",
        DayOfWeek.Tuesday => "TUE",
        DayOfWeek.Wednesday => "WED",
        DayOfWeek.Thursday => "THU",
        DayOfWeek.Friday => "FRI",
        DayOfWeek.Saturday => "SAT",
        _ => "SUN"
    };

    /// <summary>
    /// Builds the schtasks.exe command line that registers the scan.
    ///
    /// Three choices worth explaining, because they look like omissions:
    ///
    /// <list type="bullet">
    /// <item><description>
    /// No <c>/RU</c> or <c>/RP</c>. Without them schtasks registers the task for the account
    /// running it, set to "run only when the user is logged on", and never asks for a
    /// password. That is exactly what is wanted: winget is a per-user MSIX package and
    /// misbehaves under SYSTEM or with no loaded user profile, so a task that runs when
    /// nobody is signed in would fail every time it fired.
    /// </description></item>
    /// <item><description>
    /// <c>/RL HIGHEST</c>, because AppGeek's manifest requires administrator and a task
    /// without it would sit behind a UAC prompt nobody is there to answer.
    /// </description></item>
    /// <item><description>
    /// <c>/F</c>, so changing the schedule replaces the task rather than failing on a name
    /// clash.
    /// </description></item>
    /// </list>
    /// </summary>
    public static string BuildCreateArguments(ScanSchedulePlan plan, string exePath)
    {
        if (!plan.NeedsScheduledTask)
            throw new ArgumentException("Only Daily and Weekly schedules register a task.", nameof(plan));
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("The AppGeek executable path is required.", nameof(exePath));

        // schtasks parses /TR itself, so the inner quotes around the exe path have to be
        // doubled. Getting this wrong produces a task that registers happily and then fails
        // to start, which is a miserable thing to debug.
        var action = $"\"\\\"{exePath}\\\" {ScanArgument}\"";

        var args = new List<string>
        {
            "/Create", "/F",
            "/TN", Quote(TaskName),
            "/TR", action,
            "/SC", plan.Kind == ScanScheduleKind.Weekly ? "WEEKLY" : "DAILY"
        };

        if (plan.Kind == ScanScheduleKind.Weekly)
        {
            args.Add("/D");
            args.Add(ShortDay(plan.Day));
        }

        args.Add("/ST");
        args.Add(plan.TimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        args.Add("/RL");
        args.Add("HIGHEST");

        return string.Join(" ", args);
    }

    public static string BuildDeleteArguments() => $"/Delete /F /TN {Quote(TaskName)}";

    public static string BuildQueryArguments() => $"/Query /TN {Quote(TaskName)}";

    private static string Quote(string value) => $"\"{value}\"";
}
