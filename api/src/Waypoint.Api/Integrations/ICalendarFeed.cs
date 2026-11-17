using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Integrations;

/// <summary>A read-only calendar source (Google Calendar API or an iCal URL).</summary>
public interface ICalendarFeed
{
    CalendarSourceKind Kind { get; }
    /// <summary>Stored in calendar_events.source.</summary>
    string SourceName { get; }
    bool IsConfigured(Setting settings);
    Task<IReadOnlyList<ImportedEvent>> FetchAsync(Setting settings, DateTime fromUtc, DateTime toUtc, TimeZoneInfo tz, CancellationToken ct);
}

/// <summary>Thrown when a feed can't be read; the message is shown in Settings.</summary>
public sealed class CalendarFeedException(string message, Exception? inner = null) : Exception(message, inner);
