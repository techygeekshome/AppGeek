using AppGeek.Services;

namespace AppGeek.Tests;

/// <summary>
/// The background scan is registered with Windows by shelling out to schtasks.exe, and a
/// mistyped command line produces a task that registers happily and then never runs. These
/// cover the parsing and the argument building, which is everything that can be checked
/// without a Task Scheduler to talk to.
/// </summary>
public static class ScheduleTests
{
    public static void Run()
    {
        Check.Section("Scan schedule — reading the Settings dropdown");

        var daily = ScanSchedule.Parse("Daily at 03:00");
        Check.Equal("'Daily at 03:00' is a daily schedule", ScanScheduleKind.Daily, daily.Kind);
        Check.Equal("'Daily at 03:00' runs at 3am", new TimeSpan(3, 0, 0), daily.TimeOfDay);
        Check.That("a daily schedule needs a Windows task", daily.NeedsScheduledTask);

        var noon = ScanSchedule.Parse("Daily at 12:00");
        Check.Equal("'Daily at 12:00' runs at midday", new TimeSpan(12, 0, 0), noon.TimeOfDay);

        var weekly = ScanSchedule.Parse("Weekly on Sunday");
        Check.Equal("'Weekly on Sunday' is a weekly schedule", ScanScheduleKind.Weekly, weekly.Kind);
        Check.Equal("'Weekly on Sunday' picks Sunday", DayOfWeek.Sunday, weekly.Day);
        Check.Equal("a weekly option with no time falls back to 3am", new TimeSpan(3, 0, 0), weekly.TimeOfDay);

        var thursday = ScanSchedule.Parse("Weekly on Thursday at 18:30");
        Check.Equal("a day other than Sunday is read", DayOfWeek.Thursday, thursday.Day);
        Check.Equal("a time on a weekly option is read", new TimeSpan(18, 30, 0), thursday.TimeOfDay);

        var startup = ScanSchedule.Parse("Every time AppGeek starts");
        Check.Equal("'Every time AppGeek starts' is a startup scan", ScanScheduleKind.AtStartup, startup.Kind);
        Check.That("a startup scan needs no Windows task", !startup.NeedsScheduledTask);

        var manual = ScanSchedule.Parse("Manually only");
        Check.Equal("'Manually only' is manual", ScanScheduleKind.Manual, manual.Kind);
        Check.That("manual needs no Windows task", !manual.NeedsScheduledTask);

        // An unreadable value must never turn into a schedule the user did not ask for.
        Check.Equal("empty text is manual", ScanScheduleKind.Manual, ScanSchedule.Parse("").Kind);
        Check.Equal("null is manual", ScanScheduleKind.Manual, ScanSchedule.Parse(null).Kind);
        Check.Equal("nonsense is manual, not daily",
            ScanScheduleKind.Manual, ScanSchedule.Parse("whenever it feels like it").Kind);

        // A settings file written by an older build, or edited by hand.
        Check.Equal("a 24-hour time past noon is read correctly",
            new TimeSpan(23, 45, 0), ScanSchedule.Parse("Daily at 23:45").TimeOfDay);
        Check.Equal("a single-digit hour is read correctly",
            new TimeSpan(6, 5, 0), ScanSchedule.Parse("daily at 6:05").TimeOfDay);

        Check.Section("Scan schedule — the schtasks command line");

        var args = ScanSchedule.BuildCreateArguments(daily, @"C:\Program Files\AppGeek\AppGeek.exe");

        Check.That("the task is created", args.Contains("/Create"));
        Check.That("an existing task is replaced rather than clashing", args.Contains("/F"));
        Check.That("it is registered under the AppGeek name", args.Contains("\"AppGeek Scan\""));
        Check.That("it runs daily", args.Contains("/SC DAILY"));
        Check.That("it runs at the chosen time", args.Contains("/ST 03:00"));
        Check.That("it runs elevated, or the UAC prompt nobody is there to answer stops it",
            args.Contains("/RL HIGHEST"));

        // The task must only ever scan. If --scan ever falls out of this line the scheduled
        // task launches the full UI at 3am instead.
        Check.That("the task passes --scan and nothing else", args.Contains("--scan"));
        Check.That("no install switch is passed", !args.Contains("install"));

        // schtasks parses /TR itself, so the path's quotes have to survive being nested.
        Check.That("the exe path stays quoted for schtasks",
            args.Contains(@"\""C:\Program Files\AppGeek\AppGeek.exe\"""));

        var weeklyArgs = ScanSchedule.BuildCreateArguments(
            new ScanSchedulePlan(ScanScheduleKind.Weekly, new TimeSpan(2, 0, 0), DayOfWeek.Wednesday),
            @"C:\AppGeek\AppGeek.exe");
        Check.That("a weekly task says WEEKLY", weeklyArgs.Contains("/SC WEEKLY"));
        Check.That("a weekly task names the day", weeklyArgs.Contains("/D WED"));
        Check.Equal("Monday shortens to MON", "MON", ScanSchedule.ShortDay(DayOfWeek.Monday));
        Check.Equal("Sunday shortens to SUN", "SUN", ScanSchedule.ShortDay(DayOfWeek.Sunday));

        Check.That("deleting names the same task", ScanSchedule.BuildDeleteArguments().Contains("\"AppGeek Scan\""));
        Check.That("deleting does not prompt", ScanSchedule.BuildDeleteArguments().Contains("/F"));

        // Building a command line for a schedule that does not need one is a bug, not a no-op.
        var threw = false;
        try { ScanSchedule.BuildCreateArguments(ScanSchedulePlan.Manual, @"C:\AppGeek.exe"); }
        catch (ArgumentException) { threw = true; }
        Check.That("a manual schedule refuses to build a task", threw);

        var threwOnPath = false;
        try { ScanSchedule.BuildCreateArguments(daily, "  "); }
        catch (ArgumentException) { threwOnPath = true; }
        Check.That("an empty executable path is refused", threwOnPath);
    }
}
