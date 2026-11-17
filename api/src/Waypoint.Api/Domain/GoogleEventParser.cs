using System.Globalization;
using System.Text.Json;

namespace Waypoint.Api.Domain;

/// <summary>Maps Google Calendar API v3 event resources (JSON) to <see cref="ImportedEvent"/>.</summary>
public static class GoogleEventParser
{
    public static IEnumerable<ImportedEvent> ParseItems(JsonElement items, TimeZoneInfo tz)
    {
        if (items.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in items.EnumerateArray())
            if (Parse(item, tz) is { } e) yield return e;
    }

    public static ImportedEvent? Parse(JsonElement item, TimeZoneInfo tz)
    {
        var id = Str(item, "id");
        if (string.IsNullOrEmpty(id)) return null;
        var cancelled = Str(item, "status") == "cancelled";
        if (!item.TryGetProperty("start", out var start) || !item.TryGetProperty("end", out var end))
            return cancelled ? new ImportedEvent(id, "", default, default, false, [], false, "default", false, true) : null;

        var allDay = !start.TryGetProperty("dateTime", out _) && start.TryGetProperty("date", out _);
        var (s, e) = allDay
            ? (LocalMidnightUtc(Str(start, "date")!, tz), LocalMidnightUtc(Str(end, "date")!, tz))
            : (ParseDateTime(Str(start, "dateTime")!), ParseDateTime(Str(end, "dateTime")!));

        var emails = new List<string>();
        var declined = false;
        if (item.TryGetProperty("attendees", out var attendees) && attendees.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in attendees.EnumerateArray())
            {
                if (Bool(a, "resource")) continue; // meeting rooms
                var email = Str(a, "email");
                if (!string.IsNullOrEmpty(email)) emails.Add(email);
                if (Bool(a, "self") && Str(a, "responseStatus") == "declined") declined = true;
            }
        }

        var recurring = item.TryGetProperty("recurringEventId", out _) || item.TryGetProperty("recurrence", out _);
        return new ImportedEvent(
            id,
            Str(item, "summary") ?? "(no title)",
            s, e, allDay, emails, recurring,
            Str(item, "eventType") ?? "default",
            declined, cancelled);
    }

    private static DateTime ParseDateTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcDateTime;

    private static DateTime LocalMidnightUtc(string date, TimeZoneInfo tz) =>
        LocalTime.ToUtc(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeOnly.MinValue, tz);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
