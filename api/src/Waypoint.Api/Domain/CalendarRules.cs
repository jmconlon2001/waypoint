using System.Text.RegularExpressions;
using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Domain;

/// <summary>An event as read from Google Calendar or an iCal feed, before it is stored.</summary>
public sealed record ImportedEvent(
    string ExternalId,
    string Title,
    DateTime StartUtc,
    DateTime EndUtc,
    bool AllDay,
    IReadOnlyList<string> AttendeeEmails,
    bool Recurring,
    string EventType,
    bool Declined,
    bool Cancelled);

/// <summary>Which events count as meeting time, what type they are, and which project they belong to.</summary>
public static class CalendarRules
{
    public const string FocusTimeType = "focusTime";
    private static readonly HashSet<string> NonMeetingTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        FocusTimeType, "outOfOffice", "workingLocation", "birthday", "fromGmail",
    };

    /// <summary>
    /// Why an event does not count as meeting time, or null when it counts.
    /// Excluded: all-day, longer than <paramref name="maxMeetingMinutes"/>, declined, and non-meeting
    /// event types (focus time, out of office, working location).
    /// </summary>
    public static string? ExclusionReason(bool allDay, DateTime start, DateTime end, string? eventType, bool declined, int maxMeetingMinutes)
    {
        if (allDay) return "all-day";
        if (string.Equals(eventType, FocusTimeType, StringComparison.OrdinalIgnoreCase)) return "focus time";
        if (eventType is not null && NonMeetingTypes.Contains(eventType)) return eventType;
        if (declined) return "declined";
        if (end <= start) return "zero length";
        if (maxMeetingMinutes > 0 && (end - start).TotalMinutes > maxMeetingMinutes) return $"longer than {maxMeetingMinutes} min";
        return null;
    }

    public static bool IsCounted(bool allDay, DateTime start, DateTime end, string? eventType, bool declined, int maxMeetingMinutes) =>
        ExclusionReason(allDay, start, end, eventType, declined, maxMeetingMinutes) is null;

    public static bool IsPlannedFocus(bool allDay, string? eventType) =>
        !allDay && string.Equals(eventType, FocusTimeType, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lower-case domains of the attendee e-mails, de-duplicated.</summary>
    public static IReadOnlyList<string> Domains(IEnumerable<string> emails) =>
        emails.Select(e => e.Trim().ToLowerInvariant())
            .Select(e => e.StartsWith("mailto:", StringComparison.Ordinal) ? e[7..] : e)
            .Select(e => e.LastIndexOf('@') is var at and >= 0 ? e[(at + 1)..] : "")
            .Where(d => d.Length > 0)
            .Distinct()
            .ToList();

    public static IReadOnlyList<string> ParseDomainList(string? list) =>
        (list ?? "").Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Distinct()
            .ToList();

    /// <summary>
    /// Meeting type, first match wins: Focus (Google focus time) → External (any attendee domain not
    /// internal; only when internal domains are configured) → 1:1 (exactly 2 attendees) →
    /// Recurring (a recurring series) → Team (3+ attendees) → Ad hoc.
    /// A domain counts as internal when it equals an internal domain or is a subdomain of one.
    /// </summary>
    public static MeetingType Classify(string? eventType, int attendeeCount, IEnumerable<string> attendeeDomains, bool recurring, IReadOnlyCollection<string> internalDomains)
    {
        if (string.Equals(eventType, FocusTimeType, StringComparison.OrdinalIgnoreCase)) return MeetingType.Focus;
        if (internalDomains.Count > 0 && attendeeDomains.Any(d => !IsInternal(d, internalDomains))) return MeetingType.External;
        if (attendeeCount == 2) return MeetingType.OneOnOne;
        if (recurring) return MeetingType.Recurring;
        if (attendeeCount >= 3) return MeetingType.Team;
        return MeetingType.AdHoc;
    }

    public static bool IsInternal(string domain, IEnumerable<string> internalDomains) =>
        internalDomains.Any(i => domain.Equals(i, StringComparison.OrdinalIgnoreCase) ||
                                 domain.EndsWith("." + i, StringComparison.OrdinalIgnoreCase));

    /// <summary>Compiles a meeting-rule pattern (case-insensitive, with a timeout). Throws ArgumentException when invalid.</summary>
    public static Regex CompileRule(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Project of the first rule whose pattern matches the title, or null.</summary>
    public static int? MatchProject(string title, IEnumerable<(Regex Pattern, int ProjectId)> rules)
    {
        foreach (var (pattern, projectId) in rules)
        {
            try
            {
                if (pattern.IsMatch(title)) return projectId;
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological pattern shouldn't break the sync.
            }
        }
        return null;
    }
}
