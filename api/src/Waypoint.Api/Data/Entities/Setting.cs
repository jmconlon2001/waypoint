namespace Waypoint.Api.Data.Entities;

/// <summary>Single-row settings table (Id is always 1).</summary>
public class Setting
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public TimeOnly WorkdayStart { get; set; } = new(8, 45);
    public TimeOnly WorkdayEnd { get; set; } = new(17, 30);
    public int TimerWarningMinutes { get; set; } = 180;
    /// <summary>IANA time zone used to display times and define "a day".</summary>
    public string TimeZone { get; set; } = "America/New_York";

    /// <summary>Calendar events longer than this (holds, OOO, trainings) don't count as meeting time.</summary>
    public int MeetingMaxMinutes { get; set; } = 120;
    /// <summary>How far past "now" a time entry may end (e.g. logging a block you're about to start). 0 = no future entries.</summary>
    public int FutureEntryMaxMinutes { get; set; } = 240;
    /// <summary>Local time for the daily review reminder; null = off.</summary>
    public TimeOnly? ReviewReminderTime { get; set; }

    /// <summary>The calendar's secret iCal address. Null = calendar import off.</summary>
    public string? CalendarIcsUrl { get; set; }
    /// <summary>Email domain(s) treated as internal when classifying meetings, comma-separated (e.g. "cfs.com").</summary>
    public string? CalendarInternalDomains { get; set; }
    public DateTime? CalendarLastSyncAt { get; set; }
    public string? CalendarLastSyncError { get; set; }
}
