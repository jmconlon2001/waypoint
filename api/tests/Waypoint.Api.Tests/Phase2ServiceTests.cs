using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Services;

namespace Waypoint.Api.Tests;

public class Phase2ServiceTests
{
    // TestApp's clock: Tue 17 Nov 2026 10:00 New York (15:00 UTC).
    private static DateTime Utc(int h, int m = 0, int day = 17) => new(2026, 11, day, h, m, 0, DateTimeKind.Utc);

    private static async Task UseICal(TestApp app)
    {
        var s = await app.Settings.GetAsync();
        s.CalendarSource = CalendarSourceKind.ICal;
        s.InternalDomains = "cfs.canon.com";
        await app.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rollover_moves_unfinished_today_tasks_to_tomorrow_with_opt_out()
    {
        using var app = new TestApp();
        var a = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Keep going", true))).Task;
        var b = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Drop this", true))).Task;
        var c = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Finished", true))).Task;
        await app.Tasks.SetStatusAsync(c.Id, WorkTaskStatus.Done);
        await app.Timer.StartAsync(new StartTimerRequest(a.Id, null, null, null)); // Doing

        var candidates = await app.Tasks.RolloverCandidatesAsync(null);
        Assert.Equal(2, candidates.Count);

        var r = await app.Tasks.RolloverAsync(new RolloverRequest(null, new[] { b.Id }));

        Assert.Equal(new DateOnly(2026, 11, 18), r.ToDate);
        Assert.Equal(1, r.Rolled);
        Assert.Equal(1, r.Unplanned);
        var ta = await app.Db.Tasks.FindAsync(a.Id);
        Assert.Equal(new DateOnly(2026, 11, 18), ta!.PlannedDate);
        Assert.Equal(WorkTaskStatus.Today, ta.Status);
        var tb = await app.Db.Tasks.FindAsync(b.Id);
        Assert.Null(tb!.PlannedDate);
        Assert.Equal(WorkTaskStatus.Backlog, tb.Status);

        // Rolled tasks leave today's list (except Done ones, which stay as done today) and appear tomorrow.
        Assert.DoesNotContain(await app.Tasks.TodayAsync(), t => t.Id == a.Id);
        app.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Contains(await app.Tasks.TodayAsync(), t => t.Id == a.Id);
        // Both days are in the plan history.
        Assert.Equal(2, await app.Db.TaskPlans.CountAsync(p => p.TaskId == a.Id));
    }

    [Fact]
    public async Task Sync_imports_classifies_maps_rules_and_removes_deleted_events()
    {
        using var app = new TestApp();
        await UseICal(app);
        await app.Calendar.CreateRuleAsync(new MeetingRuleInput("^SOX", app.ProjectId("SOX/J-SOX"), null, null));

        app.Feed.Events.Add(FakeFeed.Meeting("1", "SOX walkthrough", Utc(14), 60));
        app.Feed.Events.Add(FakeFeed.Meeting("2", "1:1 Dana", Utc(16), 30, attendees: 2));
        app.Feed.Events.Add(FakeFeed.Meeting("3", "Offsite", Utc(5), 24 * 60, allDay: true));
        var first = await app.Calendar.SyncAsync();
        Assert.Equal(3, first.Added);

        var sox = await app.Db.CalendarEvents.SingleAsync(e => e.ExternalId == "1");
        Assert.Equal(MeetingType.Team, sox.MeetingType);
        Assert.Equal(app.ProjectId("SOX/J-SOX"), sox.ProjectId);
        Assert.Equal(MeetingType.OneOnOne, (await app.Db.CalendarEvents.SingleAsync(e => e.ExternalId == "2")).MeetingType);

        app.Feed.Events.RemoveAt(1);
        var second = await app.Calendar.SyncAsync();
        Assert.Equal(1, second.Removed);
        Assert.Equal(2, await app.Db.CalendarEvents.CountAsync());
        Assert.NotNull((await app.Settings.GetAsync()).CalendarLastSyncAt);
    }

    [Fact]
    public async Task Review_counts_meetings_as_tracked_and_flags_overlapping_timers()
    {
        // 11:00 local: work started 08:45 (13:45Z).
        using var app = new TestApp(Utc(16));
        await UseICal(app);
        app.Feed.Events.Add(FakeFeed.Meeting("m1", "Staff meeting", Utc(14), 60));               // counted
        app.Feed.Events.Add(FakeFeed.Meeting("m2", "Quarter close", Utc(13), 240));              // > 2h: context only
        app.Feed.Events.Add(FakeFeed.Meeting("m3", "Holiday", Utc(5), 1440, allDay: true));      // all-day: context only
        app.Feed.Events.Add(FakeFeed.Meeting("m4", "Focus", Utc(15, 15), 45, 1, type: "focusTime"));
        await app.Calendar.SyncAsync();

        // A timer that ran 14:30-15:15 overlaps the staff meeting by 30 minutes.
        await app.Entries.CreateAsync(new TimeEntryInput(null, app.ProjectId("Admin"), TimeEntryKind.Task, Utc(14, 30), Utc(15, 15), null));

        var r = await app.Review.GetDayAsync(null);

        Assert.Equal(4, r.CalendarEvents.Count);
        Assert.Single(r.CalendarEvents.Where(e => e.Counted));
        Assert.Equal("longer than 120 min", r.CalendarEvents.Single(e => e.Title == "Quarter close").ExclusionReason);
        Assert.True(r.CalendarEvents.Single(e => e.Title == "Focus").PlannedFocus);
        Assert.Equal(60, r.Totals.MeetingMinutes);
        Assert.Equal(45, r.Totals.PlannedFocusMinutes);

        var overlap = Assert.Single(r.Overlaps);
        Assert.Equal(30, overlap.Minutes);
        Assert.Equal("Staff meeting", overlap.EventTitle);

        // Gaps: 13:45-14:00 and 15:15-16:00 (the meeting and the entry cover 14:00-15:15).
        Assert.Equal(2, r.Gaps.Count);
        Assert.Equal(60, r.Totals.UnloggedMinutes);
    }

    [Fact]
    public async Task Weekly_report_and_aggregates_add_up()
    {
        using var app = new TestApp(Utc(22)); // 17:00 local on Tuesday
        var task = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Deep work #origination", true))).Task;
        await app.Entries.CreateAsync(new TimeEntryInput(task.Id, null, TimeEntryKind.Task, Utc(14), Utc(15), null));
        await app.Entries.CreateAsync(new TimeEntryInput(task.Id, null, TimeEntryKind.Task, Utc(14, 0, day: 16), Utc(14, 30, day: 16), null));
        var i = await app.Interruptions.StartAsync(new InterruptionDetails(InterruptionSource.WalkUp, null, "Dana", null, true));
        app.Clock.Advance(TimeSpan.FromMinutes(10));
        await app.Interruptions.ResumeAsync(i.Id, null);

        var week = await app.Metrics.WeeklyAsync(new DateOnly(2026, 11, 18)); // any day → its Monday
        Assert.Equal(new DateOnly(2026, 11, 16), week.WeekStart);
        Assert.Equal(2, week.Days.Count); // Monday and Tuesday so far
        Assert.Equal(90, week.Kinds.Focus);
        Assert.Equal(2, week.FocusBlocks);
        Assert.Equal(60, week.LongestFocusBlock!.Minutes);
        Assert.Equal("Deep work", week.LongestFocusBlock.TaskTitle);
        Assert.Equal(5, week.Trend.Count);
        Assert.Equal("Walk-up", Assert.Single(week.Interruptions.BySource).Label);
        Assert.Equal(1, week.Interruptions.CouldWaitCount);
        Assert.Equal("Dana", Assert.Single(week.Interruptions.ByPerson).Label);
        var planned = Assert.Single(week.PlannedVsActual);
        Assert.Equal(60, planned.MinutesOnPlannedDays); // planned Tuesday; Monday's 30 min was before it was planned
        Assert.Equal(90, planned.MinutesInRange);

        // Past days used for the trend are stored as aggregates; today isn't stored.
        Assert.True(await app.Db.DailyAggregates.AnyAsync(a => a.Date == new DateOnly(2026, 11, 15)));
        Assert.False(await app.Db.DailyAggregates.AnyAsync(a => a.Date == new DateOnly(2026, 11, 17)));
        var recomputed = await app.Metrics.RecomputeAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 30));
        Assert.Equal(new DateOnly(2026, 11, 17), recomputed.To); // capped at today
        Assert.Equal(8, recomputed.Days);
    }

    [Fact]
    public async Task Csv_export_contains_escaped_rows()
    {
        using var app = new TestApp();
        await app.Entries.CreateAsync(new TimeEntryInput(null, app.ProjectId("Admin"), TimeEntryKind.Task, Utc(13), Utc(14), "=HYPERLINK(\"x\"), note"));
        var csv = await app.Export.ExportAsync("time-entries", new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 17));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("id,date,start_local", lines[0]);
        Assert.Contains("2026-11-17 08:00:00", lines[1]);
        Assert.EndsWith("\"'=HYPERLINK(\"\"x\"\"), note\"", lines[1]);
        await Assert.ThrowsAsync<ApiException>(() => app.Export.ExportAsync("nope", new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 17)));
    }

    [Fact]
    public async Task Invalid_meeting_rule_patterns_are_rejected()
    {
        using var app = new TestApp();
        var ex = await Assert.ThrowsAsync<ApiException>(() => app.Calendar.CreateRuleAsync(new MeetingRuleInput("(unclosed", app.ProjectId("Admin"), null, null)));
        Assert.Equal(400, ex.StatusCode);
        await app.Calendar.CreateRuleAsync(new MeetingRuleInput("standup|sync", app.ProjectId("Origination"), null, null));
        Assert.Equal("Origination", (await app.Calendar.TestRulesAsync("Daily STANDUP")).ProjectName);
        Assert.Null((await app.Calendar.TestRulesAsync("Lunch")).ProjectId);
    }
}
