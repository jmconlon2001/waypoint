using System.Text.Json;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Tests;

public class CalendarRulesTests
{
    private static readonly string[] Internal = ["cfs.canon.com", "canon.com"];

    [Theory]
    [InlineData("focusTime", 1, "", false, MeetingType.Focus)]
    [InlineData("default", 2, "cfs.canon.com", false, MeetingType.OneOnOne)]
    [InlineData("default", 2, "cfs.canon.com", true, MeetingType.OneOnOne)]
    [InlineData("default", 5, "cfs.canon.com,canon.com", true, MeetingType.Recurring)]
    [InlineData("default", 5, "cfs.canon.com,us.canon.com", false, MeetingType.Team)]
    [InlineData("default", 3, "cfs.canon.com,vendor.io", true, MeetingType.External)]
    [InlineData("default", 2, "gmail.com,cfs.canon.com", false, MeetingType.External)]
    [InlineData("default", 0, "", false, MeetingType.AdHoc)]
    [InlineData("default", 1, "cfs.canon.com", false, MeetingType.AdHoc)]
    public void Classifies_meetings(string eventType, int attendees, string domains, bool recurring, MeetingType expected)
    {
        var d = domains.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected, CalendarRules.Classify(eventType, attendees, d, recurring, Internal));
    }

    [Fact]
    public void Without_internal_domains_nothing_is_external()
    {
        Assert.Equal(MeetingType.OneOnOne, CalendarRules.Classify("default", 2, new[] { "vendor.io" }, false, Array.Empty<string>()));
    }

    [Fact]
    public void Excludes_all_day_long_declined_and_non_meeting_events()
    {
        var s = new DateTime(2026, 11, 17, 14, 0, 0, DateTimeKind.Utc);
        Assert.True(CalendarRules.IsCounted(false, s, s.AddMinutes(120), "default", false, 120));   // exactly 2h counts
        Assert.False(CalendarRules.IsCounted(false, s, s.AddMinutes(121), "default", false, 120));  // over 2h is context only
        Assert.True(CalendarRules.IsCounted(false, s, s.AddMinutes(240), "default", false, 0));     // 0 = no limit
        Assert.False(CalendarRules.IsCounted(true, s, s.AddDays(1), "default", false, 120));        // all-day
        Assert.False(CalendarRules.IsCounted(false, s, s.AddMinutes(30), "default", true, 120));    // declined
        Assert.False(CalendarRules.IsCounted(false, s, s.AddMinutes(90), "focusTime", false, 120)); // planned focus, not a meeting
        Assert.False(CalendarRules.IsCounted(false, s, s.AddMinutes(60), "outOfOffice", false, 120));
        Assert.Equal("longer than 120 min", CalendarRules.ExclusionReason(false, s, s.AddHours(3), "default", false, 120));
        Assert.True(CalendarRules.IsPlannedFocus(false, "focusTime"));
    }

    [Fact]
    public void Domains_are_extracted_and_deduplicated()
    {
        Assert.Equal(new[] { "cfs.canon.com", "vendor.io" }, CalendarRules.Domains(new[] { "Jacob@CFS.canon.com", "mailto:ann@vendor.io", "bob@cfs.canon.com", "nobody" }));
        Assert.Equal(new[] { "cfs.canon.com", "canon.com" }, CalendarRules.ParseDomainList(" @cfs.canon.com, canon.com;canon.com "));
    }

    [Fact]
    public void First_matching_rule_maps_the_project()
    {
        var rules = new[] { (CalendarRules.CompileRule("^SOX|J-SOX"), 1), (CalendarRules.CompileRule("origination"), 2), (CalendarRules.CompileRule("."), 5) };
        Assert.Equal(1, CalendarRules.MatchProject("SOX walkthrough", rules));
        Assert.Equal(2, CalendarRules.MatchProject("Weekly Origination sync", rules));
        Assert.Equal(5, CalendarRules.MatchProject("Lunch", rules));
        Assert.Null(CalendarRules.MatchProject("x", Array.Empty<(System.Text.RegularExpressions.Regex, int)>()));
    }
}

public class GoogleEventParserTests
{
    private static readonly TimeZoneInfo NewYork = LocalTime.FindTimeZone("America/New_York");

