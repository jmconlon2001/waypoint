using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Integrations;

/// <summary>
/// Google Calendar, read-only (scope calendar.readonly), using the OAuth 2.0 flow for installed /
/// desktop apps with a loopback redirect and PKCE. The refresh token is stored in the settings row,
/// encrypted with ASP.NET Core Data Protection (keys on a local volume). No Google SDK is needed.
/// </summary>
public sealed class GoogleCalendarClient(HttpClient http, IOptions<GoogleOptions> options, IDataProtectionProvider dataProtection, TimeProvider clock, ILogger<GoogleCalendarClient> log)
    : ICalendarFeed
{
    public const string Scope = "https://www.googleapis.com/auth/calendar.readonly";
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string ApiBase = "https://www.googleapis.com/calendar/v3";

    // Single user: one cached access token and the pending sign-ins (state → PKCE verifier) in memory.
    private static readonly ConcurrentDictionary<string, PendingAuth> Pending = new();
    private static (string Token, DateTime ExpiresUtc, string RefreshHash)? _accessToken;

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Waypoint.Google.RefreshToken.v1");

    public CalendarSourceKind Kind => CalendarSourceKind.Google;
    public string SourceName => "google";
    public bool ClientConfigured => options.Value.IsConfigured;

    public bool IsConfigured(Setting settings) => options.Value.IsConfigured && settings.GoogleRefreshTokenProtected is not null;

    private sealed record PendingAuth(string Verifier, string RedirectUri, string ReturnOrigin, DateTime ExpiresUtc);

    /// <summary>Starts sign-in: returns the Google consent URL. <paramref name="origin"/> is where the browser runs the app.</summary>
    public string BeginAuthorization(string origin)
    {
        if (!options.Value.IsConfigured) throw new InvalidOperationException("Google OAuth client is not configured (Google__ClientId / Google__ClientSecret).");
        foreach (var (key, p) in Pending)
            if (p.ExpiresUtc < clock.GetUtcNow().UtcDateTime) Pending.TryRemove(key, out _);

        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var redirect = options.Value.RedirectUri is { Length: > 0 } fixedUri ? fixedUri : origin.TrimEnd('/') + "/api/calendar/google/callback";
        Pending[state] = new PendingAuth(verifier, redirect, origin.TrimEnd('/'), clock.GetUtcNow().UtcDateTime.AddMinutes(15));

        var query = new Dictionary<string, string>
        {
            ["client_id"] = options.Value.ClientId!,
            ["redirect_uri"] = redirect,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        return AuthEndpoint + "?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    /// <summary>Finishes sign-in: exchanges the code, stores the encrypted refresh token, returns the origin to send the browser back to.</summary>
    public async Task<string> CompleteAuthorizationAsync(Setting settings, string code, string state, CancellationToken ct)
    {
        if (!Pending.TryRemove(state, out var pending) || pending.ExpiresUtc < clock.GetUtcNow().UtcDateTime)
            throw new CalendarFeedException("The Google sign-in expired or didn't start here. Try Connect again.");

        var json = await PostTokenAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = options.Value.ClientId!,
            ["client_secret"] = options.Value.ClientSecret!,
            ["redirect_uri"] = pending.RedirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = pending.Verifier,
        }, ct);

        var refresh = json.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(refresh))
            throw new CalendarFeedException("Google didn't return a refresh token. Remove Waypoint's access at myaccount.google.com/permissions and connect again.");

        settings.GoogleRefreshTokenProtected = _protector.Protect(refresh);
        CacheAccessToken(json, refresh);
        settings.GoogleAccount = await GetAccountAsync(_accessToken!.Value.Token, ct);
        return pending.ReturnOrigin;
    }

    /// <summary>Forgets the token locally and asks Google to revoke it (best effort).</summary>
    public async Task DisconnectAsync(Setting settings, CancellationToken ct)
    {
        var refresh = Unprotect(settings);
        settings.GoogleRefreshTokenProtected = null;
        settings.GoogleAccount = null;
        _accessToken = null;
        if (refresh is null) return;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh });
            await http.PostAsync("https://oauth2.googleapis.com/revoke", content, ct);
        }
        catch (Exception ex)
        {
            log.LogInformation("Google token revoke failed (ignored): {Message}", ex.Message);
        }
    }

    public async Task<IReadOnlyList<ImportedEvent>> FetchAsync(Setting settings, DateTime fromUtc, DateTime toUtc, TimeZoneInfo tz, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(settings, ct);
        var events = new List<ImportedEvent>();
        string? pageToken = null;
        var pages = 0;
        do
        {
            var url = $"{ApiBase}/calendars/primary/events?singleEvents=true&orderBy=startTime&showDeleted=false&maxResults=2500" +
                      $"&timeMin={Uri.EscapeDataString(fromUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}" +
                      $"&timeMax={Uri.EscapeDataString(toUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}" +
                      (pageToken is null ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new CalendarFeedException($"Google Calendar answered {(int)response.StatusCode}: {ErrorText(body)}");
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("items", out var items))
                events.AddRange(GoogleEventParser.ParseItems(items, tz));
            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (pageToken is not null && ++pages < 20);
        return events;
    }

    private async Task<string> GetAccessTokenAsync(Setting settings, CancellationToken ct)
    {
        var refresh = Unprotect(settings) ?? throw new CalendarFeedException("Google Calendar isn't connected. Use Connect in Settings.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refresh)));
        if (_accessToken is { } cached && cached.RefreshHash == hash && cached.ExpiresUtc > clock.GetUtcNow().UtcDateTime.AddMinutes(2))
            return cached.Token;

        var json = await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = options.Value.ClientId ?? "",
            ["client_secret"] = options.Value.ClientSecret ?? "",
            ["refresh_token"] = refresh,
            ["grant_type"] = "refresh_token",
        }, ct);
        CacheAccessToken(json, refresh);
        return _accessToken!.Value.Token;
    }

    private void CacheAccessToken(JsonElement json, string refresh)
    {
        var access = json.GetProperty("access_token").GetString()!;
        var expires = json.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 3600;
        _accessToken = (access, clock.GetUtcNow().UtcDateTime.AddSeconds(expires), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refresh))));
    }

    private async Task<JsonElement> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            if (body.Contains("invalid_grant", StringComparison.Ordinal))
                throw new CalendarFeedException("Google access was revoked or expired. Connect Google Calendar again in Settings.");
            throw new CalendarFeedException($"Google sign-in failed ({(int)response.StatusCode}): {ErrorText(body)}");
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<string?> GetAccountAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/calendars/primary");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private string? Unprotect(Setting settings)
    {
        if (settings.GoogleRefreshTokenProtected is null) return null;
        try
        {
            return _protector.Unprotect(settings.GoogleRefreshTokenProtected);
        }
        catch (CryptographicException)
        {
            throw new CalendarFeedException("The stored Google token can't be decrypted (the data-protection keys changed). Connect Google Calendar again.");
        }
    }

    private static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error_description", out var d)) return d.GetString() ?? body;
            if (root.TryGetProperty("error", out var e))
                return e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() ?? body : e.ToString();
        }
        catch (JsonException)
        {
        }
        return body.Length > 200 ? body[..200] : body;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
