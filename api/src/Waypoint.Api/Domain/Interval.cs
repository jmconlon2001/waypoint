namespace Waypoint.Api.Domain;

/// <summary>A half-open UTC time range [Start, End).</summary>
public readonly record struct Interval(DateTime Start, DateTime End)
{
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
    public double Minutes => Duration.TotalMinutes;
    public bool IsEmpty => End <= Start;
}

public static class Intervals
{
    /// <summary>
    /// Clips [start, end ?? now) to [from, to). Returns null when nothing is left.
    /// An open interval (end == null) is treated as running until <paramref name="now"/>.
    /// </summary>
    public static Interval? Clip(DateTime start, DateTime? end, DateTime from, DateTime to, DateTime now)
    {
        var effectiveEnd = end ?? (now > start ? now : start);
        var s = start > from ? start : from;
        var e = effectiveEnd < to ? effectiveEnd : to;
        return e > s ? new Interval(s, e) : null;
    }

    /// <summary>Sorts and merges overlapping or touching intervals.</summary>
    public static List<Interval> Merge(IEnumerable<Interval> intervals)
    {
        var sorted = intervals.Where(i => !i.IsEmpty).OrderBy(i => i.Start).ToList();
        var result = new List<Interval>(sorted.Count);
        foreach (var i in sorted)
        {
            if (result.Count > 0 && i.Start <= result[^1].End)
            {
                var last = result[^1];
                result[^1] = last with { End = i.End > last.End ? i.End : last.End };
            }
            else
            {
                result.Add(i);
            }
        }
        return result;
    }

    /// <summary>Parts of <paramref name="window"/> not covered by <paramref name="busy"/>, ignoring gaps shorter than <paramref name="minGap"/>.</summary>
    public static List<Interval> Gaps(Interval window, IEnumerable<Interval> busy, TimeSpan minGap)
    {
        var gaps = new List<Interval>();
        if (window.IsEmpty) return gaps;
        var cursor = window.Start;
        foreach (var b in Merge(busy))
        {
            if (b.End <= window.Start) continue;
            if (b.Start >= window.End) break;
            if (b.Start > cursor) AddGap(gaps, new Interval(cursor, b.Start), minGap);
            if (b.End > cursor) cursor = b.End;
            if (cursor >= window.End) break;
        }
        if (cursor < window.End) AddGap(gaps, new Interval(cursor, window.End), minGap);
        return gaps;
    }

    /// <summary>Minutes of <paramref name="interval"/> covered by <paramref name="mergedBusy"/> (sorted, non-overlapping).</summary>
    public static double OverlapMinutes(Interval interval, IReadOnlyList<Interval> mergedBusy)
    {
        double total = 0;
        foreach (var b in mergedBusy)
        {
            if (b.End <= interval.Start) continue;
            if (b.Start >= interval.End) break;
            var s = b.Start > interval.Start ? b.Start : interval.Start;
            var e = b.End < interval.End ? b.End : interval.End;
            if (e > s) total += (e - s).TotalMinutes;
        }
        return total;
    }

    /// <summary>The parts of <paramref name="interval"/> not covered by <paramref name="cut"/>.</summary>
    public static List<Interval> Subtract(Interval interval, IEnumerable<Interval> cut) =>
        Gaps(interval, cut, TimeSpan.Zero);

    private static void AddGap(List<Interval> gaps, Interval gap, TimeSpan minGap)
    {
        if (gap.Duration >= minGap && !gap.IsEmpty) gaps.Add(gap);
    }
}
