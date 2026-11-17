using Ical.Net;
using Ical.Net.DataTypes;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace Waypoint.Api.Integrations;

/// <summary>One occurrence of a calendar event, as read from the feed (before classification).</summary>
public sealed record ImportedEvent(
    string ExternalId,
    string Title,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsAllDay,
    bool IsRecurring,
    bool IsFree,
    IReadOnlyList<string> AttendeeEmails);

public sealed record ParsedCalendar(string? OwnerEmail, IReadOnlyList<ImportedEvent> Events);

/// <summary>
/// Reads an iCalendar feed (Google Calendar's secret iCal address) and expands recurring events into
/// occurrences within a window. Cancelled events are dropped; events the owner declined, or marked
/// "free", are kept but flagged <see cref="ImportedEvent.IsFree"/>.
/// </summary>
public static class IcsParser
{
    public static ParsedCalendar Parse(string ics, DateTime fromUtc, DateTime toUtc)
    {
        var calendar = Calendar.Load(ics) ?? throw new FormatException("The calendar feed is empty or not iCalendar data.");

        // Google sets X-WR-CALNAME to the owner's address on a primary calendar.
        var calName = calendar.Properties.Get<string>("X-WR-CALNAME");
        var owner = calName is not null && calName.Contains('@') ? calName.Trim().ToLowerInvariant() : null;

        var events = new List<ImportedEvent>();
        var seen = new HashSet<string>();
        var start = new CalDateTime(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
        var end = new CalDateTime(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc));

        foreach (var occurrence in calendar.GetOccurrences<IcalEvent>(start).TakeWhileBefore(end))
        {
            if (occurrence.Source is not IcalEvent ev) continue;
            if (string.Equals(ev.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;

            var period = occurrence.Period;
            var allDay = !period.StartTime.HasTime;
            var startUtc = period.StartTime.AsUtc;
            var endUtc = period.EffectiveEndTime?.AsUtc ?? startUtc.Add(allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero);
            if (endUtc <= fromUtc || startUtc >= toUtc) continue;

            var attendees = ev.Attendees
                .Select(a => Email(a.Value))
                .Where(e => e is not null)
                .Select(e => e!)
                .Distinct()
                .ToList();
            var declined = owner is not null && ev.Attendees.Any(a =>
                Email(a.Value) == owner && string.Equals(a.ParticipationStatus, "DECLINED", StringComparison.OrdinalIgnoreCase));
            var free = string.Equals(ev.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase);

            var id = $"{ev.Uid}|{startUtc:yyyyMMddTHHmmssZ}";
            if (!seen.Add(id)) continue;
            events.Add(new ImportedEvent(
                id,
                string.IsNullOrWhiteSpace(ev.Summary) ? "(no title)" : ev.Summary.Trim(),
                DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
                DateTime.SpecifyKind(endUtc, DateTimeKind.Utc),
                allDay,
                ev.RecurrenceRule is not null || ev.RecurrenceIdentifier is not null,
                free || declined,
                attendees));
        }

        return new ParsedCalendar(owner, events);
    }

    private static string? Email(Uri? uri)
    {
        if (uri is null) return null;
        var value = uri.OriginalString;
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        value = Uri.UnescapeDataString(value).Trim().ToLowerInvariant();
        return value.Contains('@') ? value : null;
    }
}