    [Fact]
    public void Parses_timed_all_day_focus_and_declined_events()
    {
        const string json = """
        [
          { "id": "a1", "summary": "Origination standup", "status": "confirmed", "recurringEventId": "a",
            "start": { "dateTime": "2026-11-17T09:00:00-05:00" }, "end": { "dateTime": "2026-11-17T09:15:00-05:00" },
            "attendees": [ { "email": "me@cfs.canon.com", "self": true, "responseStatus": "accepted" },
                           { "email": "dev@cfs.canon.com" }, { "email": "Room 4", "resource": true } ] },
          { "id": "b", "summary": "Offsite", "start": { "date": "2026-11-18" }, "end": { "date": "2026-11-19" } },
          { "id": "c", "summary": "Focus", "eventType": "focusTime",
            "start": { "dateTime": "2026-11-17T15:00:00Z" }, "end": { "dateTime": "2026-11-17T17:00:00Z" } },
          { "id": "d", "summary": "Skip me", "start": { "dateTime": "2026-11-17T18:00:00Z" }, "end": { "dateTime": "2026-11-17T18:30:00Z" },
            "attendees": [ { "email": "me@cfs.canon.com", "self": true, "responseStatus": "declined" }, { "email": "x@cfs.canon.com" } ] },
          { "id": "e", "status": "cancelled" }
        ]
        """;
        var events = GoogleEventParser.ParseItems(JsonDocument.Parse(json).RootElement, NewYork).ToList();

        Assert.Equal(5, events.Count);
        var standup = events[0];
        Assert.Equal(new DateTime(2026, 11, 17, 14, 0, 0, DateTimeKind.Utc), standup.StartUtc);
        Assert.Equal(2, standup.AttendeeEmails.Count); // the room is not an attendee
        Assert.True(standup.Recurring);

        var offsite = events[1];
        Assert.True(offsite.AllDay);
        Assert.Equal(new DateTime(2026, 11, 18, 5, 0, 0, DateTimeKind.Utc), offsite.StartUtc); // local midnight

        Assert.Equal("focusTime", events[2].EventType);
        Assert.True(events[3].Declined);
        Assert.True(events[4].Cancelled);
    }
}

public class ICalParserTests
{
    private static readonly TimeZoneInfo NewYork = LocalTime.FindTimeZone("America/New_York");
    private static DateTime Utc(int month, int day, int h, int m = 0) => new(2026, month, day, h, m, 0, DateTimeKind.Utc);

    private const string Feed =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
        // Weekly Mon/Wed 09:00 New York, crossing the 1 Nov DST change; one EXDATE; one moved occurrence.
        "BEGIN:VEVENT\r\nUID:standup@google.com\r\nSUMMARY:Team standup\\, daily\r\n" +
        "DTSTART;TZID=America/New_York:20261026T090000\r\nDTEND;TZID=America/New_York:20261026T091500\r\n" +
        "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=6\r\n" +
        "EXDATE;TZID=America/New_York:20261028T090000\r\n" +
        "ATTENDEE;CN=Me:mailto:me@cfs.canon.com\r\nATTENDEE;CN=Dev:mailto:dev@cfs.canon.com\r\n" +
        "ATTENDEE;CUTYPE=RESOURCE;CN=\"Room: 4\":mailto:room4@resource.calendar.google.com\r\n" +
        "BEGIN:VALARM\r\nTRIGGER:-PT10M\r\nACTION:DISPLAY\r\nEND:VALARM\r\n" +
        "END:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:standup@google.com\r\nRECURRENCE-ID;TZID=America/New_York:20261104T090000\r\n" +
        "SUMMARY:Team standup (moved)\r\nDTSTART;TZID=America/New_York:20261104T113000\r\nDTEND;TZID=America/New_York:20261104T114500\r\nEND:VEVENT\r\n" +
        // Single UTC event with a folded summary.
        "BEGIN:VEVENT\r\nUID:one@google.com\r\nSUMMARY:Vendor call with a very long title that is\r\n  folded\r\n" +
        "DTSTART:20261102T190000Z\r\nDURATION:PT45M\r\nEND:VEVENT\r\n" +
        // All-day.
        "BEGIN:VEVENT\r\nUID:allday@google.com\r\nSUMMARY:Holiday\r\nDTSTART;VALUE=DATE:20261126\r\nDTEND;VALUE=DATE:20261127\r\nEND:VEVENT\r\n" +
        // Cancelled.
        "BEGIN:VEVENT\r\nUID:gone@google.com\r\nSTATUS:CANCELLED\r\nSUMMARY:Gone\r\nDTSTART:20261103T150000Z\r\nDTEND:20261103T160000Z\r\nEND:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    [Fact]
    public void Expands_weekly_recurrence_across_dst_with_exdate_and_override()
    {
        var events = ICalParser.Parse(Feed, Utc(10, 1, 0), Utc(12, 1, 0), NewYork);
        var standups = events.Where(e => e.Title.StartsWith("Team standup")).ToList();

        // COUNT=6 → Oct 26, 28(ex), Nov 2, 4(moved), 9, 11 → 5 shown.
        Assert.Equal(5, standups.Count);
        Assert.Equal(Utc(10, 26, 13), standups[0].StartUtc);          // EDT: 09:00 = 13:00Z
        Assert.Equal(Utc(11, 2, 14), standups[1].StartUtc);           // EST: 09:00 = 14:00Z
        Assert.Equal("Team standup (moved)", standups[2].Title);
        Assert.Equal(Utc(11, 4, 16, 30), standups[2].StartUtc);
        Assert.Equal(Utc(11, 11, 14), standups[4].StartUtc);
        Assert.Equal("Team standup, daily", standups[0].Title);       // unescaped
        Assert.Equal(2, standups[0].AttendeeEmails.Count);            // room excluded
        Assert.True(standups[0].Recurring);
        Assert.Equal(TimeSpan.FromMinutes(15), standups[0].EndUtc - standups[0].StartUtc);
        Assert.Equal(standups.Count, standups.Select(s => s.ExternalId).Distinct().Count());
    }

