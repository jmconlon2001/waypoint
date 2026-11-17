using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>The end-of-day timeline (entries, interruptions, unlogged gaps) and daily totals.</summary>
public sealed class ReviewService(WaypointDbContext db, SettingsService settings, TimeProvider clock)
{
    /// <summary>Gaps shorter than this are noise (the time between clicking stop and start).</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(2);

    public async Task<ReviewDto> GetDayAsync(DateOnly? date, CancellationToken ct = default)
    {
        var setting = await settings.GetAsync(ct);
        var tz = LocalTime.FindTimeZone(setting.TimeZone);
        var now = Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);
        var day = date ?? LocalTime.LocalDate(now, tz);
        var bounds = LocalTime.DayBoundsUtc(day, tz);
        var work = LocalTime.WorkHoursUtc(day, setting.WorkdayStart, setting.WorkdayEnd, tz);

        var entries = await db.TimeEntries.Include(e => e.Task).Include(e => e.Project)
            .Where(e => e.StartedAt < bounds.End && (e.EndedAt == null || e.EndedAt > bounds.Start))
            .OrderBy(e => e.StartedAt)
            .ToListAsync(ct);
        var interruptions = await db.Interruptions
            .Include(i => i.Person)
            .Include(i => i.PausedTimeEntry).ThenInclude(e => e!.Task)
            .Where(i => i.StartedAt < bounds.End && (i.EndedAt == null || i.EndedAt > bounds.Start))
            .OrderBy(i => i.StartedAt)
            .ToListAsync(ct);

        var summary = DailySummary.Build(
            bounds, work, now,
            entries.Select(e => new SummaryEntry(e.StartedAt, e.EndedAt, e.Kind, e.ProjectId, e.Project?.Name, e.Project?.Color)),
            interruptions.Select(i => (i.StartedAt, i.EndedAt)),
            MinGap);

        var totals = new DailyTotalsDto(
            day,
            summary.TaskMinutes, summary.ReactiveMinutes, summary.UntrackedMinutes, summary.TrackedMinutes,
            summary.InterruptionCount, summary.InterruptionMinutes, summary.UnloggedMinutes,
            summary.ByProject.Select(p => new ProjectTotalDto(p.ProjectId, p.Name, p.Color, p.Minutes)).ToList());

        return new ReviewDto(
            day, tz.Id, bounds.Start, bounds.End, work.Start, work.End,
            entries.Select(e => e.ToDto(now)).ToList(),
            interruptions.Select(i => i.ToDto(now)).ToList(),
            summary.Gaps.Select(g => new GapDto(g.Start, g.End, Math.Round(g.Minutes, 2))).ToList(),
            totals);
    }

    public async Task<DailyTotalsDto> GetTotalsAsync(DateOnly? date, CancellationToken ct = default) =>
        (await GetDayAsync(date, ct)).Totals;
}
