using Microsoft.Extensions.Options;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Integrations;

/// <summary>Reads the "secret address in iCal format" from config (Calendar:ICalUrl).</summary>
public sealed class ICalUrlFeed(HttpClient http, IOptions<CalendarOptions> options) : ICalendarFeed
{
    public CalendarSourceKind Kind => CalendarSourceKind.ICal;
    public string SourceName => "ical";

    public bool IsConfigured(Setting settings) => !string.IsNullOrWhiteSpace(options.Value.ICalUrl);

    public async Task<IReadOnlyList<ImportedEvent>> FetchAsync(Setting settings, DateTime fromUtc, DateTime toUtc, TimeZoneInfo tz, CancellationToken ct)
    {
        var url = options.Value.ICalUrl;
        if (string.IsNullOrWhiteSpace(url)) throw new CalendarFeedException("No iCal URL is configured (Calendar__ICalUrl).");
        string body;
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                throw new CalendarFeedException($"The iCal URL answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CalendarFeedException($"Could not download the iCal feed: {ex.Message}", ex);
        }
        if (!body.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            throw new CalendarFeedException("The iCal URL did not return an iCalendar file.");
        return ICalParser.Parse(body, fromUtc, toUtc, tz);
    }
}
