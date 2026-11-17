namespace Waypoint.Api.Data.Entities;

public enum MeetingType
{
    OneOnOne,
    Team,
    External,
    Recurring,
    AdHoc,
    /// <summary>A Focus Time block: planned focus, not meeting time.</summary>
    Focus,
}

/// <summary>
/// One occurrence of a Google Calendar event, imported read-only. Recurring events are stored one row per
/// occurrence. Whether it counts as meeting time is decided when reading (see <see cref="Domain.Meetings"/>),
/// so changing the length threshold applies to past days too.
/// </summary>
public class CalendarEvent
{
    public int Id { get; set; }
    /// <summary>iCal UID plus the occurrence start, so each occurrence of a series is its own row.</summary>
    public string ExternalId { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>UTC.</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>UTC.</summary>
    public DateTime EndedAt { get; set; }
    public bool IsAllDay { get; set; }
    public int AttendeeCount { get; set; }
    public bool IsRecurring { get; set; }
    /// <summary>Marked "free" in the calendar (transparent), or declined by the calendar owner.</summary>
    public bool IsFree { get; set; }
    public MeetingType MeetingType { get; set; } = MeetingType.AdHoc;
    /// <summary>Set by the first matching <see cref="MeetingRule"/>.</summary>
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public DateTime SyncedAt { get; set; }
}
