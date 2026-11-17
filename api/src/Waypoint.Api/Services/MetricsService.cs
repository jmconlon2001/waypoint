using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>Raw data for a range of local days, loaded once.</summary>
public sealed record RangeData(
    Setting Settings,
    TimeZoneInfo Tz,
    DateTime Now,
    DateOnly From,
    DateOnly To,
    List<TimeEntry> Entries,
    List<Interruption> Interruptions,
    List<CalendarEvent> Events,
    List<TaskPlan> Plans);

/// <summary>
/// Daily metrics (FR-D2…D6), the DailyAggregate table (nightly + on-demand recompute), weekly and
/// interruption reports.
/// </summary>
public sealed class MetricsService(WaypointDbContext db, SettingsService settings, TimeProvider clock)
{
    public static readonly TimeSpan MinGap = ReviewService.MinGap;

    public async Task<RangeData> LoadAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var tz = LocalTime.FindTimeZone(s.TimeZone);
        var now = Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);
        var start = LocalTime.ToUtc(from, TimeOnly.MinValue, tz);
        var end = LocalTime.ToUtc(to.AddDays(1), TimeOnly.MinValue, tz);

        var entries = await db.TimeEntries.Include(e => e.Task).Include(e => e.Project)
            .Where(e => e.StartedAt < end && (e.EndedAt == null || e.EndedAt > start))
            .OrderBy(e => e.StartedAt).ToListAsync(ct);
        var interruptions = await db.Interruptions
            .Include(i => i.Person)
            .Include(i => i.PausedTimeEntry).ThenInclude(e => e!.Task)
            .Where(i => i.StartedAt < end && (i.EndedAt == null || i.EndedAt > start))
            .OrderBy(i => i.StartedAt).ToListAsync(ct);
        var events = await db.CalendarEvents.Include(e => e.Project)
            .Where(e => e.Source == SourceName(s.CalendarSource) && e.StartedAt < end && e.EndedAt > start)
            .OrderBy(e => e.StartedAt).ToListAsync(ct);
        var plans = await db.TaskPlans.Where(p => p.Date >= from && p.Date <= to).ToListAsync(ct);
        return new RangeData(s, tz, now, from, to, entries, interruptions, events, plans);
    }

    /// <summary>"google"/"ical" for the selected source; with no source no events are used.</summary>
    public static string SourceName(CalendarSourceKind kind) => kind switch
    {
        CalendarSourceKind.Google => "google",
        CalendarSourceKind.ICal => "ical",
        _ => "-none-",
    };

    public DayMetrics Compute(RangeData data, DateOnly date)
    {
        var s = data.Settings;
        var day = LocalTime.DayBoundsUtc(date, data.Tz);
        var work = LocalTime.WorkHoursUtc(date, s.WorkdayStart, s.WorkdayEnd, data.Tz);

        var entries = data.Entries
            .Where(e => e.StartedAt < day.End && (e.EndedAt ?? data.Now) > day.Start)
            .Select(e => new SummaryEntry(e.StartedAt, e.EndedAt, e.Kind, e.ProjectId, e.Project?.Name, e.Project?.Color, e.TaskId))
            .ToList();
        var interruptions = data.Interruptions
            .Where(i => i.StartedAt < day.End && (i.EndedAt ?? data.Now) > day.Start)
            .Select(i => new MetricInterruption(i.StartedAt, i.EndedAt, i.SourceType, i.PersonId, i.Person?.Name, i.CouldWait))
            .ToList();
        var events = data.Events.Where(e => e.StartedAt < day.End && e.EndedAt > day.Start).ToList();
        var meetings = events
            .Where(e => CalendarRules.IsCounted(e.AllDay, e.StartedAt, e.EndedAt, e.EventType, e.Declined, s.MaxMeetingMinutes))
            .Select(e => new SummaryMeeting(e.StartedAt, e.EndedAt, e.ProjectId, e.Project?.Name, e.Project?.Color))
            .ToList();
        var focus = events.Where(e => CalendarRules.IsPlannedFocus(e.AllDay, e.EventType)).Select(e => new Interval(e.StartedAt, e.EndedAt)).ToList();
        var planned = data.Plans.Where(p => p.Date == date).Select(p => p.TaskId).ToHashSet();

        return DayMetrics.Compute(date, day, work, data.Now, entries, interruptions, meetings, focus, planned, MinGap);
    }

    /// <summary>Recomputes and stores aggregates for every day in the range (up to today).</summary>
    public async Task<RecomputeResult> RecomputeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) throw ApiException.BadRequest("The end date must be on or after the start date.");
        if (to.DayNumber - from.DayNumber > 800) throw ApiException.BadRequest("Recompute at most about two years at a time.");
        var data = await LoadAsync(from, to, ct);
        var today = LocalTime.LocalDate(data.Now, data.Tz);
        var last = to > today ? today : to;
        var existing = await db.DailyAggregates.Where(a => a.Date >= from && a.Date <= last).ToDictionaryAsync(a => a.Date, ct);
        var days = 0;
        for (var d = from; d <= last; d = d.AddDays(1))
        {
            var fresh = Compute(data, d).ToAggregate(data.Now);
            if (existing.TryGetValue(d, out var row)) db.Entry(row).CurrentValues.SetValues(fresh);
            else db.DailyAggregates.Add(fresh);
            days++;
        }
        await db.SaveChangesAsync(ct);
        return new RecomputeResult(from, last, days);
    }

    /// <summary>
    /// Aggregates for the range: stored rows for past days (missing ones are computed and stored),
    /// and today computed live (not stored until the nightly job).
    /// </summary>
    public async Task<List<DailyAggregateDto>> GetAggregatesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) throw ApiException.BadRequest("The end date must be on or after the start date.");
        var s = await settings.GetAsync(ct);
        var tz = LocalTime.FindTimeZone(s.TimeZone);
        var today = LocalTime.LocalDate(clock.GetUtcNow().UtcDateTime, tz);
        var stored = await db.DailyAggregates.Where(a => a.Date >= from && a.Date <= to).ToDictionaryAsync(a => a.Date, ct);

        var missingPast = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays)
            .Where(d => d < today && !stored.ContainsKey(d)).ToList();
        if (missingPast.Count > 0)
        {
            await RecomputeAsync(missingPast.Min(), missingPast.Max(), ct);
            stored = await db.DailyAggregates.Where(a => a.Date >= from && a.Date <= to).ToDictionaryAsync(a => a.Date, ct);
        }

        var result = stored.Values.Where(a => a.Date < today).Select(ToDto).ToList();
        if (today >= from && today <= to)
        {
            var data = await LoadAsync(today, today, ct);
            result.Add(ToDto(Compute(data, today).ToAggregate(data.Now)));
        }
        return result.OrderBy(a => a.Date).ToList();
    }

    /// <summary>FR-D5 weekly summary; <paramref name="weekStart"/> is moved back to its Monday.</summary>
    public async Task<WeeklyReportDto> WeeklyAsync(DateOnly? weekStart, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var tz = LocalTime.FindTimeZone(s.TimeZone);
        var monday = Monday(weekStart ?? LocalTime.LocalDate(clock.GetUtcNow().UtcDateTime, tz));
        var sunday = monday.AddDays(6);

        var data = await LoadAsync(monday, sunday, ct);
        var today = LocalTime.LocalDate(data.Now, data.Tz);
        var days = Enumerable.Range(0, 7).Select(monday.AddDays).Where(d => d <= today).Select(d => Compute(data, d)).ToList();
        var dayDtos = days.Select(m => ToDto(m.ToAggregate(data.Now))).ToList();

        var byProject = days.SelectMany(d => d.Summary.ByProject)
            .GroupBy(p => (p.ProjectId, p.Name, p.Color))
            .Select(g => new ProjectTotalDto(g.Key.ProjectId, g.Key.Name, g.Key.Color, Math.Round(g.Sum(p => p.Minutes), 2)))
            .OrderByDescending(p => p.Minutes).ToList();

        var kinds = new KindTotalsDto(
            Sum(dayDtos, d => d.MeetingMinutes), Sum(dayDtos, d => d.TaskMinutes), Sum(dayDtos, d => d.ReactiveMinutes),
            Sum(dayDtos, d => d.UntrackedMinutes), Sum(dayDtos, d => d.UnloggedMinutes), Sum(dayDtos, d => d.PlannedFocusMinutes));

        var titles = data.Entries.Where(e => e.Task is not null).GroupBy(e => e.TaskId!.Value).ToDictionary(g => g.Key, g => g.First().Task!.Title);
        var longest = days.SelectMany(d => d.FocusBlocks).OrderByDescending(b => b.Minutes).FirstOrDefault();
        var longestDto = longest is null ? null : new FocusBlockDto(longest.TaskId, titles.GetValueOrDefault(longest.TaskId), longest.Start, longest.End, Math.Round(longest.Minutes, 2));

        // Trend: this week + the previous 4, from stored aggregates (this week's days computed above).
        var trendFrom = monday.AddDays(-28);
        var previous = await GetAggregatesAsync(trendFrom, monday.AddDays(-1), ct);
        var trend = previous.Concat(dayDtos)
            .GroupBy(a => Monday(a.Date))
            .Select(g => new WeekTrendDto(g.Key,
                Sum(g, a => a.MeetingMinutes), Sum(g, a => a.TaskMinutes), Sum(g, a => a.ReactiveMinutes),
                Sum(g, a => a.UntrackedMinutes), Sum(g, a => a.UnloggedMinutes),
                g.Sum(a => a.InterruptionCount), g.Sum(a => a.FocusBlocks), g.Sum(a => a.TaskSwitches)))
            .ToDictionary(t => t.WeekStart);
        var trendList = Enumerable.Range(0, 5).Select(i => trendFrom.AddDays(7 * i))
            .Select(w => trend.GetValueOrDefault(w) ?? new WeekTrendDto(w, 0, 0, 0, 0, 0, 0, 0, 0)).ToList();

        return new WeeklyReportDto(
            monday, sunday, dayDtos, byProject, kinds,
            InterruptionStats(data, monday, sunday),
            longestDto,
            days.Sum(d => d.FocusBlocks.Count),
            days.Sum(d => d.TaskSwitches),
            trendList,
            await PlannedVsActualAsync(data, monday, sunday, ct),
            Sum(dayDtos, d => d.PlannedTaskMinutes),
            Sum(dayDtos, d => d.UnplannedTaskMinutes));
    }

    /// <summary>FR-D3 interruption breakdown for a date range.</summary>
    public async Task<InterruptionStatsDto> InterruptionsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) throw ApiException.BadRequest("The end date must be on or after the start date.");
        if (to.DayNumber - from.DayNumber > 400) throw ApiException.BadRequest("Pick a range of at most about a year.");
        return InterruptionStats(await LoadAsync(from, to, ct), from, to);
    }

    /// <summary>FR-D6 planned vs actual for a date range.</summary>
    public async Task<List<PlannedTaskDto>> PlannedVsActualAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) throw ApiException.BadRequest("The end date must be on or after the start date.");
        var data = await LoadAsync(from, to, ct);
        return await PlannedVsActualAsync(data, from, to, ct);
    }

    private async Task<List<PlannedTaskDto>> PlannedVsActualAsync(RangeData data, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var plansByTask = data.Plans.Where(p => p.Date >= from && p.Date <= to).GroupBy(p => p.TaskId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Date).OrderBy(d => d).ToList());
        if (plansByTask.Count == 0) return [];
        var ids = plansByTask.Keys.ToList();
        var tasks = await db.Tasks.Include(t => t.Project).Where(t => ids.Contains(t.Id)).ToListAsync(ct);

        var rangeStart = LocalTime.ToUtc(from, TimeOnly.MinValue, data.Tz);
        var rangeEnd = LocalTime.ToUtc(to.AddDays(1), TimeOnly.MinValue, data.Tz);
        var result = new List<PlannedTaskDto>();
        foreach (var t in tasks)
        {
            var dates = plansByTask[t.Id];
            double onPlanned = 0, inRange = 0;
            foreach (var e in data.Entries.Where(e => e.TaskId == t.Id && e.Kind != TimeEntryKind.Untracked))
            {
                if (Intervals.Clip(e.StartedAt, e.EndedAt, rangeStart, rangeEnd, data.Now) is not { } c) continue;
                inRange += c.Minutes;
                foreach (var d in dates)
                {
                    var b = LocalTime.DayBoundsUtc(d, data.Tz);
                    if (Intervals.Clip(c.Start, c.End, b.Start, b.End, data.Now) is { } dc) onPlanned += dc.Minutes;
                }
            }
            result.Add(new PlannedTaskDto(t.Id, t.Title, t.Project?.Name, t.Project?.Color, t.Status, dates, Math.Round(onPlanned, 2), Math.Round(inRange, 2)));
        }
        return result.OrderByDescending(r => r.PlannedDates.Count).ThenBy(r => r.MinutesOnPlannedDays).ThenBy(r => r.Title).ToList();
    }

    private static InterruptionStatsDto InterruptionStats(RangeData data, DateOnly from, DateOnly to)
    {
        var start = LocalTime.ToUtc(from, TimeOnly.MinValue, data.Tz);
        var end = LocalTime.ToUtc(to.AddDays(1), TimeOnly.MinValue, data.Tz);
        var items = data.Interruptions.Where(i => i.StartedAt >= start && i.StartedAt < end).ToList();
        double Minutes(Interruption i) => Mapping.Minutes((i.EndedAt ?? data.Now) - i.StartedAt);

        var bySource = items.GroupBy(i => i.SourceType)
            .Select(g => new NamedCountDto(g.Key?.ToString() ?? "Unclassified", SourceLabel(g.Key), g.Count(), Math.Round(g.Sum(Minutes), 2), g.Count(i => i.CouldWait == true)))
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.Minutes).ToList();
        var byPerson = items.Where(i => i.Person is not null).GroupBy(i => i.Person!.Name)
            .Select(g => new NamedCountDto(g.First().PersonId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), g.Key, g.Count(), Math.Round(g.Sum(Minutes), 2), g.Count(i => i.CouldWait == true)))
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.Minutes).ToList();
        var byDay = items.GroupBy(i => LocalTime.LocalDate(i.StartedAt, data.Tz))
            .OrderBy(g => g.Key)
            .Select(g => new NamedCountDto(g.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), g.Key.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture), g.Count(), Math.Round(g.Sum(Minutes), 2), g.Count(i => i.CouldWait == true)))
            .ToList();

        return new InterruptionStatsDto(from, to, items.Count, Math.Round(items.Sum(Minutes), 2), items.Count(i => i.CouldWait == true),
            bySource, byPerson, Metrics.Heatmap(items.Select(i => i.StartedAt), data.Tz), byDay);
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

    public static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static double Sum<T>(IEnumerable<T> items, Func<T, double> f) => Math.Round(items.Sum(f), 2);

    public static DailyAggregateDto ToDto(DailyAggregate a) => new(
        a.Date, a.TaskMinutes, a.ReactiveMinutes, a.UntrackedMinutes, a.MeetingMinutes, a.PlannedFocusMinutes, a.UnloggedMinutes,
        a.InterruptionCount, a.InterruptionMinutes, a.CouldWaitCount, a.TaskSwitches, a.FocusBlocks, a.LongestFocusBlockMinutes,
        a.PlannedTasks, a.PlannedTasksWorked, a.PlannedTaskMinutes, a.UnplannedTaskMinutes, a.MeetingCount, a.ComputedAt);
}
