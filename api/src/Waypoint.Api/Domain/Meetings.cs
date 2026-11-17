using System.Text.RegularExpressions;
using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Domain;

/// <summary>Meeting classification and the rules for what counts as meeting time.</summary>
public static partial class Meetings
{
    /// <summary>
    /// Assigns a meeting type. Focus Time blocks first; then external (any attendee outside the internal
    /// domains); then by size: 2 people = 1:1, 3+ recurring = team, recurring on your own = recurring,
    /// anything else = ad hoc.
    /// </summary>
    public static MeetingType Classify(string title, IReadOnlyCollection<string> attendeeEmails, bool isRecurring, IReadOnlyCollection<string> internalDomains)
    {
        if (IsFocusTime(title)) return MeetingType.Focus;

        var people = attendeeEmails.Where(e => !IsResource(e)).ToList();
        if (internalDomains.Count > 0 && people.Any(e => !IsInternal(e, internalDomains)))
            return MeetingType.External;

        return people.Count switch
        {
            2 => MeetingType.OneOnOne,
            >= 3 when isRecurring => MeetingType.Team,
            <= 1 when isRecurring => MeetingType.Recurring,
            _ => MeetingType.AdHoc,
        };
    }

    /// <summary>Google's Focus Time events are titled "Focus time" by default; also accept "Focus" / "Focus block".</summary>
    public static bool IsFocusTime(string title) => FocusTitle().IsMatch(title.Trim());

    /// <summary>
    /// Counts as meeting time: timed (not all-day), busy (not free or declined), not a Focus Time block,
    /// and no longer than the threshold. Longer events are holds, OOO or trainings.
    /// </summary>
    public static bool CountsAsMeeting(CalendarEvent e, int maxMinutes) =>
        !e.IsAllDay && !e.IsFree && e.MeetingType != MeetingType.Focus &&
        (e.EndedAt - e.StartedAt).TotalMinutes <= maxMinutes;

    /// <summary>Focus Time events count as planned focus (all-day ones don't).</summary>
    public static bool CountsAsPlannedFocus(CalendarEvent e) => !e.IsAllDay && e.MeetingType == MeetingType.Focus;

    /// <summary>The first rule whose pattern appears in the title (case-insensitive), in rule order.</summary>
    public static int? MatchProject(string title, IEnumerable<MeetingRule> rules) =>
        rules.OrderBy(r => r.Id)
            .FirstOrDefault(r => r.TitlePattern.Length > 0 && title.Contains(r.TitlePattern, StringComparison.OrdinalIgnoreCase))
            ?.ProjectId;

    public static IReadOnlyList<string> ParseDomains(string? value) =>
        (value ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.TrimStart('@').ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Distinct()
            .ToList();

    public static string? DomainOf(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        return at > 0 && at < email!.Length - 1 ? email[(at + 1)..].ToLowerInvariant() : null;
    }

    private static bool IsInternal(string email, IReadOnlyCollection<string> domains)
    {
        var domain = DomainOf(email);
        return domain is null || domains.Any(d => domain == d || domain.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>Rooms and group calendars show up as attendees but aren't people.</summary>
    private static bool IsResource(string email) =>
        email.EndsWith("resource.calendar.google.com", StringComparison.OrdinalIgnoreCase) ||
        email.EndsWith("group.calendar.google.com", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^focus( time| block)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex FocusTitle();
}
