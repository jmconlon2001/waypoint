using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Domain;

/// <summary>Everything Waypoint measures about one local day.</summary>
public sealed record DayMetrics(
    DateOnly Date,
    DailySummary Summary,
    int TaskSwitches,
    IReadOnlyList<FocusBlock> FocusBlocks,
    int CouldWaitCount,
    double PlannedFocusMinutes,
    int PlannedTasks,
    int PlannedTasksWorked,
    double PlannedTaskMinutes,
    double UnplannedTaskMinutes)
{
    public double LongestFocusBlockMinutes => FocusBlocks.Count == 0 ? 0 : Math.Round(FocusBlocks.Max(b => b.Minutes), 2);

    /// <summary>
    /// Computes the day. <paramref name="plannedFocus"/> are Google focus-time blocks;
    /// <paramref name="plannedTaskIds"/> are the tasks planned for this day (FR-D6).
    /// </summary>
    public static DayMetrics Compute(
        DateOnly date,
        Interval day,
        Interval workHours,
        DateTime now,
        IReadOnlyList<SummaryEntry> entries,
        IReadOnlyList<MetricInterruption> interruptions,
        IReadOnlyList<SummaryMeeting> countedMeetings,
        IReadOnlyList<Interval> plannedFocus,
        IReadOnlyCollection<int> plannedTaskIds,
        TimeSpan minGap,
        int focusBlockMinutes = Metrics.DefaultFocusBlockMinutes)
    {
        var summary = DailySummary.Build(day, workHours, now, entries,
            interruptions.Select(i => (i.StartedAt, i.EndedAt)), minGap, countedMeetings);

        // Clip to the day so cross-midnight entries split cleanly between days.
        var clipped = entries
            .Select(e => Intervals.Clip(e.StartedAt, e.EndedAt, day.Start, day.End, now) is { } c
                ? new MetricEntry(c.Start, c.End, e.Kind, e.TaskId, e.ProjectId)
                : null)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
        var dayInterruptions = interruptions.Where(i => i.StartedAt >= day.Start && i.StartedAt < day.End).ToList();

        var blocks = Metrics.FocusBlocks(clipped, dayInterruptions.Select(i => i.StartedAt), now, focusBlockMinutes);
        var switches = Metrics.TaskSwitches(clipped);

        double planned = 0, unplanned = 0;
        var worked = new HashSet<int>();
        foreach (var e in clipped.Where(e => e.Kind != TimeEntryKind.Untracked))
        {
            var minutes = ((e.EndedAt ?? now) - e.StartedAt).TotalMinutes;
            if (e.TaskId is int t && plannedTaskIds.Contains(t))
            {
                planned += minutes;
                worked.Add(t);
            }
            else
            {
                unplanned += minutes;
            }
        }

        var focusPlanned = Metrics.UnionMinutes(plannedFocus
            .Select(f => Intervals.Clip(f.Start, f.End, day.Start, day.End, now))
            .Where(f => f is not null)
            .Select(f => f!.Value));

        return new DayMetrics(
            date, summary, switches, blocks,
            dayInterruptions.Count(i => i.CouldWait == true),
            Math.Round(focusPlanned, 2),
            plannedTaskIds.Count, worked.Count,
            Math.Round(planned, 2), Math.Round(unplanned, 2));
    }

    public DailyAggregate ToAggregate(DateTime computedAt) => new()
    {
        Date = Date,
        TaskMinutes = Summary.TaskMinutes,
        ReactiveMinutes = Summary.ReactiveMinutes,
        UntrackedMinutes = Summary.UntrackedMinutes,
        MeetingMinutes = Summary.MeetingMinutes,
        MeetingCount = Summary.MeetingCount,
        PlannedFocusMinutes = PlannedFocusMinutes,
        UnloggedMinutes = Summary.UnloggedMinutes,
        InterruptionCount = Summary.InterruptionCount,
        InterruptionMinutes = Summary.InterruptionMinutes,
        CouldWaitCount = CouldWaitCount,
        TaskSwitches = TaskSwitches,
        FocusBlocks = FocusBlocks.Count,
        LongestFocusBlockMinutes = LongestFocusBlockMinutes,
        PlannedTasks = PlannedTasks,
        PlannedTasksWorked = PlannedTasksWorked,
        PlannedTaskMinutes = PlannedTaskMinutes,
        UnplannedTaskMinutes = UnplannedTaskMinutes,
        ComputedAt = computedAt,
    };
}
