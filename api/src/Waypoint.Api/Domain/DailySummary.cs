using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Domain;

public sealed record SummaryEntry(DateTime StartedAt, DateTime? EndedAt, TimeEntryKind Kind, int? ProjectId, string? ProjectName, string? ProjectColor);

public sealed record ProjectTotal(int? ProjectId, string Name, string? Color, double Minutes);

public sealed record DailySummary(
    double TaskMinutes,
    double ReactiveMinutes,
    double UntrackedMinutes,
    double TrackedMinutes,
    int InterruptionCount,
    double InterruptionMinutes,
    double UnloggedMinutes,
    IReadOnlyList<ProjectTotal> ByProject,
    IReadOnlyList<Interval> Gaps)
{
    public const string NoProject = "No project";

    /// <summary>
    /// Totals for one local day. Entries and interruptions are clipped to the day (running ones end at
    /// <paramref name="now"/>). Gaps are the parts of the work hours, up to now, covered by neither.
    /// </summary>
    public static DailySummary Build(
        Interval day,
        Interval workHours,
        DateTime now,
        IEnumerable<SummaryEntry> entries,
        IEnumerable<(DateTime StartedAt, DateTime? EndedAt)> interruptions,
        TimeSpan minGap)
    {
        double task = 0, reactive = 0, untracked = 0;
        var byProject = new Dictionary<int, ProjectTotal>();
        double noProject = 0;
        var busy = new List<Interval>();

        foreach (var e in entries)
        {
            if (Intervals.Clip(e.StartedAt, e.EndedAt, day.Start, day.End, now) is not { } clipped) continue;
            busy.Add(clipped);
            var minutes = clipped.Minutes;
            switch (e.Kind)
            {
                case TimeEntryKind.Task: task += minutes; break;
                case TimeEntryKind.Reactive: reactive += minutes; break;
                default: untracked += minutes; break;
            }
            if (e.Kind == TimeEntryKind.Untracked) continue;
            if (e.ProjectId is int pid)
            {
                byProject[pid] = byProject.TryGetValue(pid, out var cur)
                    ? cur with { Minutes = cur.Minutes + minutes }
                    : new ProjectTotal(pid, e.ProjectName ?? $"Project {pid}", e.ProjectColor, minutes);
            }
            else
            {
                noProject += minutes;
            }
        }

        int count = 0;
        double interruptionMinutes = 0;
        foreach (var (start, end) in interruptions)
        {
            if (Intervals.Clip(start, end, day.Start, day.End, now) is not { } clipped) continue;
            count++;
            interruptionMinutes += clipped.Minutes;
            busy.Add(clipped);
        }

        // Only look for gaps in the part of the work day that has already happened.
        var gapWindow = new Interval(workHours.Start, workHours.End < now ? workHours.End : now);
        var gaps = Intervals.Gaps(gapWindow, busy, minGap);

        var projects = byProject.Values.Select(p => p with { Minutes = Round(p.Minutes) }).OrderByDescending(p => p.Minutes).ToList();
        if (noProject > 0) projects.Add(new ProjectTotal(null, NoProject, null, Round(noProject)));

        return new DailySummary(
            Round(task), Round(reactive), Round(untracked), Round(task + reactive + untracked),
            count, Round(interruptionMinutes), Round(gaps.Sum(g => g.Minutes)),
            projects, gaps);
    }

    private static double Round(double v) => Math.Round(v, 2);
}
