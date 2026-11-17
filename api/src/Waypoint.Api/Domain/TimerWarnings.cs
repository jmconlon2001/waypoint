namespace Waypoint.Api.Domain;

public static class TimerWarnings
{
    public const string LongRunning = "long-running";
    public const string CrossedMidnight = "crossed-midnight";

    /// <summary>Warnings for a running timer: over the threshold, and/or started on an earlier local day.</summary>
    public static IReadOnlyList<string> For(DateTime startedAtUtc, DateTime nowUtc, int thresholdMinutes, TimeZoneInfo tz)
    {
        var warnings = new List<string>(2);
        if (thresholdMinutes > 0 && (nowUtc - startedAtUtc).TotalMinutes > thresholdMinutes)
            warnings.Add(LongRunning);
        if (LocalTime.LocalDate(startedAtUtc, tz) != LocalTime.LocalDate(nowUtc, tz))
            warnings.Add(CrossedMidnight);
        return warnings;
    }
}