    [Fact]
    public void Reads_utc_duration_folded_all_day_and_skips_cancelled()
    {
        var events = ICalParser.Parse(Feed, Utc(10, 1, 0), Utc(12, 1, 0), NewYork);
        var vendor = Assert.Single(events.Where(e => e.ExternalId == "one@google.com"));
        Assert.Equal("Vendor call with a very long title that is folded", vendor.Title);
        Assert.Equal(Utc(11, 2, 19, 45), vendor.EndUtc);

        var holiday = Assert.Single(events.Where(e => e.Title == "Holiday"));
        Assert.True(holiday.AllDay);
        Assert.Equal(Utc(11, 26, 5), holiday.StartUtc);
        Assert.Equal(Utc(11, 27, 5), holiday.EndUtc);

        Assert.Empty(events.Where(e => e.Title == "Gone"));
    }

    [Fact]
    public void Window_limits_expansion()
    {
        var events = ICalParser.Parse(Feed, Utc(11, 9, 0), Utc(11, 10, 0), NewYork);
        Assert.Equal(Utc(11, 9, 14), Assert.Single(events).StartUtc);
    }

    [Fact]
    public void Expands_monthly_ordinal_weekday_and_daily_until()
    {
        const string ics =
            "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:m\r\nSUMMARY:Month end close\r\n" +
            "DTSTART;TZID=America/New_York:20260930T100000\r\nDTEND;TZID=America/New_York:20260930T110000\r\n" +
            "RRULE:FREQ=MONTHLY;BYDAY=-1WE\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:d\r\nSUMMARY:Daily\r\nDTSTART:20261116T140000Z\r\nDTEND:20261116T141000Z\r\n" +
            "RRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20261120T140000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var events = ICalParser.Parse(ics, Utc(10, 1, 0), Utc(12, 31, 0), NewYork);

        var close = events.Where(e => e.Title == "Month end close").Select(e => e.StartUtc).ToList();
        Assert.Equal(new[] { Utc(10, 28, 14), Utc(11, 25, 15), Utc(12, 30, 15) }, close);

        var daily = events.Where(e => e.Title == "Daily").Select(e => e.StartUtc).ToList();
        Assert.Equal(new[] { Utc(11, 16, 14), Utc(11, 18, 14), Utc(11, 20, 14) }, daily);
    }

    [Fact]
    public void Parses_durations()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), ICalParser.ParseDuration("PT1H30M"));
        Assert.Equal(TimeSpan.FromDays(1), ICalParser.ParseDuration("P1D"));
        Assert.Null(ICalParser.ParseDuration("1H"));
    }
}

