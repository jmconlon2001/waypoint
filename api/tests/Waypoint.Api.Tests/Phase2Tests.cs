using System.Text;
using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;
using Waypoint.Api.Integrations;
using Waypoint.Api.Services;

namespace Waypoint.Api.Tests;

/// <summary>Calendar import, meeting time, focus/switch metrics, planned vs. actual, roll-over, reports.</summary>
public class Phase2Tests
{
    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    /// <summary>A Google-style feed. "Today" in the tests is Tue 17 Nov 2026 (New York, UTC-5).</summary>
    internal const string SampleIcs = """
        BEGIN:VCALENDAR
        PRODID:-//Google Inc//Google Calendar 70.9054//EN
        VERSION:2.0
        CALSCALE:GREGORIAN
        X-WR-CALNAME:jacob@cfs.com
        X-WR-TIMEZONE:America/New_York
        BEGIN:VTIMEZONE
        TZID:America/New_York
        BEGIN:DAYLIGHT
        TZOFFSETFROM:-0500
        TZOFFSETTO:-0400
        TZNAME:EDT
        DTSTART:19700308T020000
        RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU
        END:DAYLIGHT
        BEGIN:STANDARD
        TZOFFSETFROM:-0400
        TZOFFSETTO:-0500
        TZNAME:EST
        DTSTART:19701101T020000
        RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU
        END:STANDARD
        END:VTIMEZONE
        BEGIN:VEVENT
        DTSTART;TZID=America/New_York:20261103T090000
        DTEND;TZID=America/New_York:20261103T091500
        RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=6
        EXDATE;TZID=America/New_York:20261110T090000
        UID:standup@google.com
        SUMMARY:SOX standup
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:jacob@cfs.com
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:raj@cfs.com
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:amy@cfs.com
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:c_123@resource.calendar.google.com
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;TZID=America/New_York:20261124T100000
        DTEND;TZID=America/New_York:20261124T101500
        RECURRENCE-ID;TZID=America/New_York:20261124T090000
        UID:standup@google.com
        SUMMARY:SOX standup (moved)
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:jacob@cfs.com
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:raj@cfs.com
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:amy@cfs.com
        END:VEVENT
        BEGIN:VEVENT
        DTSTART:20261117T180000Z
        DTEND:20261117T183000Z
        UID:oneonone@google.com
        SUMMARY:Raj / Jacob
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:jacob@cfs.com
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:raj@cfs.com
        END:VEVENT
        BEGIN:VEVENT
        DTSTART:20261118T150000Z
        DTEND:20261118T160000Z
        UID:vendor@google.com
        SUMMARY:Vendor sync
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:jacob@cfs.com
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:amy@cfs.com
        ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:bob@vendor.example
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;VALUE=DATE:20261119
        DTEND;VALUE=DATE:20261120
        UID:ooo@google.com
        SUMMARY:OOO
        END:VEVENT
        BEGIN:VEVENT
        DTSTART:20261119T140000Z
        DTEND:20261119T180000Z
        UID:training@google.com
        SUMMARY:Training
        END:VEVENT
        BEGIN:VEVENT
        DTSTART:20261117T190000Z
        DTEND:20261117T193000Z
        UID:townhall@google.com
        SUMMARY:Optional town hall
        ATTENDEE;PARTSTAT=DECLINED:mailto:jacob@cfs.com
        ATTENDEE;PARTSTAT=ACCEPTED:mailto:ceo@cfs.com
        END:VEVENT
        BEGIN:VEVENT
        DTSTART:20261117T200000Z
        DTEND:20261117T210000Z
        UID:focus@google.com
        SUMMARY:Focus time
        END:VEVENT
        BEGIN:VEVENT
        DTSTART:20261118T200000Z
        DTEND:20261118T210000Z
        UID:cancelled@google.com
        STATUS:CANCELLED
        SUMMARY:Cancelled thing
        END:VEVENT
        END:VCALENDAR
        """;

    // ---- iCal parsing ----

