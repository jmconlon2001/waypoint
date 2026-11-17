using System.Text;
using Waypoint.Api.Contracts;
using Waypoint.Api.Integrations;
using Waypoint.Api.Services;

namespace Waypoint.Api.Endpoints;

public static class Phase2Endpoints
{
    public static IEndpointRouteBuilder MapWaypointPhase2Api(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // ---- Calendar ----
        var cal = api.MapGroup("/calendar");
        cal.MapGet("/status", (CalendarService s, CancellationToken ct) => s.GetStatusAsync(ct));
        cal.MapPost("/sync", (CalendarService s, CancellationToken ct) => s.SyncAsync(ct));
        cal.MapGet("/events", (DateOnly from, DateOnly to, CalendarService s, CancellationToken ct) => s.ListAsync(from, to, ct));

        // Google OAuth (installed-app flow with a loopback redirect + PKCE).
        cal.MapGet("/google/connect", (string? origin, HttpContext http, GoogleCalendarClient google) =>
        {
            var o = SafeOrigin(origin) ?? $"{http.Request.Scheme}://{http.Request.Host}";
            if (!google.ClientConfigured)
                return Results.Redirect($"{o}/settings?calendar=error&message={Uri.EscapeDataString("Set Google__ClientId and Google__ClientSecret first (see README).")}");
            return Results.Redirect(google.BeginAuthorization(o));
        });
        cal.MapGet("/google/callback", async (string? code, string? state, string? error, HttpContext http,
            GoogleCalendarClient google, SettingsService settings, CalendarService calendar, Data.WaypointDbContext db, CancellationToken ct) =>
        {
            var fallbackOrigin = $"{http.Request.Scheme}://{http.Request.Host}";
            if (!string.IsNullOrEmpty(error))
                return Results.Redirect($"{fallbackOrigin}/settings?calendar=error&message={Uri.EscapeDataString("Google sign-in was cancelled: " + error)}");
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return Results.BadRequest("Missing code or state.");
            try
            {
                var s = await settings.GetAsync(ct);
                var origin = await google.CompleteAuthorizationAsync(s, code, state, ct);
                s.CalendarSource = Data.Entities.CalendarSourceKind.Google;
                s.CalendarLastSyncError = null;
                await db.SaveChangesAsync(ct);
                try { await calendar.SyncAsync(ct); }
                catch (ApiException) { /* shown on the settings page via LastSyncError */ }
                return Results.Redirect($"{origin}/settings?calendar=connected");
            }
            catch (CalendarFeedException ex)
            {
                return Results.Redirect($"{fallbackOrigin}/settings?calendar=error&message={Uri.EscapeDataString(ex.Message)}");
            }
        });
        cal.MapPost("/google/disconnect", async (CalendarService s, GoogleCalendarClient google, CancellationToken ct) =>
        {
            await s.DisconnectGoogleAsync(google, ct);
            return Results.NoContent();
        });

        var rules = api.MapGroup("/meeting-rules");
        rules.MapGet("/", (CalendarService s, CancellationToken ct) => s.ListRulesAsync(ct));
        rules.MapPost("/", (MeetingRuleInput input, CalendarService s, CancellationToken ct) => s.CreateRuleAsync(input, ct));
        rules.MapPut("/{id:int}", (int id, MeetingRuleInput input, CalendarService s, CancellationToken ct) => s.UpdateRuleAsync(id, input, ct));
        rules.MapDelete("/{id:int}", async (int id, CalendarService s, CancellationToken ct) =>
        {
            await s.DeleteRuleAsync(id, ct);
            return Results.NoContent();
        });
        rules.MapPost("/test", (RuleTestRequest input, CalendarService s, CancellationToken ct) => s.TestRulesAsync(input.Title, ct));

        // ---- Tasks: end-of-day rollover ----
        api.MapGet("/tasks/rollover", (DateOnly? date, TaskService s, CancellationToken ct) => s.RolloverCandidatesAsync(date, ct));
        api.MapPost("/tasks/rollover", (RolloverRequest input, TaskService s, CancellationToken ct) => s.RolloverAsync(input, ct));

        // ---- Reports ----
        api.MapGet("/reports/weekly", (DateOnly? start, MetricsService s, CancellationToken ct) => s.WeeklyAsync(start, ct));
        api.MapGet("/reports/interruptions", (DateOnly from, DateOnly to, MetricsService s, CancellationToken ct) => s.InterruptionsAsync(from, to, ct));
        api.MapGet("/reports/planned", (DateOnly from, DateOnly to, MetricsService s, CancellationToken ct) => s.PlannedVsActualAsync(from, to, ct));
        api.MapGet("/aggregates", (DateOnly from, DateOnly to, MetricsService s, CancellationToken ct) => s.GetAggregatesAsync(from, to, ct));
        api.MapPost("/aggregates/recompute", (RecomputeRequest input, MetricsService s, CancellationToken ct) => s.RecomputeAsync(input.From, input.To, ct));

        // ---- CSV export ----
        api.MapGet("/export/{dataset}.csv", async (string dataset, DateOnly from, DateOnly to, ExportService s, CancellationToken ct) =>
        {
            var csv = await s.ExportAsync(dataset, from, to, ct);
            // UTF-8 with BOM so Excel detects the encoding.
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return Results.File(bytes, "text/csv; charset=utf-8", $"waypoint-{dataset}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        });

        return app;
    }

    /// <summary>Only loopback origins are accepted (the app is local-only); anything else is ignored.</summary>
    internal static string? SafeOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        if (!uri.IsLoopback && uri.Host != "localhost") return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
