namespace Waypoint.Api.Integrations;

// Phase 2+ hooks. Nothing here is implemented or registered in Phase 1.
//
// - Google Calendar import: an ICalendarImportSource would turn meetings into proposed time entries
//   (TimeEntry.Kind = Task, project picked by the user) on the daily review page, filling gaps.
// - LLM features (e.g. "summarise my week", suggested project for a gap) would read through the same
//   services the API uses (ReviewService, TimeEntryService) rather than the database directly.

/// <summary>A calendar event proposed for import as a time entry.</summary>
public sealed record CalendarEvent(string ExternalId, string Title, DateTime StartUtc, DateTime EndUtc, IReadOnlyList<string> Attendees);

/// <summary>Source of calendar events for a local day (e.g. Google Calendar). Not implemented in Phase 1.</summary>
public interface ICalendarImportSource
{
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateOnly date, CancellationToken ct = default);
}
