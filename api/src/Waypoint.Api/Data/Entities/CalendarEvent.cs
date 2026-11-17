namespace Waypoint.Api.Data.Entities;

public enum MeetingType
{
    /// <summary>Exactly two attendees.</summary>
    OneOnOne,
    /// <summary>One-off meeting with three or more attendees, all internal.</summary>
    Team,
    /// <summary>At least one attendee outside the internal domains.</summary>
    External,
    /// <summary>A recurring series with three or more attendees (or none).</summary>
    Recurring,
    /// <summary>One-off with no other attendees (a hold, a quick call).</summary>
    AdHoc,
    /// <summary>Google "focus time" block: planned focus, not a meeting.</summary>
    Focus,
}

/// <summary>An imported calendar event (read-only copy of Google Calendar / iCal).</summary>
public class CalendarEvent
{
    public int Id { get; set; }
    /// <summary>"google" or "ical".</summary>
    public string Source { get; set; } = "";
    /// <summary>Google event id (or iCal UID + occurrence start) — unique per source.</summary>
    public string ExternalId { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>UTC. For all-day events, local midnight of the first day.</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>UTC.</summary>
    public DateTime EndedAt { get; set; }
    public bool AllDay { get; set; }
    public int AttendeeCount { get; set; }
    public bool Recurring { get; set; }
    /// <summary>Google eventType: default, focusTime, outOfOffice, workingLocation…</summary>
    public string EventType { get; set; } = "default";
    /// <summary>Comma-separated, lower-case attendee e-mail domains.</summary>
    public string AttendeeDomains { get; set; } = "";
    /// <summary>The calendar owner declined (not counted as time).</summary>
    public bool Declined { get; set; }
    public MeetingType MeetingType { get; set; } = MeetingType.AdHoc;
    /// <summary>Set by the first matching <see cref="MeetingRule"/>.</summary>
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public DateTime SyncedAt { get; set; }
}