    [Fact]
    public void Ics_parser_expands_recurrence_and_applies_exdate_override_and_cancellation()
    {
        var parsed = IcsParser.Parse(SampleIcs, Utc(1, 0), new DateTime(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("jacob@cfs.com", parsed.OwnerEmail);
        var standups = parsed.Events.Where(e => e.Title.StartsWith("SOX standup")).OrderBy(e => e.StartUtc).ToList();
        // Nov 3, (Nov 10 excluded), Nov 17, Nov 24 moved to 10:00, Dec 1.
        Assert.Equal([Utc(3, 14), Utc(17, 14), Utc(24, 15), new DateTime(2026, 12, 1, 14, 0, 0, DateTimeKind.Utc)], standups.Select(s => s.StartUtc));
        Assert.All(standups, s => Assert.True(s.IsRecurring));
        Assert.Equal("SOX standup (moved)", standups[2].Title);
        Assert.DoesNotContain(parsed.Events, e => e.Title == "Cancelled thing");

        var ooo = parsed.Events.Single(e => e.Title == "OOO");
        Assert.True(ooo.IsAllDay);
        Assert.True(parsed.Events.Single(e => e.Title == "Optional town hall").IsFree);
        Assert.Contains("c_123@resource.calendar.google.com", standups[0].AttendeeEmails);
    }

    [Theory]
    [InlineData("Focus time", 0, false, MeetingType.Focus)]
    [InlineData("Raj / Jacob", 2, false, MeetingType.OneOnOne)]
    [InlineData("Standup", 4, true, MeetingType.Team)]
    [InlineData("Workshop", 4, false, MeetingType.AdHoc)]
    [InlineData("Weekly hold", 0, true, MeetingType.Recurring)]
    [InlineData("Quick chat", 0, false, MeetingType.AdHoc)]
    public void Meetings_are_classified(string title, int attendees, bool recurring, MeetingType expected)
    {
        var emails = Enumerable.Range(0, attendees).Select(i => $"p{i}@cfs.com").ToList();
        Assert.Equal(expected, Meetings.Classify(title, emails, recurring, ["cfs.com"]));
    }

    [Fact]
    public void External_attendee_wins_and_rooms_are_not_people()
    {
        Assert.Equal(MeetingType.External, Meetings.Classify("Vendor", ["me@cfs.com", "bob@vendor.example"], false, ["cfs.com"]));
        Assert.Equal(MeetingType.OneOnOne, Meetings.Classify("1:1", ["me@cfs.com", "raj@uk.cfs.com", "room@resource.calendar.google.com"], false, ["cfs.com"]));
    }

    [Theory]
    [InlineData("https://calendar.google.com/calendar/ical/x%40cfs.com/private-abc/basic.ics", true)]
    [InlineData("http://calendar.google.com/calendar/ical/x/basic.ics", false)]
    [InlineData("https://evil.example/basic.ics", false)]
    [InlineData("https://calendar.google.com.evil.example/basic.ics", false)]
    [InlineData("not a url", false)]
    public void Only_https_google_calendar_addresses_are_allowed(string url, bool allowed) =>
        Assert.Equal(allowed, HttpCalendarFeed.IsAllowedUrl(url, out _));

    // ---- Daily summary with meetings ----

    [Fact]
    public void Meetings_win_over_overlapping_timers_and_close_gaps()
    {
        var day = new Interval(Utc(17, 5), Utc(18, 5));
        var work = new Interval(Utc(17, 14), Utc(17, 16));
        var entries = new[] { new SummaryEntry(Utc(17, 14), Utc(17, 15), TimeEntryKind.Task, 1, "Origination", "#7c3aed", TaskId: 7, Id: 70) };
        var meetings = new[]
        {
            new SummaryMeeting(Utc(17, 14, 30), Utc(17, 15, 30), 2, "SOX/J-SOX", "#e11d48", false),
            new SummaryMeeting(Utc(17, 15), Utc(17, 15, 45), null, null, null, false), // overlaps the first: counted once
            new SummaryMeeting(Utc(17, 20), Utc(17, 21), null, null, null, true),       // Focus Time: planned only
        };

        var s = DailySummary.Build(day, work, Utc(17, 23), entries, [], TimeSpan.FromMinutes(2), meetings);

        Assert.Equal(75, s.MeetingMinutes);
        Assert.Equal(2, s.MeetingCount);
        Assert.Equal(30, s.TaskMinutes);
        Assert.Equal(60, s.PlannedFocusMinutes);
        Assert.Equal(new MeetingOverlap(70, 30), Assert.Single(s.Overlaps));
        Assert.Equal(60, s.ByProject.Single(p => p.ProjectId == 2).Minutes);
        Assert.Equal(15, s.ByProject.Single(p => p.Name == DailySummary.UnmappedMeetings).Minutes);
        // Work hours 14:00-16:00: busy until 15:45, then a 15-minute gap.
        Assert.Equal(new Interval(Utc(17, 15, 45), Utc(17, 16)), Assert.Single(s.Gaps));
    }

    [Fact]
    public void Meetings_later_today_do_not_count_yet()
    {
        var day = new Interval(Utc(17, 5), Utc(18, 5));
        var meetings = new[] { new SummaryMeeting(Utc(17, 15, 30), Utc(17, 16, 30), null, null, null, false) };
        var s = DailySummary.Build(day, day, Utc(17, 16), [], [], TimeSpan.FromMinutes(2), meetings);
        Assert.Equal(30, s.MeetingMinutes);
    }

    [Fact]
    public void Focus_blocks_need_25_uninterrupted_minutes_on_one_task()
    {
        var day = new Interval(Utc(17, 5), Utc(18, 5));
        SummaryEntry E(int h1, int m1, int h2, int m2, int task) =>
            new(Utc(17, h1, m1), Utc(17, h2, m2), TimeEntryKind.Task, 1, "P", "#000", task);
        var entries = new[]
        {
            E(14, 0, 14, 15, 1), E(14, 16, 14, 30, 1), // stop/start within 2 min on the same task: joined into 30 min
            E(15, 0, 15, 40, 2),                        // 40 min, but an interruption at 15:20-15:25 splits it 20 + 15
            E(16, 0, 16, 20, 3),                        // too short
        };
        var interruptions = new (DateTime, DateTime?)[] { (Utc(17, 15, 20), Utc(17, 15, 25)) };

        var s = DailySummary.Build(day, day, Utc(17, 23), entries, interruptions, TimeSpan.FromMinutes(2));

        var block = Assert.Single(s.FocusBlocks);
        Assert.Equal(1, block.TaskId);
        Assert.Equal(30, block.Interval.Minutes);
        Assert.Equal(30, s.LongestFocusBlockMinutes);
    }

    [Fact]
    public void Task_switches_count_changes_of_the_active_timer()
    {
        SummaryEntry E(int h, int? task, TimeEntryKind kind = TimeEntryKind.Task) =>
            new(Utc(17, h), Utc(17, h, 30), kind, 1, "P", "#000", task);
        // A, A (resumed after an interruption), B, reactive, B.
        var entries = new[] { E(14, 1), E(15, 1), E(16, 2), E(17, null, TimeEntryKind.Reactive), E(18, 2) };
        Assert.Equal(3, DailySummary.CountSwitches(entries));
    }

    // ---- Calendar sync ----

    [Fact]
    public async Task Sync_imports_classifies_maps_and_removes_deleted_events()
    {
        using var app = new TestApp();
        app.Feed.Ics = SampleIcs;
        await app.Calendar.SetSourceAsync(new CalendarSourceInput("https://calendar.google.com/calendar/ical/x/private-abc/basic.ics", null));
        await app.Calendar.CreateRuleAsync(new MeetingRuleInput("sox", app.ProjectId("SOX/J-SOX")));

        var r = await app.Calendar.SyncAsync();
        Assert.Equal(10, r.Total);
        Assert.Equal(10, r.Added);
        Assert.NotNull(r.Status.LastSyncAt);

        var events = await app.Db.CalendarEvents.ToListAsync();
        Assert.Equal(MeetingType.Team, events.First(e => e.Title == "SOX standup").MeetingType);
        Assert.Equal(MeetingType.OneOnOne, events.Single(e => e.Title == "Raj / Jacob").MeetingType);
        // Internal domain comes from the calendar owner's address.
        Assert.Equal(MeetingType.External, events.Single(e => e.Title == "Vendor sync").MeetingType);
        Assert.Equal(MeetingType.Focus, events.Single(e => e.Title == "Focus time").MeetingType);
        Assert.All(events.Where(e => e.Title.StartsWith("SOX")), e => Assert.Equal(app.ProjectId("SOX/J-SOX"), e.ProjectId));

        // A rule added later re-maps stored events.
        await app.Calendar.CreateRuleAsync(new MeetingRuleInput("Vendor", app.ProjectId("Origination")));
        Assert.Equal(app.ProjectId("Origination"), (await app.Db.CalendarEvents.SingleAsync(e => e.Title == "Vendor sync")).ProjectId);

        // The 1:1 is deleted in Google: the next sync removes it.
        app.Feed.Ics = SampleIcs.Replace("UID:oneonone@google.com", "UID:oneonone@google.com\nSTATUS:CANCELLED");
        r = await app.Calendar.SyncAsync();
        Assert.Equal(1, r.Removed);
        Assert.False(await app.Db.CalendarEvents.AnyAsync(e => e.Title == "Raj / Jacob"));
    }

    [Fact]
    public async Task Sync_failure_is_saved_and_reported()
    {
        using var app = new TestApp();
        await app.Calendar.SetSourceAsync(new CalendarSourceInput("https://calendar.google.com/calendar/ical/x/private-abc/basic.ics", null));
        app.Feed.Error = new HttpRequestException("Google Calendar answered 404 Not Found.");

        var ex = await Assert.ThrowsAsync<ApiException>(() => app.Calendar.SyncAsync());
        Assert.Equal(502, ex.StatusCode);
        var status = await app.Calendar.GetStatusAsync();
        Assert.Contains("404", status.LastError);
        Assert.DoesNotContain("private-abc", status.AddressHint ?? "");
    }

    [Fact]
    public async Task Calendar_source_rejects_other_hosts_and_can_be_cleared()
    {
        using var app = new TestApp();
        await Assert.ThrowsAsync<ApiException>(() => app.Calendar.SetSourceAsync(new CalendarSourceInput("https://evil.example/x.ics", null)));
        await app.Calendar.SetSourceAsync(new CalendarSourceInput("https://calendar.google.com/calendar/ical/x/basic.ics", "@CFS.com, cfs.co.jp"));
        var status = await app.Calendar.GetStatusAsync();
        Assert.True(status.Configured);
        Assert.Equal("cfs.com, cfs.co.jp", status.InternalDomains);

        status = await app.Calendar.SetSourceAsync(new CalendarSourceInput("", null));
        Assert.False(status.Configured);
    }

    // ---- Review with meetings ----

    [Fact]
    public async Task Review_shows_meetings_flags_overlapping_timer_and_counts_meeting_time()
    {
        using var app = new TestApp(Utc(17, 13, 50)); // 08:50 New York
        app.Feed.Ics = SampleIcs;
        await app.Calendar.SetSourceAsync(new CalendarSourceInput("https://calendar.google.com/calendar/ical/x/basic.ics", null));
        await app.Calendar.SyncAsync();

        var task = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Draft controls memo #sox", true))).Task;
        await app.Timer.StartAsync(new StartTimerRequest(task.Id, null, null, null));
        app.Clock.Advance(TimeSpan.FromMinutes(40)); // timer left running through the 09:00 standup
        await app.Timer.StopAsync();
        app.Clock.UtcNow = Utc(17, 22); // 17:00 New York

        var review = await app.Review.GetDayAsync(null);

        Assert.Equal(25, review.Totals.TaskMinutes);
        Assert.Equal(45, review.Totals.MeetingMinutes);      // standup 15 + 1:1 30; town hall declined
        Assert.Equal(2, review.Totals.MeetingCount);
        Assert.Equal(60, review.Totals.PlannedFocusMinutes);  // Focus time block
        var overlap = Assert.Single(review.Overlaps);
        Assert.Equal("SOX standup", overlap.MeetingTitle);
        Assert.Equal(15, overlap.Minutes);
        Assert.Contains(review.Meetings, m => m.Title == "Optional town hall" && !m.CountsAsMeeting && m.ExcludedReason == "Free or declined");
        // Meetings close gaps: no gap during the 1:1 (13:00-13:30 New York).
        Assert.DoesNotContain(review.Gaps, g => g.Start < Utc(17, 18, 30) && g.End > Utc(17, 18));
    }

    [Fact]
    public async Task Long_and_all_day_events_are_excluded_by_the_configurable_threshold()
    {
        using var app = new TestApp(Utc(19, 22));
        app.Feed.Ics = SampleIcs;
        await app.Calendar.SetSourceAsync(new CalendarSourceInput("https://calendar.google.com/calendar/ical/x/basic.ics", null));
        await app.Calendar.SyncAsync();

        var review = await app.Review.GetDayAsync(null);
        Assert.Equal(0, review.Totals.MeetingMinutes);
        Assert.Contains(review.Meetings, m => m.Title == "Training" && m.ExcludedReason == "Longer than 120 min");
        Assert.Contains(review.Meetings, m => m.Title == "OOO" && m.ExcludedReason == "All-day");

        var s = await app.Settings.GetDtoAsync();
        await app.Settings.UpdateAsync(new SettingsInput(s.WorkdayStart, s.WorkdayEnd, s.TimerWarningMinutes, s.TimeZone, 300, null));
        Assert.Equal(240, (await app.Review.GetDayAsync(null)).Totals.MeetingMinutes);
    }

    // ---- Planned vs. actual and roll-over ----

    [Fact]
    public async Task Planned_vs_actual_separates_planned_and_unplanned_work()
    {
        using var app = new TestApp();
        var planned = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Planned #admin", true))).Task;
        var adHoc = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Ad hoc #admin", false))).Task;

        await app.Timer.StartAsync(new StartTimerRequest(planned.Id, null, null, null));
        app.Clock.Advance(TimeSpan.FromMinutes(30));
        await app.Timer.StartAsync(new StartTimerRequest(adHoc.Id, null, null, null)); // starting a timer doesn't plan it
        app.Clock.Advance(TimeSpan.FromMinutes(20));
        await app.Timer.StopAsync();

        var plan = await app.Reports.GetPlanAsync(null);
        Assert.Equal(1, plan.PlannedCount);
        Assert.Equal(30, plan.PlannedMinutes);
        Assert.Equal(20, plan.UnplannedTaskMinutes);
        Assert.Equal("Ad hoc", Assert.Single(plan.Unplanned).Title);
    }

    [Fact]
    public async Task Roll_over_moves_unfinished_tasks_and_keeps_todays_plan_history()
    {
        using var app = new TestApp();
        var a = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Unfinished #admin", true))).Task;
        var b = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Park it #admin", true))).Task;

        var review = await app.Review.GetDayAsync(null);
        Assert.Equal(2, review.Unfinished.Count);

        var tomorrow = new DateOnly(2026, 11, 18);
        Assert.Equal(1, (await app.Tasks.RollOverAsync(new RollOverRequest([a.Id], tomorrow))).Moved);
        await app.Tasks.RollOverAsync(new RollOverRequest([b.Id], null));

        var movedA = await app.Db.Tasks.FindAsync(a.Id);
        Assert.Equal(tomorrow, movedA!.PlannedDate);
        Assert.Equal(WorkTaskStatus.Backlog, movedA.Status);
        Assert.Null((await app.Db.Tasks.FindAsync(b.Id))!.PlannedDate);
        Assert.Empty((await app.Review.GetDayAsync(null)).Unfinished);

        // Today's plan still shows both (not done); tomorrow's has the rolled-over one.
        Assert.Equal(2, (await app.Reports.GetPlanAsync(new DateOnly(2026, 11, 17))).PlannedCount);
        Assert.Equal("Unfinished", Assert.Single((await app.Reports.GetPlanAsync(tomorrow)).Planned).Title);

        // Tomorrow it's back on the Today list.
        app.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Contains(await app.Tasks.TodayAsync(), t => t.Id == a.Id);
        await Assert.ThrowsAsync<ApiException>(() => app.Tasks.RollOverAsync(new RollOverRequest([a.Id], new DateOnly(2026, 11, 18))));
    }

    [Fact]
    public async Task Unplanning_the_same_day_forgets_the_plan()
    {
        using var app = new TestApp();
        var t = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Oops #admin", true))).Task;
        await app.Tasks.PlanAsync(t.Id, false);
        Assert.Equal(0, (await app.Reports.GetPlanAsync(null)).PlannedCount);
    }

    // ---- Weekly summary, interruption stats, CSV ----

    [Fact]
    public async Task Weekly_summary_totals_days_trend_and_interruptions()
    {
        using var app = new TestApp(Utc(10, 15)); // the week before: Tue 10 Nov
        var task = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Deep work #origination", true))).Task;
        await app.Timer.StartAsync(new StartTimerRequest(task.Id, null, null, null));
        app.Clock.Advance(TimeSpan.FromMinutes(60));
        await app.Timer.StopAsync();

        app.Clock.UtcNow = Utc(17, 15); // this week
        await app.Timer.StartAsync(new StartTimerRequest(task.Id, null, null, null));
        app.Clock.Advance(TimeSpan.FromMinutes(30));
        var i = await app.Interruptions.StartAsync(new InterruptionDetails(InterruptionSource.WalkUp, null, "Raj", null, true));
        app.Clock.Advance(TimeSpan.FromMinutes(10));
        await app.Interruptions.ResumeAsync(i.Id, null);
        app.Clock.Advance(TimeSpan.FromMinutes(20));
        await app.Timer.StopAsync();
        app.Clock.UtcNow = Utc(17, 22);

        var week = await app.Reports.GetWeekAsync(null);

        Assert.Equal(new DateOnly(2026, 11, 16), week.WeekStart);
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(50, week.Totals.FocusMinutes);
        Assert.Equal(1, week.Totals.InterruptionCount);
        Assert.Equal(1, week.Totals.FocusBlockCount); // the first 30 minutes
        Assert.Equal(30, week.LongestFocusBlock!.Minutes);
        Assert.Equal(1, week.TrendWeeks);
        Assert.Equal(60, week.TrendAverage.FocusMinutes);
        Assert.Equal(0, week.Days.Single(d => d.Date == new DateOnly(2026, 11, 21)).UnloggedMinutes); // idle Saturday
        Assert.Equal(0, week.Days.Single(d => d.Date == new DateOnly(2026, 11, 16)).UnloggedMinutes); // idle Monday: a day off
        Assert.True(week.Days.Single(d => d.Date == new DateOnly(2026, 11, 17)).UnloggedMinutes > 0); // a tracked day still has gaps

        var walkUp = Assert.Single(week.Interruptions.BySource);
        Assert.Equal("Walk-up", walkUp.Label);
        Assert.Equal("Raj", Assert.Single(week.Interruptions.ByPerson).Label);
        Assert.Equal(1, week.Interruptions.CouldWaitCount);
        Assert.Equal(1, week.Interruptions.Heatmap[1][10]); // Tuesday, 10:30 New York
        Assert.Equal(1, week.Interruptions.ByHour[10]);
    }

    [Fact]
    public async Task Csv_export_escapes_values_and_defuses_formulas()
    {
        using var app = new TestApp();
        var t = (await app.Tasks.CreateAsync(new TaskInput("=HYPERLINK(\"x\"), \"quoted\"", app.ProjectId("Admin"), 2, null, null, WorkTaskStatus.Today, null)));
        await app.Timer.StartAsync(new StartTimerRequest(t.Id, null, null, null));
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        await app.Timer.StopAsync();

        var (content, name) = await app.Reports.ExportCsvAsync("time-entries", null, null);
        var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("waypoint-time-entries-", name);
        Assert.StartsWith("id,date,start,end,minutes,kind,project,task", lines[0]);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\"), \"\"quoted\"\"\"", lines[1]);
        Assert.Contains(",5,Task,Admin,", lines[1]);

        var daily = Encoding.UTF8.GetString((await app.Reports.ExportCsvAsync("daily", new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 17))).Content);
        Assert.Equal(3, daily.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        await Assert.ThrowsAsync<ApiException>(() => app.Reports.ExportCsvAsync("passwords", null, null));
    }

    [Fact]
    public async Task Settings_validate_meeting_limit_and_reminder()
    {
        using var app = new TestApp();
        var s = await app.Settings.GetDtoAsync();
        Assert.Equal(120, s.MeetingMaxMinutes);
        Assert.Null(s.ReviewReminderTime);

        var updated = await app.Settings.UpdateAsync(new SettingsInput(s.WorkdayStart, s.WorkdayEnd, 180, s.TimeZone, 90, "17:15"));
        Assert.Equal(90, updated.MeetingMaxMinutes);
        Assert.Equal("17:15", updated.ReviewReminderTime);

        await Assert.ThrowsAsync<ApiException>(() => app.Settings.UpdateAsync(new SettingsInput(s.WorkdayStart, s.WorkdayEnd, 180, s.TimeZone, 5, null)));
        await Assert.ThrowsAsync<ApiException>(() => app.Settings.UpdateAsync(new SettingsInput(s.WorkdayStart, s.WorkdayEnd, 180, s.TimeZone, null, "late")));
        Assert.Null((await app.Settings.UpdateAsync(new SettingsInput(s.WorkdayStart, s.WorkdayEnd, 180, s.TimeZone, null, ""))).ReviewReminderTime);
    }

    // ---- Overlapping entries: the entry being saved carves out its time ----

    private static async Task<TimeEntryDto> Log(TestApp app, string project, int h1, int m1, int h2, int m2, string? note = null) =>
        await app.Entries.CreateAsync(new TimeEntryInput(null, app.ProjectId(project), TimeEntryKind.Task, Utc(17, h1, m1), Utc(17, h2, m2), note));

    private static async Task<List<(string Project, DateTime Start, DateTime? End)>> Entries(TestApp app) =>
        (await app.Db.TimeEntries.Include(e => e.Project).OrderBy(e => e.StartedAt).ToListAsync())
            .Select(e => (e.Project!.Name, e.StartedAt, e.EndedAt)).ToList();

    [Fact]
    public async Task Logging_inside_a_long_entry_splits_it_around_the_new_one()
    {
        using var app = new TestApp(Utc(17, 20));
        await Log(app, "Origination", 13, 30, 18, 31, "long block");
        var inner = await Log(app, "Extractor migration", 15, 50, 16, 20);

        Assert.Equal(["Split \"Origination\" around this entry"], inner.Adjusted);
        Assert.Equal(
            [("Origination", Utc(17, 13, 30), (DateTime?)Utc(17, 15, 50)), ("Extractor migration", Utc(17, 15, 50), Utc(17, 16, 20)), ("Origination", Utc(17, 16, 20), Utc(17, 18, 31))],
            await Entries(app));
        Assert.Equal(2, await app.Db.TimeEntries.CountAsync(e => e.Note == "long block"));
        Assert.Equal(301, (await app.Review.GetDayAsync(null)).Totals.TaskMinutes); // 13:30-18:31, counted once
    }

    [Fact]
    public async Task Overlapping_one_end_trims_and_covering_replaces()
    {
        using var app = new TestApp(Utc(17, 20));
        await Log(app, "Admin", 14, 0, 15, 0);
        await Log(app, "Origination", 16, 0, 16, 30);

        var r = await Log(app, "Extractor migration", 14, 30, 16, 45);

        Assert.Contains("Trimmed the end of \"Admin\"", r.Adjusted!);
        Assert.Contains("Replaced \"Origination\"", r.Adjusted!);
        Assert.Equal([("Admin", Utc(17, 14), (DateTime?)Utc(17, 14, 30)), ("Extractor migration", Utc(17, 14, 30), Utc(17, 16, 45))], await Entries(app));
    }

    [Fact]
    public async Task Editing_only_carves_newly_covered_time()
    {
        using var app = new TestApp(Utc(17, 20));
        var outer = await Log(app, "Origination", 14, 0, 16, 0);
        // An overlap that already exists (e.g. from before this rule): saving the long entry's note leaves it alone.
        app.Db.TimeEntries.Add(new TimeEntry { ProjectId = app.ProjectId("Admin"), Kind = TimeEntryKind.Task, StartedAt = Utc(17, 15), EndedAt = Utc(17, 15, 30) });
        await app.Db.SaveChangesAsync();

        var saved = await app.Entries.UpdateAsync(outer.Id, new TimeEntryInput(null, app.ProjectId("Origination"), TimeEntryKind.Task, Utc(17, 14), Utc(17, 16), "new note"));
        Assert.Empty(saved.Adjusted!);
        Assert.Equal(2, await app.Db.TimeEntries.CountAsync());

        var review = await app.Review.GetDayAsync(null);
        var overlap = Assert.Single(review.EntryOverlaps);
        Assert.Equal("Admin", overlap.KeepTitle);
        Assert.Equal(30, overlap.Minutes);

        // One click on the review warning resolves it: the short entry keeps its time.
        var kept = await app.Entries.KeepAsync(overlap.KeepEntryId);
        Assert.Equal(["Split \"Origination\" around this entry"], kept.Adjusted);
        Assert.Empty((await app.Review.GetDayAsync(null)).EntryOverlaps);
    }

    [Fact]
    public async Task Logging_inside_the_running_timer_splits_it_and_it_keeps_running()
    {
        using var app = new TestApp(Utc(17, 14));
        var task = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Deep work #origination", true))).Task;
        await app.Timer.StartAsync(new StartTimerRequest(task.Id, null, null, null));
        app.Clock.UtcNow = Utc(17, 16);

        await Log(app, "Admin", 14, 30, 15, 0);

        var running = Assert.Single(await app.Db.TimeEntries.Where(e => e.EndedAt == null).ToListAsync());
        Assert.Equal(Utc(17, 15), running.StartedAt);
        Assert.Equal(task.Id, running.TaskId);
        Assert.Equal(Utc(17, 14, 30), (await app.Db.TimeEntries.SingleAsync(e => e.TaskId == task.Id && e.EndedAt != null)).EndedAt);
    }

    [Fact]
    public async Task Slivers_under_a_minute_are_dropped()
    {
        using var app = new TestApp(Utc(17, 20));
        await Log(app, "Admin", 14, 0, 15, 0);
        var r = await Log(app, "Origination", 14, 0, 14, 59); // leaves 1 minute: kept
        Assert.Contains("Trimmed the start of \"Admin\"", r.Adjusted!);
        Assert.Equal(Utc(17, 14, 59), (await app.Db.TimeEntries.SingleAsync(e => e.Project!.Name == "Admin")).StartedAt);

        // Covering all but 30 seconds of what's left: the 30-second sliver goes too.
        r = await app.Entries.CreateAsync(new TimeEntryInput(null, app.ProjectId("Origination"), TimeEntryKind.Task, Utc(17, 14, 59), Utc(17, 14, 59).AddSeconds(30), null));
        Assert.Equal(["Replaced \"Admin\""], r.Adjusted);
        Assert.False(await app.Db.TimeEntries.AnyAsync(e => e.Project!.Name == "Admin"));
    }

    [Fact]
    public async Task Back_to_work_with_an_empty_body_keeps_saved_interruption_details()
    {
        using var app = new TestApp();
        var i = await app.Interruptions.StartAsync(null);
        await app.Interruptions.UpdateAsync(i.Id, new InterruptionDetails(InterruptionSource.WalkUp, null, "Marcus", "Report access", true));
        app.Clock.Advance(TimeSpan.FromMinutes(5));

        // What the "b" shortcut / "Back to work" button sends.
        var r = await app.Interruptions.ResumeAsync(i.Id, new InterruptionDetails(null, null, null, null, null));

        Assert.Equal("Marcus", r.Interruption.PersonName);
        Assert.Equal("Report access", r.Interruption.Note);
        Assert.True(r.Interruption.CouldWait);
        Assert.Equal(InterruptionSource.WalkUp, r.Interruption.SourceType);
    }
}
