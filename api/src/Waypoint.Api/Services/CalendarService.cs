using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;
using Waypoint.Api.Integrations;

namespace Waypoint.Api.Services;

/// <summary>Calendar import (Google or iCal), classification, meeting rules and the event list.</summary>
public sealed class CalendarService(
    WaypointDbContext db,
    SettingsService settings,
    IEnumerable<ICalendarFeed> feeds,
    IOptions<CalendarOptions> options,
    TimeProvider clock,
    ILogger<CalendarService> log)
{
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    public async Task<CalendarStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var google = feeds.OfType<GoogleCalendarClient>().FirstOrDefault();
        var ical = feeds.FirstOrDefault(f => f.Kind == CalendarSourceKind.ICal);
        var count = await db.CalendarEvents.CountAsync(ct);
        return new CalendarStatusDto(
            s.CalendarSource,
            google?.ClientConfigured ?? false,
            s.GoogleRefreshTokenProtected is not null,
            s.GoogleAccount,
            ical?.IsConfigured(s) ?? false,
            s.CalendarLastSyncAt,
            s.CalendarLastSyncError,
            count,
            options.Value.SyncIntervalMinutes);
    }

    /// <summary>Imports events for [today - SyncDaysBack, today + SyncDaysForward) from the selected source.</summary>
    public async Task<SyncResultDto> SyncAsync(CancellationToken ct = default)
    {
        await SyncLock.WaitAsync(ct);
        try
        {
            var s = await settings.GetAsync(ct);
            var now = Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);
            if (s.CalendarSource == CalendarSourceKind.None)
                throw ApiException.BadRequest("Pick a calendar source (Google or iCal) in Settings first.");
            var feed = feeds.FirstOrDefault(f => f.Kind == s.CalendarSource)
                       ?? throw ApiException.BadRequest($"The {s.CalendarSource} calendar source isn't available.");

            var tz = LocalTime.FindTimeZone(s.TimeZone);
            var today = LocalTime.LocalDate(now, tz);
            var from = LocalTime.ToUtc(today.AddDays(-Math.Max(1, options.Value.SyncDaysBack)), TimeOnly.MinValue, tz);
            var to = LocalTime.ToUtc(today.AddDays(Math.Max(1, options.Value.SyncDaysForward)), TimeOnly.MinValue, tz);

            IReadOnlyList<ImportedEvent> imported;
            try
            {
                if (!feed.IsConfigured(s)) throw new CalendarFeedException(s.CalendarSource == CalendarSourceKind.Google
                    ? "Google Calendar isn't connected. Use Connect in Settings."
                    : "No iCal URL is configured (Calendar__ICalUrl).");
                imported = await feed.FetchAsync(s, from, to, tz, ct);
            }
            catch (CalendarFeedException ex)
            {
                s.CalendarLastSyncError = ex.Message;
                await db.SaveChangesAsync(ct);
                throw ApiException.BadRequest(ex.Message);
            }

            var result = await ApplyAsync(feed.SourceName, imported, from, to, s, now, ct);
            s.CalendarLastSyncAt = now;
            s.CalendarLastSyncError = null;
            await db.SaveChangesAsync(ct);
            log.LogInformation("Calendar sync ({Source}): {Added} added, {Updated} updated, {Removed} removed", feed.SourceName, result.Added, result.Updated, result.Removed);
            return result with { Source = s.CalendarSource };
        }
        finally
        {
            SyncLock.Release();
        }
    }

    /// <summary>Upserts imported events by (source, external id) and removes ones that disappeared from the window.</summary>
    internal async Task<SyncResultDto> ApplyAsync(string source, IReadOnlyList<ImportedEvent> imported, DateTime fromUtc, DateTime toUtc, Setting s, DateTime now, CancellationToken ct)
    {
        var internalDomains = CalendarRules.ParseDomainList(s.InternalDomains);
        var rules = await CompiledRulesAsync(ct);

        // Events from another source (the user switched) are dropped.
        var stale = await db.CalendarEvents.Where(e => e.Source != source).ToListAsync(ct);
        db.CalendarEvents.RemoveRange(stale);

        var existing = await db.CalendarEvents
            .Where(e => e.Source == source && e.StartedAt < toUtc && e.EndedAt > fromUtc)
            .ToDictionaryAsync(e => e.ExternalId, ct);

        int added = 0, updated = 0;
        var seen = new HashSet<string>();
        foreach (var i in imported)
        {
            if (i.Cancelled || i.ExternalId.Length == 0 || !seen.Add(i.ExternalId)) continue;
            if (!existing.TryGetValue(i.ExternalId, out var e))
            {
                e = await db.CalendarEvents.FirstOrDefaultAsync(x => x.Source == source && x.ExternalId == i.ExternalId, ct);
                if (e is null)
                {
                    e = new CalendarEvent { Source = source, ExternalId = Truncate(i.ExternalId, 512) };
                    db.CalendarEvents.Add(e);
                    added++;
                }
                else
                {
                    updated++;
                }
            }
            else
            {
                updated++;
            }

            var domains = CalendarRules.Domains(i.AttendeeEmails);
            e.Title = Truncate(i.Title, 500);
            e.StartedAt = Mapping.TruncateToSecond(i.StartUtc);
            e.EndedAt = Mapping.TruncateToSecond(i.EndUtc);
            e.AllDay = i.AllDay;
            e.AttendeeCount = i.AttendeeEmails.Count;
            e.Recurring = i.Recurring;
            e.EventType = Truncate(i.EventType, 32);
            e.AttendeeDomains = Truncate(string.Join(",", domains), 1000);
            e.Declined = i.Declined;
            e.MeetingType = CalendarRules.Classify(i.EventType, e.AttendeeCount, domains, i.Recurring, internalDomains);
            e.ProjectId = CalendarRules.MatchProject(e.Title, rules);
            e.SyncedAt = now;
        }

        var removed = 0;
        foreach (var (externalId, e) in existing)
        {
            if (seen.Contains(externalId)) continue;
            db.CalendarEvents.Remove(e);
            removed++;
        }

        await db.SaveChangesAsync(ct);
        return new SyncResultDto(s.CalendarSource, added, updated, removed + stale.Count, seen.Count, now);
    }

    /// <summary>Re-applies meeting types and rules to stored events (after settings or rules change).</summary>
    public async Task ReclassifyAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var internalDomains = CalendarRules.ParseDomainList(s.InternalDomains);
        var rules = await CompiledRulesAsync(ct);
        foreach (var e in await db.CalendarEvents.ToListAsync(ct))
        {
            var domains = e.AttendeeDomains.Split(',', StringSplitOptions.RemoveEmptyEntries);
            e.MeetingType = CalendarRules.Classify(e.EventType, e.AttendeeCount, domains, e.Recurring, internalDomains);
            e.ProjectId = CalendarRules.MatchProject(e.Title, rules);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<CalendarEventDto>> ListAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var tz = LocalTime.FindTimeZone(s.TimeZone);
        var start = LocalTime.ToUtc(from, TimeOnly.MinValue, tz);
        var end = LocalTime.ToUtc(to.AddDays(1), TimeOnly.MinValue, tz);
        var events = await db.CalendarEvents.Include(e => e.Project)
            .Where(e => e.StartedAt < end && e.EndedAt > start)
            .OrderBy(e => e.StartedAt)
            .ToListAsync(ct);
        return events.Select(e => ToDto(e, s.MaxMeetingMinutes)).ToList();
    }

    public async Task DisconnectGoogleAsync(GoogleCalendarClient google, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        await google.DisconnectAsync(s, ct);
        db.CalendarEvents.RemoveRange(await db.CalendarEvents.Where(e => e.Source == google.SourceName).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    internal static CalendarEventDto ToDto(CalendarEvent e, int maxMeetingMinutes)
    {
        var reason = CalendarRules.ExclusionReason(e.AllDay, e.StartedAt, e.EndedAt, e.EventType, e.Declined, maxMeetingMinutes);
        return new CalendarEventDto(
            e.Id, e.Source, e.Title, e.StartedAt, e.EndedAt, e.AllDay, e.AttendeeCount, e.Recurring, e.EventType, e.MeetingType,
            e.ProjectId, e.Project?.Name, e.Project?.Color,
            reason is null, reason, CalendarRules.IsPlannedFocus(e.AllDay, e.EventType),
            Mapping.Minutes(e.EndedAt - e.StartedAt));
    }

    // ---- Meeting rules ----

    public async Task<List<MeetingRuleDto>> ListRulesAsync(CancellationToken ct = default) =>
        (await db.MeetingRules.Include(r => r.Project).OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync(ct))
            .Select(ToDto).ToList();

    public async Task<MeetingRuleDto> CreateRuleAsync(MeetingRuleInput input, CancellationToken ct = default)
    {
        await ValidateRuleAsync(input, ct);
        var order = input.SortOrder ?? ((await db.MeetingRules.MaxAsync(r => (int?)r.SortOrder, ct)) ?? 0) + 10;
        var rule = new MeetingRule { Pattern = input.Pattern.Trim(), ProjectId = input.ProjectId, SortOrder = order, Active = input.Active ?? true };
        db.MeetingRules.Add(rule);
        await db.SaveChangesAsync(ct);
        await ReclassifyAsync(ct);
        return ToDto(await db.MeetingRules.Include(r => r.Project).FirstAsync(r => r.Id == rule.Id, ct));
    }

    public async Task<MeetingRuleDto> UpdateRuleAsync(int id, MeetingRuleInput input, CancellationToken ct = default)
    {
        var rule = await db.MeetingRules.FindAsync([id], ct) ?? throw ApiException.NotFound("Meeting rule");
        await ValidateRuleAsync(input, ct);
        rule.Pattern = input.Pattern.Trim();
        rule.ProjectId = input.ProjectId;
        if (input.SortOrder is not null) rule.SortOrder = input.SortOrder.Value;
        if (input.Active is not null) rule.Active = input.Active.Value;
        await db.SaveChangesAsync(ct);
        await ReclassifyAsync(ct);
        return ToDto(await db.MeetingRules.Include(r => r.Project).FirstAsync(r => r.Id == id, ct));
    }

    public async Task DeleteRuleAsync(int id, CancellationToken ct = default)
    {
        var rule = await db.MeetingRules.FindAsync([id], ct) ?? throw ApiException.NotFound("Meeting rule");
        db.MeetingRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        await ReclassifyAsync(ct);
    }

    /// <summary>Which rule (and project) a title would get.</summary>
    public async Task<RuleTestResult> TestRulesAsync(string title, CancellationToken ct = default)
    {
        var rules = await db.MeetingRules.Include(r => r.Project).Where(r => r.Active).OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync(ct);
        foreach (var r in rules)
        {
            Regex regex;
            try { regex = CalendarRules.CompileRule(r.Pattern); }
            catch (ArgumentException) { continue; }
            if (CalendarRules.MatchProject(title ?? "", [(regex, r.ProjectId)]) is not null)
                return new RuleTestResult(r.Id, r.ProjectId, r.Project?.Name);
        }
        return new RuleTestResult(null, null, null);
    }

    private async Task ValidateRuleAsync(MeetingRuleInput input, CancellationToken ct)
    {
        var pattern = input.Pattern?.Trim() ?? "";
        if (pattern.Length == 0) throw ApiException.BadRequest("Enter a title pattern (a regular expression, e.g. ^SOX|walkthrough).");
        if (pattern.Length > 300) throw ApiException.BadRequest("The pattern is too long (300 characters max).");
        try { CalendarRules.CompileRule(pattern); }
        catch (ArgumentException ex) { throw ApiException.BadRequest($"That isn't a valid regular expression: {ex.Message}"); }
        if (!await db.Projects.AnyAsync(p => p.Id == input.ProjectId, ct)) throw ApiException.BadRequest("Unknown project.");
    }

    private async Task<List<(Regex, int)>> CompiledRulesAsync(CancellationToken ct)
    {
        var list = new List<(Regex, int)>();
        foreach (var r in await db.MeetingRules.Where(r => r.Active).OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync(ct))
        {
            try { list.Add((CalendarRules.CompileRule(r.Pattern), r.ProjectId)); }
            catch (ArgumentException) { /* validated on save; skip if it somehow isn't valid */ }
        }
        return list;
    }

    private static MeetingRuleDto ToDto(MeetingRule r) => new(r.Id, r.Pattern, r.ProjectId, r.Project?.Name, r.Project?.Color, r.SortOrder, r.Active);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
