namespace Waypoint.Api.Integrations;

/// <summary>Config section "Calendar" (env: Calendar__ICalUrl, Calendar__SyncIntervalMinutes, …).</summary>
public sealed class CalendarOptions
{
    /// <summary>Google Calendar "Secret address in iCal format" (fallback source). Treat it like a password.</summary>
    public string? ICalUrl { get; set; }
    public int SyncIntervalMinutes { get; set; } = 15;
    public int SyncDaysBack { get; set; } = 35;
    public int SyncDaysForward { get; set; } = 14;
}

/// <summary>Config section "Google" (env: Google__ClientId, Google__ClientSecret). Desktop-app OAuth client.</summary>
public sealed class GoogleOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>Optional fixed redirect URI. By default it is built from the browser's origin + /api/calendar/google/callback.</summary>
    public string? RedirectUri { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
