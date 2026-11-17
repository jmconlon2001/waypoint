using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>
/// Metrics over days and weeks (FR-D1..D7). Everything is computed from the raw entries, interruptions and
/// calendar events when asked, so editing a past entry changes the reports straight away (no stale aggregates).
/// </summary>
public sealed class ReportService(WaypointDbContext db, SettingsService settings, TimeProvider clock)
{
    public const int MaxRangeDays = 366;
    public const int TrendWeeks = 4;

    /// <summary>Everything logged in a range of local days, loaded once.</summary>
    internal sealed record RangeData(
        Setting Setting,
        TimeZoneInfo Tz,
        DateTime Now,
        DateOnly From,
        DateOnly To,
        List<TimeEntry> Entries,
        List<Interruption> Interruptions,
        List<CalendarEvent> Events);

    internal async Task<RangeData> LoadAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var setting = await settings.GetAsync(ct);
        var tz = LocalTime.FindTimeZone(setting.TimeZone);
        var start = LocalTime.DayBoundsUtc(from, tz).Start;
        var end = LocalTime.DayBoundsUtc(to, tz).End;

        var entries = await db.TimeEntries.Include(e => e.Task).Include(e => e.Project)
            .Where(e => e.StartedAt < end && (e.EndedAt == null || e.EndedAt > start))
            .OrderBy(e => e.StartedAt)
            .ToListAsync(ct);
        var interruptions = await db.Interruptions
            .Include(i => i.Person)
            .Include(i => i.PausedTimeEntry).ThenInclude(e => e!.Task)
            .Where(i => i.StartedAt < end && (i.EndedAt == null || i.EndedAt > start))
            .OrderBy(i => i.StartedAt)
            .ToListAsync(ct);
        var events = await db.CalendarEvents.Include(e => e.Project)
            .Where(e => e.StartedAt < end && e.EndedAt > start)
            .OrderBy(e => e.StartedAt)
            .ToListAsync(ct);
        return new RangeData(setting, tz, Now(), from, to, entries, interruptions, events);
    }

    internal static DailySummary Summarize(DateOnly date, RangeData d)
    {
        var day = LocalTime.DayBoundsUtc(date, d.Tz);
        var work = LocalTime.WorkHoursUtc(date, d.Setting.WorkdayStart, d.Setting.WorkdayEnd, d.Tz);
        return DailySummary.Build(
            day, work, d.Now,
            d.Entries.Where(e => Overlaps(e.StartedAt, e.EndedAt, day, d.Now))
                .Select(e => new SummaryEntry(e.StartedAt, e.EndedAt, e.Kind, e.ProjectId, e.Project?.Name, e.Project?.Color, e.TaskId, e.Id)),
            d.Interruptions.Where(i => Overlaps(i.StartedAt, i.EndedAt, day, d.Now)).Select(i => (i.StartedAt, i.EndedAt)),
            ReviewService.MinGap,
            MeetingsFor(day, d));
    }

    internal static IEnumerable<SummaryMeeting> MeetingsFor(Interval day, RangeData d) =>
        d.Events
            .Where(e => e.StartedAt < day.End && e.EndedAt > day.Start)
            .Where(e => Meetings.CountsAsMeeting(e, d.Setting.MeetingMaxMinutes) || Meetings.CountsAsPlannedFocus(e))
            .Select(e => new SummaryMeeting(e.StartedAt, e.EndedAt, e.ProjectId, e.Project?.Name, e.Project?.Color,
                e.MeetingType == MeetingType.Focus, e.Id));

    internal static DailyTotalsDto ToTotalsDto(DateOnly date, DailySummary s) => new(
        date,
        s.TaskMinutes, s.ReactiveMinutes, s.UntrackedMinutes, s.TrackedMinutes,
        s.InterruptionCount, s.InterruptionMinutes, s.UnloggedMinutes,
        s.ByProject.Select(p => new ProjectTotalDto(p.ProjectId, p.Name, p.Color, p.Minutes)).ToList(),
        s.MeetingMinutes, s.MeetingCount, s.PlannedFocusMinutes,
        s.FocusBlocks.Count, Round(s.FocusBlockMinutes), Round(s.LongestFocusBlockMinutes), s.TaskSwitches);

    public async Task<DailyTotalsDto> GetDailyAsync(DateOnly? date, CancellationToken ct = default)
    {
        var day = date ?? await TodayAsync(ct);
        var data = await LoadAsync(day, day, ct);
        return ToTotalsDto(day, Summarize(day, data));
    }

    // ---- Weekly summary (FR-D5) ----

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public async Task<WeeklySummaryDto> GetWeekAsync(DateOnly? anyDay, CancellationToken ct = default)
    {
        var weekStart = WeekStart(anyDay ?? await TodayAsync(ct));
        var weekEnd = weekStart.AddDays(6);
        var trendStart = weekStart.AddDays(-7 * TrendWeeks);
        var data = await LoadAsync(trendStart, weekEnd, ct);

        var summaries = new Dictionary<DateOnly, DailySummary>();
        for (var d = trendStart; d <= weekEnd; d = d.AddDays(1)) summaries[d] = Summarize(d, data);

        var weekDays = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var days = weekDays.Select(d => ToBreakdown(d, summaries[d])).ToList();
        var totals = Total(days);

        // Trend: average over the previous weeks that have anything logged, so a new install isn't compared with zeros.
        var previous = Enumerable.Range(1, TrendWeeks)
            .Select(w => Total(Enumerable.Range(0, 7).Select(i => weekStart.AddDays(-7 * w + i)).Select(d => ToBreakdown(d, summaries[d])).ToList()))
            .Where(HasActivity)
            .ToList();
        var trend = Average(previous);

        var byProject = weekDays.SelectMany(d => summaries[d].ByProject)
            .GroupBy(p => (p.ProjectId, p.Name))
            .Select(g => new ProjectTotalDto(g.Key.ProjectId, g.Key.Name, g.First().Color, Round(g.Sum(p => p.Minutes))))
            .OrderByDescending(p => p.Minutes)
            .ToList();

        var weekBounds = new Interval(LocalTime.DayBoundsUtc(weekStart, data.Tz).Start, LocalTime.DayBoundsUtc(weekEnd, data.Tz).End);
        var meetingTypes = MeetingTypeBreakdown(data, weekBounds);

        LongestBlockDto? longest = null;
        foreach (var d in weekDays)
            foreach (var b in summaries[d].FocusBlocks)
                if (longest is null || b.Interval.Minutes > longest.Minutes)
                    longest = new LongestBlockDto(d, b.Interval.Start, b.Interval.End, Round(b.Interval.Minutes),
                        data.Entries.FirstOrDefault(e => e.TaskId == b.TaskId)?.Task?.Title);

        var plans = new List<PlannedVsActualDto>();
        foreach (var d in weekDays) plans.Add(await BuildPlanAsync(d, data, ct));

        return new WeeklySummaryDto(
            weekStart, weekEnd, data.Tz.Id, days, totals, trend, previous.Count, byProject, meetingTypes, longest,
            InterruptionStats(weekStart, weekEnd, data),
            plans.Sum(p => p.PlannedCount), plans.Sum(p => p.PlannedDoneCount),
            Round(plans.Sum(p => p.PlannedMinutes)), Round(plans.Sum(p => p.UnplannedTaskMinutes)));
    }

    private static DayBreakdownDto ToBreakdown(DateOnly date, DailySummary s)
    {
        // A day with nothing logged at all (a day off, or before you started using Waypoint) isn't a tracked work
        // day, so its gaps aren't counted as missing time. The daily review still shows them.
        var idle = s.TrackedMinutes == 0 && s.MeetingMinutes == 0 && s.InterruptionCount == 0;
        return new DayBreakdownDto(
            date, s.TaskMinutes, s.ReactiveMinutes, s.UntrackedMinutes, s.MeetingMinutes,
            idle ? 0 : s.UnloggedMinutes, s.PlannedFocusMinutes,
            s.InterruptionCount, s.InterruptionMinutes, s.TaskSwitches, s.FocusBlocks.Count, Round(s.LongestFocusBlockMinutes));
    }

    private static PeriodTotalsDto Total(IReadOnlyCollection<DayBreakdownDto> days) => new(
        Round(days.Sum(d => d.FocusMinutes)), Round(days.Sum(d => d.ReactiveMinutes)), Round(days.Sum(d => d.UntrackedMinutes)),
        Round(days.Sum(d => d.MeetingMinutes)), Round(days.Sum(d => d.UnloggedMinutes)), Round(days.Sum(d => d.PlannedFocusMinutes)),
        days.Sum(d => d.InterruptionCount), Round(days.Sum(d => d.InterruptionMinutes)), days.Sum(d => d.TaskSwitches),
        days.Sum(d => d.FocusBlockCount));

    private static bool HasActivity(PeriodTotalsDto t) =>
        t.FocusMinutes + t.ReactiveMinutes + t.UntrackedMinutes + t.MeetingMinutes > 0 || t.InterruptionCount > 0;

    private static PeriodTotalsDto Average(IReadOnlyCollection<PeriodTotalsDto> weeks)
    {
        if (weeks.Count == 0) return new PeriodTotalsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        double Avg(Func<PeriodTotalsDto, double> f) => Round(weeks.Average(f));
        int AvgInt(Func<PeriodTotalsDto, int> f) => (int)Math.Round(weeks.Average(f));
        return new PeriodTotalsDto(
            Avg(w => w.FocusMinutes), Avg(w => w.ReactiveMinutes), Avg(w => w.UntrackedMinutes), Avg(w => w.MeetingMinutes),
            Avg(w => w.UnloggedMinutes), Avg(w => w.PlannedFocusMinutes), AvgInt(w => w.InterruptionCount), Avg(w => w.InterruptionMinutes),
            AvgInt(w => w.TaskSwitches), AvgInt(w => w.FocusBlockCount));
    }

    private static List<CountMinutesDto> MeetingTypeBreakdown(RangeData data, Interval range) =>
        data.Events
            .Where(e => e.StartedAt < range.End && e.EndedAt > range.Start && Meetings.CountsAsMeeting(e, data.Setting.MeetingMaxMinutes))
            .Where(e => e.StartedAt < data.Now)
            .GroupBy(e => e.MeetingType)
            .Select(g => new CountMinutesDto(g.Key.ToString(), MeetingTypeLabel(g.Key), g.Count(),
                Round(g.Sum(e => Intervals.Clip(e.StartedAt, e.EndedAt, range.Start, range.End < data.Now ? range.End : data.Now, data.Now)?.Minutes ?? 0))))
            .OrderByDescending(x => x.Minutes)
            .ToList();

    public static string MeetingTypeLabel(MeetingType t) => t switch
    {
        MeetingType.OneOnOne => "1:1",
        MeetingType.Team => "Team",
        MeetingType.External => "External",
        MeetingType.Recurring => "Recurring",
        MeetingType.Focus => "Focus time",
        _ => "Ad hoc",
    };

    // ---- Interruptions (FR-D3) ----

    public async Task<InterruptionStatsDto> GetInterruptionStatsAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var (f, t) = await RangeAsync(from, to, ct);
        return InterruptionStats(f, t, await LoadAsync(f, t, ct));
    }

    internal static InterruptionStatsDto InterruptionStats(DateOnly from, DateOnly to, RangeData d)
    {
        var start = LocalTime.DayBoundsUtc(from, d.Tz).Start;
        var end = LocalTime.DayBoundsUtc(to, d.Tz).End;
        var items = d.Interruptions.Where(i => i.StartedAt >= start && i.StartedAt < end).ToList();
        double Minutes(Interruption i) => Mapping.Minutes((i.EndedAt ?? d.Now) - i.StartedAt);

        var byHour = new int[24];
        var heat = Enumerable.Range(0, 7).Select(_ => new int[24]).ToArray();
        foreach (var i in items)
        {
            var local = LocalTime.ToLocal(i.StartedAt, d.Tz);
            byHour[local.Hour]++;
            heat[((int)local.DayOfWeek + 6) % 7][local.Hour]++;
        }

        var bySource = items.GroupBy(i => i.SourceType)
            .Select(g => new CountMinutesDto(g.Key?.ToString() ?? "Unclassified", SourceLabel(g.Key), g.Count(), Round(g.Sum(Minutes))))
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.Minutes).ToList();
        var byPerson = items.Where(i => i.Person is not null).GroupBy(i => i.Person!.Name)
            .Select(g => new CountMinutesDto(g.Key, g.Key, g.Count(), Round(g.Sum(Minutes))))
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.Minutes).ToList();
        var byDay = new List<CountMinutesDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var group = items.Where(i => LocalTime.LocalDate(i.StartedAt, d.Tz) == day).ToList();
            byDay.Add(new CountMinutesDto(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), day.ToString("ddd", CultureInfo.InvariantCulture),
                group.Count, Round(group.Sum(Minutes))));
        }

        return new InterruptionStatsDto(
            from, to, items.Count, Round(items.Sum(Minutes)),
            items.Count(i => i.CouldWait == true), items.Count(i => i.CouldWait == false),
            bySource, byPerson, byHour, heat, byDay);
    }

    public static string SourceLabel(InterruptionSource? s) => s switch
    {
        InterruptionSource.WalkUp => "Walk-up",
        InterruptionSource.GoogleChat => "Google Chat",
        InterruptionSource.Email => "Email",
        InterruptionSource.Phone => "Phone",
        InterruptionSource.MeetingOverrun => "Meeting overrun",
        InterruptionSource.SelfDistraction => "Self-distraction",
        _ => "Unclassified",
    };

    // ---- Planned vs. actual (FR-D6) ----

    public async Task<PlannedVsActualDto> GetPlanAsync(DateOnly? date, CancellationToken ct = default)
    {
        var day = date ?? await TodayAsync(ct);
        return await BuildPlanAsync(day, await LoadAsync(day, day, ct), ct);
    }

    internal async Task<PlannedVsActualDto> BuildPlanAsync(DateOnly date, RangeData d, CancellationToken ct)
    {
        var day = LocalTime.DayBoundsUtc(date, d.Tz);
        var plannedTasks = await db.TaskPlans.Include(p => p.Task).ThenInclude(t => t!.Project)
            .Where(p => p.Date == date)
            .Select(p => p.Task!)
            .ToListAsync(ct);
        var plannedIds = plannedTasks.Select(t => t.Id).ToHashSet();

        // Same rule as the totals: time inside a counted meeting belongs to the meeting.
        var meetingWindowEnd = day.End < d.Now ? day.End : d.Now;
        var meetingUnion = Intervals.Merge(MeetingsFor(day, d).Where(m => !m.IsFocusTime)
            .Select(m => Intervals.Clip(m.StartedAt, m.EndedAt, day.Start, meetingWindowEnd, d.Now))
            .OfType<Interval>());
        var minutes = new List<(TimeEntry Entry, double Minutes)>();
        foreach (var e in d.Entries)
            if (Intervals.Clip(e.StartedAt, e.EndedAt, day.Start, day.End, d.Now) is { } c)
                minutes.Add((e, c.Minutes - Intervals.OverlapMinutes(c, meetingUnion)));

        var planned = plannedTasks
            .Select(t => new PlannedTaskDto(
                t.Id, t.Title, t.Project?.Name, t.Project?.Color, t.Status,
                t.Status == WorkTaskStatus.Done && t.CompletedAt < day.End,
                Round(minutes.Where(m => m.Entry.TaskId == t.Id && m.Entry.Kind != TimeEntryKind.Untracked).Sum(m => m.Minutes))))
            .OrderBy(p => p.Done).ThenByDescending(p => p.Minutes)
            .ToList();

        var unplanned = minutes
            .Where(m => m.Entry.Kind == TimeEntryKind.Task && (m.Entry.TaskId is null || !plannedIds.Contains(m.Entry.TaskId.Value)))
            .GroupBy(m => (m.Entry.TaskId, m.Entry.ProjectId))
            .Select(g =>
            {
                var e = g.First().Entry;
                var title = e.Task?.Title ?? (e.Project is null ? "No project" : $"{e.Project.Name} (no task)");
                return new UnplannedTaskDto(e.TaskId, title, e.Project?.Color, Round(g.Sum(m => m.Minutes)));
            })
            .Where(u => u.Minutes > 0)
            .OrderByDescending(u => u.Minutes)
            .ToList();

        return new PlannedVsActualDto(
            date, planned, planned.Count, planned.Count(p => p.Done), Round(planned.Sum(p => p.Minutes)),
            Round(unplanned.Sum(u => u.Minutes)),
            Round(minutes.Where(m => m.Entry.Kind == TimeEntryKind.Reactive).Sum(m => m.Minutes)),
            Round(meetingUnion.Sum(i => i.Minutes)),
            unplanned);
    }

    // ---- CSV export (FR-D7) ----

    public static readonly IReadOnlyList<string> ExportKinds = ["time-entries", "interruptions", "meetings", "daily"];

    public async Task<(byte[] Content, string FileName)> ExportCsvAsync(string kind, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        if (!ExportKinds.Contains(kind)) throw ApiException.NotFound($"Export '{kind}'");
        var (f, t) = await RangeAsync(from, to, ct);
        var d = await LoadAsync(f, t, ct);
        var start = LocalTime.DayBoundsUtc(f, d.Tz).Start;
        var end = LocalTime.DayBoundsUtc(t, d.Tz).End;
        var csv = new Csv();

        string Date(DateTime utc) => LocalTime.LocalDate(utc, d.Tz).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string Time(DateTime? utc) => utc is null ? "" : LocalTime.ToLocal(utc.Value, d.Tz).ToString("HH:mm", CultureInfo.InvariantCulture);
        string Utc(DateTime? utc) => utc?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) ?? "";
        string Num(double v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

        switch (kind)
        {
            case "time-entries":
                csv.Row("id", "date", "start", "end", "minutes", "kind", "project", "task", "note", "started_utc", "ended_utc");
                foreach (var e in d.Entries.Where(e => e.StartedAt >= start && e.StartedAt < end))
                    csv.Row(e.Id.ToString(CultureInfo.InvariantCulture), Date(e.StartedAt), Time(e.StartedAt), Time(e.EndedAt),
                        Num(((e.EndedAt ?? d.Now) - e.StartedAt).TotalMinutes), e.Kind.ToString(), e.Project?.Name, e.Task?.Title, e.Note,
                        Utc(e.StartedAt), Utc(e.EndedAt));
                break;
            case "interruptions":
                csv.Row("id", "date", "start", "end", "minutes", "source", "person", "could_wait", "note", "paused_task", "converted_to_task", "started_utc", "ended_utc");
                foreach (var i in d.Interruptions.Where(i => i.StartedAt >= start && i.StartedAt < end))
                    csv.Row(i.Id.ToString(CultureInfo.InvariantCulture), Date(i.StartedAt), Time(i.StartedAt), Time(i.EndedAt),
                        Num(((i.EndedAt ?? d.Now) - i.StartedAt).TotalMinutes), SourceLabel(i.SourceType), i.Person?.Name,
                        i.CouldWait switch { true => "yes", false => "no", _ => "" }, i.Note, i.PausedTimeEntry?.Task?.Title,
                        i.ConvertedTaskId is null ? "" : "yes", Utc(i.StartedAt), Utc(i.EndedAt));
                break;
            case "meetings":
                csv.Row("date", "start", "end", "minutes", "title", "type", "attendees", "recurring", "project", "counts_as_meeting", "excluded_reason", "started_utc", "ended_utc");
                foreach (var e in d.Events.Where(e => e.StartedAt >= start && e.StartedAt < end))
                {
                    var dto = CalendarService.ToDto(e, d.Setting.MeetingMaxMinutes);
                    csv.Row(Date(e.StartedAt), e.IsAllDay ? "" : Time(e.StartedAt), e.IsAllDay ? "" : Time(e.EndedAt), Num(dto.Minutes), e.Title,
                        MeetingTypeLabel(e.MeetingType), e.AttendeeCount.ToString(CultureInfo.InvariantCulture), e.IsRecurring ? "yes" : "no",
                        e.Project?.Name, dto.CountsAsMeeting ? "yes" : "no", dto.ExcludedReason, Utc(e.StartedAt), Utc(e.EndedAt));
                }
                break;
            default:
                csv.Row("date", "focus_min", "reactive_min", "untracked_min", "meeting_min", "unlogged_min", "planned_focus_min",
                    "interruptions", "interruption_min", "task_switches", "focus_blocks", "longest_focus_block_min");
                for (var day = f; day <= t; day = day.AddDays(1))
                {
                    var b = ToBreakdown(day, Summarize(day, d));
                    csv.Row(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Num(b.FocusMinutes), Num(b.ReactiveMinutes),
                        Num(b.UntrackedMinutes), Num(b.MeetingMinutes), Num(b.UnloggedMinutes), Num(b.PlannedFocusMinutes),
                        b.InterruptionCount.ToString(CultureInfo.InvariantCulture), Num(b.InterruptionMinutes),
                        b.TaskSwitches.ToString(CultureInfo.InvariantCulture), b.FocusBlockCount.ToString(CultureInfo.InvariantCulture),
                        Num(b.LongestFocusBlockMinutes));
                }
                break;
        }

        return (csv.ToBytes(), $"waypoint-{kind}-{f:yyyyMMdd}-{t:yyyyMMdd}.csv");
    }

    /// <summary>Defaults to the last 30 days up to today.</summary>
    private async Task<(DateOnly From, DateOnly To)> RangeAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var t = to ?? await TodayAsync(ct);
        var f = from ?? t.AddDays(-29);
        if (f > t) throw ApiException.BadRequest("'From' must be on or before 'to'.");
        if (t.DayNumber - f.DayNumber + 1 > MaxRangeDays) throw ApiException.BadRequest($"Pick at most {MaxRangeDays} days.");
        return (f, t);
    }

    private static bool Overlaps(DateTime start, DateTime? end, Interval day, DateTime now) =>
        start < day.End && (end ?? now) > day.Start;

    private async Task<DateOnly> TodayAsync(CancellationToken ct) => LocalTime.LocalDate(Now(), await settings.TimeZoneAsync(ct));

    private DateTime Now() => Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);

    private static double Round(double v) => Math.Round(v, 2);

    /// <summary>RFC 4180 CSV, UTF-8 with BOM so Excel reads names correctly. Cells that look like formulas are defused.</summary>
    private sealed class Csv
    {
        private readonly StringBuilder sb = new();

        public void Row(params string?[] cells)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(cells[i]));
            }
            sb.Append("\r\n");
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                value = "'" + value;
            return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public byte[] ToBytes() => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }
}
