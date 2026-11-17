using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Domain;

public sealed record SummaryEntry(
    DateTime StartedAt,
    DateTime? EndedAt,
    TimeEntryKind Kind,
    int? ProjectId,
    string? ProjectName,
    string? ProjectColor,
    int? TaskId = null,
    int Id = 0);

/// <summary>A calendar event that already passed the "counts as meeting" / "planned focus" filter.</summary>
public sealed record SummaryMeeting(
    DateTime StartedAt,
    DateTime EndedAt,
    int? ProjectId,
    string? ProjectName,
    string? ProjectColor,
    bool IsFocusTime,
    int Id = 0);

public sealed record ProjectTotal(int? ProjectId, string Name, string? Color, double Minutes);

/// <summary>≥ 25 minutes on one task timer with no interruption (and no meeting) in it.</summary>
public sealed record FocusBlock(Interval Interval, int TaskId);

/// <summary>A time entry that ran while a counted meeting was on (likely a timer left running).</summary>
public sealed record MeetingOverlap(int EntryId, double Minutes);

public sealed record DailySummary(
    double TaskMinutes,
    double ReactiveMinutes,
    double UntrackedMinutes,
    double TrackedMinutes,
    int InterruptionCount,
    double InterruptionMinutes,
    double UnloggedMinutes,
    IReadOnlyList<ProjectTotal> ByProject,
    IReadOnlyList<Interval> Gaps,
    double MeetingMinutes,
    int MeetingCount,
    double PlannedFocusMinutes,
    IReadOnlyList<FocusBlock> FocusBlocks,
    int TaskSwitches,
    IReadOnlyList<MeetingOverlap> Overlaps)
{
    public const string NoProject = "No project";
    public const string UnmappedMeetings = "Meetings (no project)";
    public static readonly TimeSpan FocusBlockMinimum = TimeSpan.FromMinutes(25);

    public double FocusBlockMinutes => FocusBlocks.Sum(b => b.Interval.Minutes);
    public double LongestFocusBlockMinutes => FocusBlocks.Count == 0 ? 0 : FocusBlocks.Max(b => b.Interval.Minutes);

    /// <summary>
    /// Totals for one local day. Entries, interruptions and meetings are clipped to the day (running entries end
    /// at <paramref name="now"/>; meetings count only up to now). Meetings win over time entries: an entry that
    /// overlaps a meeting only counts for the part outside it, so the day adds up to wall-clock time, and the
    /// overlap is reported. Gaps are the parts of the work hours, up to now, covered by nothing.
    /// </summary>
    public static DailySummary Build(
        Interval day,
        Interval workHours,
        DateTime now,
        IEnumerable<SummaryEntry> entries,
        IEnumerable<(DateTime StartedAt, DateTime? EndedAt)> interruptions,
        TimeSpan minGap,
        IEnumerable<SummaryMeeting>? meetings = null)
    {
        var byProject = new Dictionary<int, ProjectTotal>();
        double noProject = 0, unmappedMeetings = 0;
        var busy = new List<Interval>();

        void AddProject(int pid, string? name, string? color, double minutes) =>
            byProject[pid] = byProject.TryGetValue(pid, out var cur)
                ? cur with { Minutes = cur.Minutes + minutes }
                : new ProjectTotal(pid, name ?? $"Project {pid}", color, minutes);

        // Meetings: count each minute once, even when meetings overlap; only the past part of today.
        var meetingWindowEnd = day.End < now ? day.End : now;
        var counted = new List<Interval>();
        double meetingMinutes = 0, plannedFocus = 0;
        var meetingCount = 0;
        foreach (var m in (meetings ?? []).OrderBy(m => m.StartedAt))
        {
            if (m.IsFocusTime)
            {
                if (Intervals.Clip(m.StartedAt, m.EndedAt, day.Start, day.End, now) is { } f) plannedFocus += f.Minutes;
                continue;
            }
            if (Intervals.Clip(m.StartedAt, m.EndedAt, day.Start, meetingWindowEnd, now) is not { } clipped) continue;
            meetingCount++;
            var fresh = Intervals.Subtract(clipped, counted).Sum(i => i.Minutes);
            counted.Add(clipped);
            meetingMinutes += fresh;
            if (m.ProjectId is int pid) AddProject(pid, m.ProjectName, m.ProjectColor, fresh);
            else unmappedMeetings += fresh;
        }
        var meetingUnion = Intervals.Merge(counted);
        busy.AddRange(meetingUnion);

        double task = 0, reactive = 0, untracked = 0;
        var overlaps = new List<MeetingOverlap>();
        var clippedEntries = new List<(SummaryEntry Entry, Interval Interval)>();
        foreach (var e in entries)
        {
            if (Intervals.Clip(e.StartedAt, e.EndedAt, day.Start, day.End, now) is not { } clipped) continue;
            busy.Add(clipped);
            clippedEntries.Add((e, clipped));
            var overlap = Intervals.OverlapMinutes(clipped, meetingUnion);
            if (overlap >= 1 && e.Kind != TimeEntryKind.Untracked) overlaps.Add(new MeetingOverlap(e.Id, Round(overlap)));
            var minutes = clipped.Minutes - overlap;
            switch (e.Kind)
            {
                case TimeEntryKind.Task: task += minutes; break;
                case TimeEntryKind.Reactive: reactive += minutes; break;
                default: untracked += minutes; break;
            }
            if (e.Kind == TimeEntryKind.Untracked) continue;
            if (e.ProjectId is int pid) AddProject(pid, e.ProjectName, e.ProjectColor, minutes);
            else noProject += minutes;
        }

        int count = 0;
        double interruptionMinutes = 0;
        var interruptionIntervals = new List<Interval>();
        foreach (var (start, end) in interruptions)
        {
            if (Intervals.Clip(start, end, day.Start, day.End, now) is not { } clipped) continue;
            count++;
            interruptionMinutes += clipped.Minutes;
            interruptionIntervals.Add(clipped);
            busy.Add(clipped);
        }

        // Only look for gaps in the part of the work day that has already happened.
        var gapWindow = new Interval(workHours.Start, workHours.End < now ? workHours.End : now);
        var gaps = Intervals.Gaps(gapWindow, busy, minGap);

        var projects = byProject.Values.Select(p => p with { Minutes = Round(p.Minutes) })
            .Where(p => p.Minutes > 0)
            .OrderByDescending(p => p.Minutes).ToList();
        if (noProject > 0) projects.Add(new ProjectTotal(null, NoProject, null, Round(noProject)));
        if (unmappedMeetings > 0) projects.Add(new ProjectTotal(null, UnmappedMeetings, null, Round(unmappedMeetings)));

        return new DailySummary(
            Round(task), Round(reactive), Round(untracked), Round(task + reactive + untracked),
            count, Round(interruptionMinutes), Round(gaps.Sum(g => g.Minutes)),
            projects, gaps,
            Round(meetingMinutes), meetingCount, Round(plannedFocus),
            FindFocusBlocks(clippedEntries, interruptionIntervals.Concat(meetingUnion), minGap),
            CountSwitches(clippedEntries.Select(c => c.Entry)),
            overlaps);
    }

    /// <summary>
    /// Joins back-to-back entries on the same task (stop/start within <paramref name="joinGap"/>), removes any
    /// time covered by an interruption or meeting, and keeps the pieces of at least 25 minutes.
    /// </summary>
    internal static List<FocusBlock> FindFocusBlocks(
        IEnumerable<(SummaryEntry Entry, Interval Interval)> entries, IEnumerable<Interval> breaks, TimeSpan joinGap)
    {
        var runs = new List<(int TaskId, Interval Interval)>();
        foreach (var (e, i) in entries.Where(x => x.Entry.Kind == TimeEntryKind.Task && x.Entry.TaskId is not null).OrderBy(x => x.Interval.Start))
        {
            var taskId = e.TaskId!.Value;
            if (runs.Count > 0 && runs[^1].TaskId == taskId && i.Start - runs[^1].Interval.End <= joinGap)
            {
                var last = runs[^1];
                runs[^1] = (taskId, last.Interval with { End = i.End > last.Interval.End ? i.End : last.Interval.End });
            }
            else
            {
                runs.Add((taskId, i));
            }
        }

        var cuts = Intervals.Merge(breaks);
        var blocks = new List<FocusBlock>();
        foreach (var (taskId, run) in runs)
            foreach (var piece in Intervals.Subtract(run, cuts))
                if (piece.Duration >= FocusBlockMinimum) blocks.Add(new FocusBlock(piece, taskId));
        return blocks;
    }

    /// <summary>How often the active timer changed: consecutive entries on a different task, project or kind.</summary>
    internal static int CountSwitches(IEnumerable<SummaryEntry> entries)
    {
        var switches = 0;
        (int?, int?, TimeEntryKind)? previous = null;
        foreach (var e in entries.OrderBy(e => e.StartedAt))
        {
            var key = (e.TaskId, e.ProjectId, e.Kind);
            if (previous is not null && previous != key) switches++;
            previous = key;
        }
        return switches;
    }

    private static double Round(double v) => Math.Round(v, 2);
}
