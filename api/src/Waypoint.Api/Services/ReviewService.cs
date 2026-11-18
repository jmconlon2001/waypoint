using Waypoint.Api.Contracts;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>
/// The end-of-day review: timeline (entries, interruptions, meetings, unlogged gaps), daily totals, timers that
/// overlapped meetings, focus blocks, planned vs. actual, and today's unfinished tasks to roll over.
/// </summary>
public sealed class ReviewService(ReportService reports, TaskService tasks, SettingsService settings, TimeProvider clock)
{
    /// <summary>Gaps shorter than this are noise (the time between clicking stop and start).</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(2);

    public async Task<ReviewDto> GetDayAsync(DateOnly? date, CancellationToken ct = default)
    {
        var now = Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);
        var today = LocalTime.LocalDate(now, await settings.TimeZoneAsync(ct));
        var day = date ?? today;
        var data = await reports.LoadAsync(day, day, ct);

        var bounds = LocalTime.DayBoundsUtc(day, data.Tz);
        var work = LocalTime.WorkHoursUtc(day, data.Setting.WorkdayStart, data.Setting.WorkdayEnd, data.Tz);
        var summary = ReportService.Summarize(day, data);
        var max = data.Setting.MeetingMaxMinutes;

        var entries = data.Entries.Where(e => e.StartedAt < bounds.End && (e.EndedAt ?? now) > bounds.Start).ToList();
        var interruptions = data.Interruptions.Where(i => i.StartedAt < bounds.End && (i.EndedAt ?? now) > bounds.Start).ToList();
        var counted = data.Events.Where(e => Meetings.CountsAsMeeting(e, max)).ToList();

        var overlaps = summary.Overlaps.Select(o =>
        {
            var entry = entries.First(e => e.Id == o.EntryId);
            var meeting = counted.FirstOrDefault(m => m.StartedAt < (entry.EndedAt ?? now) && m.EndedAt > entry.StartedAt);
            return new OverlapDto(o.EntryId, entry.Task?.Title ?? entry.Project?.Name ?? "Timer", meeting?.Id ?? 0, meeting?.Title ?? "a meeting", o.Minutes);
        }).ToList();

        var entryOverlaps = FindEntryOverlaps(entries, now);

        var blocks = summary.FocusBlocks.Select(b => new FocusBlockDto(
            b.Interval.Start, b.Interval.End, Math.Round(b.Interval.Minutes, 2), b.TaskId,
            entries.FirstOrDefault(e => e.TaskId == b.TaskId)?.Task?.Title)).ToList();

        var unfinished = day == today
            ? (await tasks.TodayAsync(ct)).Where(t => t.Status != WorkTaskStatus.Done).ToList()
            : [];

        return new ReviewDto(
            day, data.Tz.Id, bounds.Start, bounds.End, work.Start, work.End,
            entries.Select(e => e.ToDto(now)).ToList(),
            interruptions.Select(i => i.ToDto(now)).ToList(),
            summary.Gaps.Select(g => new GapDto(g.Start, g.End, Math.Round(g.Minutes, 2))).ToList(),
            ReportService.ToTotalsDto(day, summary),
            data.Events.Select(e => CalendarService.ToDto(e, max)).ToList(),
            overlaps,
            entryOverlaps,
            blocks,
            unfinished,
            await reports.BuildPlanAsync(day, data, ct));
    }

    /// <summary>Pairs of entries covering the same time (≥ 1 minute), e.g. a short entry logged inside a long one.</summary>
    internal static List<EntryOverlapDto> FindEntryOverlaps(IReadOnlyList<TimeEntry> entries, DateTime now)
    {
        string Title(TimeEntry e) => e.Task?.Title ?? e.Project?.Name ?? "Untracked";
        var result = new List<EntryOverlapDto>();
        var sorted = entries.OrderBy(e => e.StartedAt).ToList();
        for (var i = 0; i < sorted.Count; i++)
            for (var j = i + 1; j < sorted.Count && sorted[j].StartedAt < (sorted[i].EndedAt ?? now); j++)
            {
                var a = sorted[i];
                var b = sorted[j];
                var aEnd = a.EndedAt ?? now;
                var bEnd = b.EndedAt ?? now;
                var minutes = ((aEnd < bEnd ? aEnd : bEnd) - b.StartedAt).TotalMinutes;
                if (minutes < 1) continue;
                // Keep the shorter, finished one: it's the more specific entry (a running timer can't be kept).
                var keepB = b.EndedAt is not null && (a.EndedAt is null || bEnd - b.StartedAt <= aEnd - a.StartedAt);
                var (keep, other) = keepB ? (b, a) : (a, b);
                result.Add(new EntryOverlapDto(keep.Id, Title(keep), other.Id, Title(other), Math.Round(minutes, 2)));
            }
        return result;
    }

    public async Task<DailyTotalsDto> GetTotalsAsync(DateOnly? date, CancellationToken ct = default) =>
        (await GetDayAsync(date, ct)).Totals;
}
