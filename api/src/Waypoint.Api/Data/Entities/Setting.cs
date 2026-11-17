namespace Waypoint.Api.Data.Entities;

public enum CalendarSourceKind
{
    None,
    Google,
    ICal,
}

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

    // ---- Calendar (Phase 2) ----
    public CalendarSourceKind CalendarSource { get; set; } = CalendarSourceKind.None;
    /// <summary>Events longer than this don't count as meeting time (still shown as context).</summary>
    public int MaxMeetingMinutes { get; set; } = 120;
    /// <summary>Comma-separated e-mail domains treated as internal, e.g. "cfs.canon.com,canon.com".</summary>
    public string InternalDomains { get; set; } = "";
    /// <summary>Google refresh token, encrypted with ASP.NET Core Data Protection.</summary>
    public string? GoogleRefreshTokenProtected { get; set; }
    public string? GoogleAccount { get; set; }
    public DateTime? CalendarLastSyncAt { get; set; }
    public string? CalendarLastSyncError { get; set; }

    // ---- Review reminder (Phase 2) ----
    public bool ReviewReminderEnabled { get; set; }
    public TimeOnly ReviewReminderTime { get; set; } = new(17, 15);
}
