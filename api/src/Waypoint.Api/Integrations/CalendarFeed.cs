namespace Waypoint.Api.Integrations;

/// <summary>Downloads the iCalendar feed. Swappable in tests (and for a Google API source later).</summary>
public interface ICalendarFeed
{
    Task<string> DownloadAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// The only outbound call Waypoint makes (NFR-1): a read-only GET of the calendar's secret iCal address.
/// Restricted to HTTPS Google Calendar hosts so the feature can't be pointed anywhere else.
/// </summary>
public sealed class HttpCalendarFeed(HttpClient http) : ICalendarFeed
{
    public const long MaxBytes = 20 * 1024 * 1024;

    public static bool IsAllowedUrl(string? url, out string? problem)
    {
        problem = null;
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
            problem = "That isn't a valid web address.";
        else if (uri.Scheme != Uri.UriSchemeHttps)
            problem = "The calendar address must start with https://.";
        else if (!(uri.Host.Equals("calendar.google.com", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.EndsWith(".calendar.google.com", StringComparison.OrdinalIgnoreCase)))
            problem = "Use the \"Secret address in iCal format\" from Google Calendar (calendar.google.com).";
        return problem is null;
    }

    public async Task<string> DownloadAsync(string url, CancellationToken ct = default)
    {
        if (!IsAllowedUrl(url, out var problem)) throw new InvalidOperationException(problem);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google Calendar answered {(int)response.StatusCode} {response.ReasonPhrase}. Check the secret address.");
        if (response.Content.Headers.ContentLength > MaxBytes)
            throw new HttpRequestException("The calendar feed is too large.");
        return await response.Content.ReadAsStringAsync(ct);
    }
}