public class MetricsTests
{
    private static readonly TimeZoneInfo NewYork = LocalTime.FindTimeZone("America/New_York");
    private static DateTime T(int h, int m = 0, int day = 17) => new(2026, 11, day, h, m, 0, DateTimeKind.Utc);
    private static MetricEntry E(int task, DateTime s, DateTime? e, TimeEntryKind kind = TimeEntryKind.Task) => new(s, e, kind, task, 1);

    [Fact]
    public void Focus_block_needs_25_continuous_minutes_on_one_task()
    {
        var entries = new[]
        {
            E(1, T(14), T(14, 24)),           // 24 min: too short
            E(2, T(14, 30), T(14, 45)),       // joined with the next one (same task, 30 s apart)…
            E(2, T(14, 45), T(15, 5)),        // … = 35 min block
            E(3, T(15, 10), T(16, 10)),       // 60 min, but an interruption at 15:30 splits it 20 + 40
            E(4, T(16, 15), T(16, 45), TimeEntryKind.Reactive), // reactive never counts
        };
        var blocks = Metrics.FocusBlocks(entries, new[] { T(15, 30) }, T(23));

        Assert.Equal(2, blocks.Count);
        Assert.Equal(2, blocks[0].TaskId);
        Assert.Equal(35, blocks[0].Minutes);
        Assert.Equal(3, blocks[1].TaskId);
        Assert.Equal(40, blocks[1].Minutes);
    }

    [Fact]
    public void Running_timer_counts_toward_a_focus_block()
    {
        var blocks = Metrics.FocusBlocks(new[] { E(1, T(14), null) }, Array.Empty<DateTime>(), T(14, 30));
        Assert.Equal(30, Assert.Single(blocks).Minutes);
    }

    [Fact]
    public void Task_switches_ignore_resuming_the_same_task()
    {
        var entries = new[]
        {
            E(1, T(14), T(14, 30)),
            E(1, T(14, 40), T(15)),      // resumed after an interruption: not a switch
            E(2, T(15), T(15, 20)),      // switch 1
            E(1, T(15, 20), T(15, 50)),  // switch 2
            new MetricEntry(T(16), T(16, 30), TimeEntryKind.Task, null, 5),       // switch 3 (project only)
            new MetricEntry(T(16, 30), T(17), TimeEntryKind.Untracked, null, null), // switch 4
        };
        Assert.Equal(4, Metrics.TaskSwitches(entries));
        Assert.Equal(0, Metrics.TaskSwitches(Array.Empty<MetricEntry>()));
    }

    [Fact]
    public void Heatmap_buckets_by_local_weekday_and_hour_across_dst()
    {
        var starts = new[]
        {
            new DateTime(2026, 10, 30, 13, 30, 0, DateTimeKind.Utc), // Fri 09:30 EDT
            new DateTime(2026, 11, 6, 14, 30, 0, DateTimeKind.Utc),  // Fri 09:30 EST — same bucket
            new DateTime(2026, 11, 17, 4, 30, 0, DateTimeKind.Utc),  // Mon 23:30 local (Tue in UTC)
        };
        var grid = Metrics.Heatmap(starts, NewYork);
        Assert.Equal(2, grid[4][9]);  // Friday (Mon=0) 9 o'clock
        Assert.Equal(1, grid[0][23]); // Monday 23 o'clock
        Assert.Equal(3, grid.Sum(r => r.Sum()));
    }

    [Fact]
    public void Flags_task_timers_that_overlap_counted_meetings()
    {
        var entries = new[]
        {
            E(1, T(14), T(15)),                                     // overlaps the 14:30 meeting by 30 min
            E(2, T(15), T(15, 30)),                                 // touches but doesn't overlap
            new MetricEntry(T(14, 30), T(15), TimeEntryKind.Untracked, null, null), // untracked: ignored
            E(3, T(16), null),                                      // running into the 16:15 meeting
        };
        var meetings = new[] { new Interval(T(14, 30), T(15)), new Interval(T(16, 15), T(16, 45)) };
        var overlaps = Metrics.MeetingOverlaps(entries, meetings, T(16, 25));

        Assert.Equal(2, overlaps.Count);
        Assert.Equal((0, 0, 30.0), (overlaps[0].EntryIndex, overlaps[0].MeetingIndex, overlaps[0].Minutes));
        Assert.Equal((3, 1, 10.0), (overlaps[1].EntryIndex, overlaps[1].MeetingIndex, overlaps[1].Minutes));
    }

