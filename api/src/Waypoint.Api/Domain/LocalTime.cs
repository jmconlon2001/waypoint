namespace Waypoint.Api.Domain;

/// <summary>Conversions between UTC (stored) and the user's local time zone (displayed / "a day").</summary>
public static class LocalTime
{
    public const string DefaultTimeZone = "America/New_York";

    public static TimeZoneInfo FindTimeZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz))
            return tz;
        return TimeZoneInfo.TryFindSystemTimeZoneById(DefaultTimeZone, out var fallback) ? fallback : TimeZoneInfo.Utc;
    }

    public static bool IsValidTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo tz) => DateOnly.FromDateTime(ToLocal(utc, tz));

    /// <summary>UTC instant of a local wall-clock time. Times skipped by a DST jump are moved forward.</summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        var guard = 0;
        while (tz.IsInvalidTime(local) && guard++ < 180) local = local.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    /// <summary>[start, end) of a local calendar day, in UTC.</summary>
    public static Interval DayBoundsUtc(DateOnly date, TimeZoneInfo tz) =>
        new(ToUtc(date, TimeOnly.MinValue, tz), ToUtc(date.AddDays(1), TimeOnly.MinValue, tz));

    /// <summary>Configured work hours of a local day, in UTC.</summary>
    public static Interval WorkHoursUtc(DateOnly date, TimeOnly start, TimeOnly end, TimeZoneInfo tz) =>
        new(ToUtc(date, start, tz), ToUtc(date, end, tz));
}
