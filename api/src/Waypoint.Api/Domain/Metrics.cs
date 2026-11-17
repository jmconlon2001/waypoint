using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Domain;

public sealed record MetricEntry(DateTime StartedAt, DateTime? EndedAt, TimeEntryKind Kind, int? TaskId, int? ProjectId);

public sealed record MetricInterruption(DateTime StartedAt, DateTime? EndedAt, InterruptionSource? Source, int? PersonId, string? PersonName, bool? CouldWait);

public sealed record FocusBlock(int TaskId, DateTime Start, DateTime End)
{
    public double Minutes => (End - Start).TotalMinutes;
}

public sealed record MeetingOverlap(int EntryIndex, int MeetingIndex, DateTime Start, DateTime End)
{
    public double Minutes => (End - Start).TotalMinutes;
}

/// <summary>Pure metric calculations over a day's (or range's) entries, interruptions and meetings.</summary>
public static class Metrics
{
    public const int DefaultFocusBlockMinutes = 25;
    /// <summary>Two entries of the same task this close together are one continuous stretch (stop/start, a quick switch-back).</summary>
    public static readonly TimeSpan MergeTolerance = TimeSpan.FromMinutes(1);

    /// <summary>
    /// FR-D2: stretches of at least <paramref name="minMinutes"/> continuous minutes on one task timer
    /// (kind Task) with no interruption logged inside. Back-to-back entries of the same task are joined;
    /// an interruption start splits a stretch. Running entries end at <paramref name="now"/>.
    /// </summary>
    public static List<FocusBlock> FocusBlocks(IEnumerable<MetricEntry> entries, IEnumerable<DateTime> interruptionStarts, DateTime now, int minMinutes = DefaultFocusBlockMinutes)
    {
        var cuts = interruptionStarts.OrderBy(t => t).ToList();
        var stretches = new List<FocusBlock>();
        foreach (var e in entries.Where(e => e.Kind == TimeEntryKind.Task && e.TaskId is not null).OrderBy(e => e.StartedAt))
        {
            var end = e.EndedAt ?? now;
            if (end <= e.StartedAt) continue;
            if (stretches.Count > 0 && stretches[^1] is var last && last.TaskId == e.TaskId && e.StartedAt - last.End <= MergeTolerance && e.StartedAt >= last.Start)
                stretches[^1] = last with { End = end > last.End ? end : last.End };
            else
                stretches.Add(new FocusBlock(e.TaskId!.Value, e.StartedAt, end));
        }

        var blocks = new List<FocusBlock>();
        foreach (var s in stretches)
        {
            var segmentStart = s.Start;
            foreach (var cut in cuts.Where(c => c > s.Start && c < s.End))
            {
                Add(new FocusBlock(s.TaskId, segmentStart, cut));
                segmentStart = cut;
            }
            Add(new FocusBlock(s.TaskId, segmentStart, s.End));
        }
        return blocks;

        void Add(FocusBlock b)
        {
            if (b.Minutes >= minMinutes) blocks.Add(b);
        }
    }

    /// <summary>
    /// FR-D4: how often the active timer changed to something else (a different task, or a different
    /// project/kind for task-less entries). Resuming the same task after a pause is not a switch.
    /// </summary>
    public static int TaskSwitches(IEnumerable<MetricEntry> entries)
    {
        var switches = 0;
        (int? Task, int? Project, TimeEntryKind Kind)? previous = null;
        foreach (var e in entries.OrderBy(e => e.StartedAt))
        {
            var key = (e.TaskId, e.TaskId is null ? e.ProjectId : null, e.TaskId is null ? e.Kind : TimeEntryKind.Task);
            if (previous is not null && previous != key) switches++;
            previous = key;
        }
        return switches;
    }

    /// <summary>
    /// FR-D3: counts per local weekday (Monday = 0) × hour (0-23), in the given time zone, so a 09:30
    /// interruption is in the 9 o'clock bucket in both EST and EDT.
    /// </summary>
    public static int[][] Heatmap(IEnumerable<DateTime> startsUtc, TimeZoneInfo tz)
    {
        var grid = Enumerable.Range(0, 7).Select(_ => new int[24]).ToArray();
        foreach (var t in startsUtc)
        {
            var local = LocalTime.ToLocal(t, tz);
            grid[((int)local.DayOfWeek + 6) % 7][local.Hour]++;
        }
        return grid;
    }

    /// <summary>
    /// FR-C2: task/reactive time entries that overlap a counted meeting by at least <paramref name="minOverlap"/>.
    /// Untracked entries are ignored. Running entries end at <paramref name="now"/>.
    /// </summary>
    public static List<MeetingOverlap> MeetingOverlaps(IReadOnlyList<MetricEntry> entries, IReadOnlyList<Interval> countedMeetings, DateTime now, TimeSpan? minOverlap = null)
    {
        var min = minOverlap ?? TimeSpan.FromMinutes(1);
        var result = new List<MeetingOverlap>();
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Kind == TimeEntryKind.Untracked) continue;
            var end = e.EndedAt ?? now;
            for (var m = 0; m < countedMeetings.Count; m++)
            {
                var meeting = countedMeetings[m];
                var s = e.StartedAt > meeting.Start ? e.StartedAt : meeting.Start;
                var x = end < meeting.End ? end : meeting.End;
                if (x - s >= min) result.Add(new MeetingOverlap(i, m, s, x));
            }
        }
        return result;
    }

    /// <summary>Union length of intervals, in minutes.</summary>
    public static double UnionMinutes(IEnumerable<Interval> intervals) => Intervals.Merge(intervals).Sum(i => i.Minutes);
}