    [Fact]
    public void Day_metrics_treat_counted_meetings_as_tracked_and_split_planned_time()
    {
        var date = new DateOnly(2026, 11, 17);
        var day = LocalTime.DayBoundsUtc(date, NewYork);
        var work = LocalTime.WorkHoursUtc(date, new TimeOnly(8, 45), new TimeOnly(17, 30), NewYork);
        var entries = new[]
        {
            new SummaryEntry(T(13, 45), T(14, 30), TimeEntryKind.Task, 1, "Origination", null, 10), // planned
            new SummaryEntry(T(15), T(16), TimeEntryKind.Task, 2, "Admin", null, 11),               // unplanned
        };
        var meetings = new[]
        {
            new SummaryMeeting(T(14, 30), T(15), 1, "Origination", null),
            new SummaryMeeting(T(14, 45), T(15, 15), null, null, null), // overlaps the first: counted once
        };
        var interruptions = new[] { new MetricInterruption(T(15, 20), T(15, 25), InterruptionSource.Email, null, null, true) };

        var m = DayMetrics.Compute(date, day, work, T(16), entries, interruptions, meetings,
            new[] { new Interval(T(13, 45), T(14, 45)) }, new HashSet<int> { 10, 12 }, TimeSpan.FromMinutes(2));

        Assert.Equal(45, m.Summary.MeetingMinutes);   // 14:30-15:15
        Assert.Equal(2, m.Summary.MeetingCount);
        Assert.Equal(0, m.Summary.UnloggedMinutes);  // 13:45-16:00 fully covered
        Assert.Equal(75, m.Summary.ByProject.Single(p => p.ProjectId == 1).Minutes); // 45 task + 30 meeting
        Assert.Equal(45, m.PlannedTaskMinutes);
        Assert.Equal(60, m.UnplannedTaskMinutes);
        Assert.Equal(2, m.PlannedTasks);
        Assert.Equal(1, m.PlannedTasksWorked);
        Assert.Equal(1, m.CouldWaitCount);
        Assert.Equal(1, m.TaskSwitches);
        Assert.Equal(60, m.PlannedFocusMinutes);
        Assert.Equal(2, m.FocusBlocks.Count); // 45 min; 60 min split at 15:20 → 20 + 40 → only the 40 counts
        Assert.Equal(45, m.LongestFocusBlockMinutes);
    }

    [Fact]
    public void Future_meetings_do_not_count_yet()
    {
        var date = new DateOnly(2026, 11, 17);
        var day = LocalTime.DayBoundsUtc(date, NewYork);
        var work = LocalTime.WorkHoursUtc(date, new TimeOnly(8, 45), new TimeOnly(17, 30), NewYork);
        var s = DailySummary.Build(day, work, T(15), Array.Empty<SummaryEntry>(), Array.Empty<(DateTime, DateTime?)>(), TimeSpan.FromMinutes(2),
            new[] { new SummaryMeeting(T(14, 30), T(15, 30), null, null, null) });
        Assert.Equal(30, s.MeetingMinutes);
    }
}

public class CsvTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("=SUM(A1:A2)", "'=SUM(A1:A2)")]
    [InlineData("+1 555", "'+1 555")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@user", "'@user")]
    [InlineData("=1,2", "\"'=1,2\"")]
    [InlineData("", "")]
    public void Escapes_text_fields(string input, string expected)
    {
        Assert.Equal(expected, Csv.Field(input));
    }

    [Fact]
    public void Formats_numbers_dates_and_rows_invariantly()
    {
        var csv = new Csv()
            .Row("date", "minutes", "negative", "utc", "flag", "missing")
            .Row(new DateOnly(2026, 11, 17), 12.5, -3.25, new DateTime(2026, 11, 17, 14, 0, 0, DateTimeKind.Utc), true, null)
            .ToString();
        Assert.Equal("date,minutes,negative,utc,flag,missing\r\n2026-11-17,12.5,-3.25,2026-11-17T14:00:00Z,true,\r\n", csv);
    }
}
