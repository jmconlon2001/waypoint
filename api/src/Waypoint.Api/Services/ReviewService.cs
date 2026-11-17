using Waypoint.Api.Contracts;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>
/// The end-of-day timeline (entries, interruptions, calendar events, unlogged gaps), meeting
/// overlaps and daily totals.
/// </summary>
public sealed class ReviewService(MetricsService metrics, SettingsService settings, TimeProvider clock)
{
    /// <summary>Gaps shorter than this are noise (the time between clicking stop and start).</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(2);

    public async Task<ReviewDto> GetDayAsync(DateOnly? date, CancellationToken ct = default)
    {
        var tz = await settings.TimeZoneAsync(ct);
        var day = date ?? LocalTime.LocalDate(clock.GetUtcNow().UtcDateTime, tz);
        var data = await metrics.LoadAsync(day, day, ct);
        var m = metrics.Compute(data, day);
        var s = data.Settings;
        var now = data.Now;
        var bounds = LocalTime.DayBoundsUtc(day, data.Tz);
        var work = LocalTime.WorkHoursUtc(day, s.WorkdayStart, s.WorkdayEnd, data.Tz);

        var entries = data.Entries.Where(e => e.StartedAt < bounds.End && (e.EndedAt ?? now) > bounds.Start).ToList();
        var events = data.Events.Where(e => e.StartedAt < bounds.End && e.EndedAt > bounds.Start).ToList();
        var eventDtos = events.Select(e => CalendarService.ToDto(e, s.MaxMeetingMinutes)).ToList();
        var counted = events.Where((e, i) => eventDtos[i].Counted).ToList();

        var overlaps = Metrics.MeetingOverlaps(
                entries.Select(e => new MetricEntry(e.StartedAt, e.EndedAt, e.Kind, e.TaskId, e.ProjectId)).ToList(),
                counted.Select(e => new Interval(e.StartedAt, e.EndedAt)).ToList(),
                now)
            .Select(o => new MeetingOverlapDto(entries[o.EntryIndex].Id, counted[o.MeetingIndex].Id, counted[o.MeetingIndex].Title, o.Start, o.End, Math.Round(o.Minutes, 2)))
            .ToList();

        var titles = entries.Where(e => e.Task is not null).GroupBy(e => e.TaskId!.Value).ToDictionary(g => g.Key, g => g.First().Task!.Title);
        var blocks = m.FocusBlocks.Select(b => new FocusBlockDto(b.TaskId, titles.GetValueOrDefault(b.TaskId), b.Start, b.End, Math.Round(b.Minutes, 2))).ToList();

        var sm = m.Summary;
        var totals = new DailyTotalsDto(
            day,
            sm.TaskMinutes, sm.ReactiveMinutes, sm.UntrackedMinutes, sm.TrackedMinutes,
            sm.InterruptionCount, sm.InterruptionMinutes, sm.UnloggedMinutes,
            sm.ByProject.Select(p => new ProjectTotalDto(p.ProjectId, p.Name, p.Color, p.Minutes)).ToList(),
            sm.MeetingMinutes, sm.MeetingCount, m.PlannedFocusMinutes, m.CouldWaitCount, m.TaskSwitches, m.FocusBlocks.Count, m.LongestFocusBlockMinutes);

        return new ReviewDto(
            day, data.Tz.Id, bounds.Start, bounds.End, work.Start, work.End,
            entries.Select(e => e.ToDto(now)).ToList(),
            data.Interruptions.Where(i => i.StartedAt < bounds.End && (i.EndedAt ?? now) > bounds.Start).Select(i => i.ToDto(now)).ToList(),
            sm.Gaps.Select(g => new GapDto(g.Start, g.End, Math.Round(g.Minutes, 2))).ToList(),
            totals,
            eventDtos,
            overlaps,
            blocks);
    }

    public async Task<DailyTotalsDto> GetTotalsAsync(DateOnly? date, CancellationToken ct = default) =>
        (await GetDayAsync(date, ct)).Totals;
}
