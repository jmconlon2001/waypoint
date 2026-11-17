using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Tests;

public class DomainTests
{
    private static readonly TimeZoneInfo NewYork = LocalTime.FindTimeZone("America/New_York");
    private static DateTime Utc(int h, int m = 0, int day = 17) => new(2026, 11, day, h, m, 0, DateTimeKind.Utc);

    [Fact]
    public void Day_bounds_follow_the_local_time_zone()
    {
        var day = LocalTime.DayBoundsUtc(new DateOnly(2026, 11, 17), NewYork);
        Assert.Equal(Utc(5), day.Start);          // EST is UTC-5
        Assert.Equal(Utc(5, day: 18), day.End);
    }

    [Fact]
    public void Day_bounds_handle_daylight_saving_changes()
    {
        // 2026-11-01 is the fall-back day in the US: 25 hours long.
        var day = LocalTime.DayBoundsUtc(new DateOnly(2026, 11, 1), NewYork);
        Assert.Equal(TimeSpan.FromHours(25), day.End - day.Start);
    }

    [Fact]
    public void Gaps_are_the_uncovered_parts_of_the_window()
    {
        var window = new Interval(Utc(14), Utc(18));
        var busy = new[]
        {
            new Interval(Utc(13), Utc(14, 30)),   // starts before the window
            new Interval(Utc(15), Utc(16)),
            new Interval(Utc(15, 30), Utc(16, 15)), // overlaps the previous one
            new Interval(Utc(16, 16), Utc(17)),    // leaves a 1-minute gap
        };
        var gaps = Intervals.Gaps(window, busy, TimeSpan.FromMinutes(2));
        Assert.Equal(2, gaps.Count);
        Assert.Equal(new Interval(Utc(14, 30), Utc(15)), gaps[0]);
        Assert.Equal(new Interval(Utc(17), Utc(18)), gaps[1]);
    }

    [Fact]
    public void Gaps_of_an_empty_day_cover_the_whole_window()
    {
        var window = new Interval(Utc(14), Utc(18));
        var gaps = Intervals.Gaps(window, [], TimeSpan.FromMinutes(2));
        Assert.Equal(window, Assert.Single(gaps));
    }

    [Fact]
    public void Daily_summary_totals_by_kind_and_project_and_stops_gaps_at_now()
    {
        var day = LocalTime.DayBoundsUtc(new DateOnly(2026, 11, 17), NewYork);
        var work = LocalTime.WorkHoursUtc(new DateOnly(2026, 11, 17), new TimeOnly(8, 45), new TimeOnly(17, 30), NewYork);
        var now = Utc(16); // 11:00 local

        var entries = new[]
        {
            new SummaryEntry(Utc(14), Utc(15), TimeEntryKind.Task, 1, "Origination", "#7c3aed"),
            new SummaryEntry(Utc(15, 10), Utc(15, 30), TimeEntryKind.Reactive, 2, "Admin", "#64748b"),
            new SummaryEntry(Utc(15, 40), null, TimeEntryKind.Task, 1, "Origination", "#7c3aed"), // running
        };
        var interruptions = new (DateTime, DateTime?)[] { (Utc(15), Utc(15, 10)) };

        var s = DailySummary.Build(day, work, now, entries, interruptions, TimeSpan.FromMinutes(2));

        Assert.Equal(80, s.TaskMinutes);
        Assert.Equal(20, s.ReactiveMinutes);
        Assert.Equal(0, s.UntrackedMinutes);
        Assert.Equal(1, s.InterruptionCount);
        Assert.Equal(10, s.InterruptionMinutes);
        Assert.Equal(80, s.ByProject.Single(p => p.ProjectId == 1).Minutes);
        // Work starts 08:45 local (13:45 UTC): gaps 13:45-14:00 and 15:30-15:40; nothing after "now".
        Assert.Equal(25, s.UnloggedMinutes);
        Assert.Equal(2, s.Gaps.Count);
    }

    [Fact]
    public void Daily_summary_clips_entries_that_cross_midnight()
    {
        var day = LocalTime.DayBoundsUtc(new DateOnly(2026, 11, 17), NewYork);
        var work = LocalTime.WorkHoursUtc(new DateOnly(2026, 11, 17), new TimeOnly(8, 45), new TimeOnly(17, 30), NewYork);
        var entries = new[] { new SummaryEntry(Utc(4), Utc(6), TimeEntryKind.Untracked, null, null, null) };
        var s = DailySummary.Build(day, work, Utc(23), entries, [], TimeSpan.FromMinutes(2));
        Assert.Equal(60, s.UntrackedMinutes);
        Assert.Empty(s.ByProject);
    }

    [Fact]
    public void Timer_warns_after_threshold_and_across_midnight()
    {
        Assert.Empty(TimerWarnings.For(Utc(14), Utc(16), 180, NewYork));
        Assert.Equal(new[] { TimerWarnings.LongRunning }, TimerWarnings.For(Utc(14), Utc(17, 1), 180, NewYork));
        // 23:30 local on the 16th (04:30 UTC on the 17th) to 00:30 local on the 17th.
        Assert.Equal(new[] { TimerWarnings.CrossedMidnight }, TimerWarnings.For(Utc(4, 30, day: 17), Utc(5, 30, day: 17), 180, NewYork));
        Assert.Empty(TimerWarnings.For(Utc(14), Utc(23), 0, NewYork)); // 0 turns the threshold off
    }

    [Fact]
    public void Palette_avoids_duplicates_and_validates_colors()
    {
        Assert.Equal(8, ProjectPalette.Colors.Distinct().Count());
        Assert.True(ProjectPalette.IsValid("#E11D48"));
        Assert.False(ProjectPalette.IsValid("#2563eb")); // the UI accent is not a project color
    }
}
