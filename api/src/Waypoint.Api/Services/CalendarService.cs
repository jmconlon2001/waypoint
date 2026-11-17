using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;
using Waypoint.Api.Integrations;

namespace Waypoint.Api.Services;

/// <summary>
/// Read-only Google Calendar import (FR-C1..C4) from the calendar's secret iCal address. Each sync replaces
/// the events in a rolling window (5 weeks back, 2 weeks ahead); older events are kept for the metrics.
/// </summary>
public sealed class CalendarService(WaypointDbContext db, SettingsService settings, ICalendarFeed feed, TimeProvider clock, ILogger<CalendarService> logger)
{
    public const int SyncIntervalMinutes = 15;
    public static readonly TimeSpan SyncBack = TimeSpan.FromDays(35);
    public static readonly TimeSpan SyncAhead = TimeSpan.FromDays(14);

    public async Task<CalendarStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        return new CalendarStatusDto(
            !string.IsNullOrWhiteSpace(s.CalendarIcsUrl),
            Hint(s.CalendarIcsUrl),
            s.CalendarInternalDomains,
            s.CalendarLastSyncAt,
            s.CalendarLastSyncError,
            await db.CalendarEvents.CountAsync(ct),
            SyncIntervalMinutes);
    }

    public async Task<CalendarStatusDto> SetSourceAsync(CalendarSourceInput input, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        if (input.IcsUrl is not null)
        {
            var url = input.IcsUrl.Trim();
            if (url.Length == 0)
            {
                s.CalendarIcsUrl = null;
                s.CalendarLastSyncError = null;
            }
            else
            {
                if (!HttpCalendarFeed.IsAllowedUrl(url, out var problem)) throw ApiException.BadRequest(problem!);
                if (url.Length > 2048) throw ApiException.BadRequest("That address is too long.");
                if (url != s.CalendarIcsUrl) s.CalendarLastSyncError = null;
                s.CalendarIcsUrl = url;
            }
        }
        var domains = Meetings.ParseDomains(input.InternalDomains);
        s.CalendarInternalDomains = domains.Count == 0 ? null : string.Join(", ", domains);
        await db.SaveChangesAsync(ct);
        return await GetStatusAsync(ct);
    }

    /// <summary>Downloads the feed and replaces the events in the sync window. Errors are saved on the settings row.</summary>
    public async Task<SyncResultDto> SyncAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(s.CalendarIcsUrl))
            throw ApiException.BadRequest("Add your calendar's secret iCal address first.");

        var now = Now();
        var from = now - SyncBack;
        var to = now + SyncAhead;
        ParsedCalendar parsed;
        try
        {
            var ics = await feed.DownloadAsync(s.CalendarIcsUrl, ct);
            parsed = IcsParser.Parse(ics, from, to);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Calendar sync failed: {Message}", ex.Message);
            s.CalendarLastSyncError = Truncate(ex is HttpRequestException or InvalidOperationException or FormatException
                ? ex.Message
                : $"Couldn't read the calendar ({ex.GetType().Name}).", 1000);
            await db.SaveChangesAsync(CancellationToken.None);
            throw ApiException.BadGateway(s.CalendarLastSyncError);
        }

        var result = await ApplyAsync(parsed, from, to, now, s, ct);
        s.CalendarLastSyncAt = now;
        s.CalendarLastSyncError = null;
        await db.SaveChangesAsync(ct);
        return result with { Status = await GetStatusAsync(ct) };
    }

    internal async Task<SyncResultDto> ApplyAsync(ParsedCalendar parsed, DateTime from, DateTime to, DateTime now, Setting s, CancellationToken ct)
    {
        var domains = Meetings.ParseDomains(s.CalendarInternalDomains);
        if (domains.Count == 0 && Meetings.DomainOf(parsed.OwnerEmail) is { } ownerDomain) domains = [ownerDomain];
        var rules = await db.MeetingRules.ToListAsync(ct);

        var existing = await db.CalendarEvents
            .Where(e => e.StartedAt < to && e.EndedAt > from)
            .ToDictionaryAsync(e => e.ExternalId, ct);
        var seen = new HashSet<string>();
        int added = 0, updated = 0;

        foreach (var ev in parsed.Events)
        {
            seen.Add(ev.ExternalId);
            if (!existing.TryGetValue(ev.ExternalId, out var row))
            {
                // Might sit just outside the window query (e.g. it started earlier); look it up directly.
                row = await db.CalendarEvents.FirstOrDefaultAsync(e => e.ExternalId == ev.ExternalId, ct);
                if (row is null)
                {
                    row = new CalendarEvent { ExternalId = ev.ExternalId };
                    db.CalendarEvents.Add(row);
                    added++;
                }
                else updated++;
            }
            else updated++;

            var people = ev.AttendeeEmails;
            row.Title = Truncate(ev.Title, 500);
            row.StartedAt = Mapping.TruncateToSecond(ev.StartUtc);
            row.EndedAt = Mapping.TruncateToSecond(ev.EndUtc);
            row.IsAllDay = ev.IsAllDay;
            row.IsRecurring = ev.IsRecurring;
            row.IsFree = ev.IsFree;
            row.AttendeeCount = people.Count;
            row.MeetingType = Meetings.Classify(ev.Title, people, ev.IsRecurring, domains);
            row.ProjectId = Meetings.MatchProject(ev.Title, rules);
            row.SyncedAt = now;
        }

        var removed = existing.Values.Where(e => !seen.Contains(e.ExternalId)).ToList();
        db.CalendarEvents.RemoveRange(removed);
        return new SyncResultDto(added, updated, removed.Count, parsed.Events.Count, now, null!);
    }

    public async Task<List<CalendarEventDto>> ListAsync(DateOnly? date, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var tz = LocalTime.FindTimeZone(s.TimeZone);
        var day = LocalTime.DayBoundsUtc(date ?? LocalTime.LocalDate(Now(), tz), tz);
        var events = await db.CalendarEvents.Include(e => e.Project)
            .Where(e => e.StartedAt < day.End && e.EndedAt > day.Start)
            .OrderBy(e => e.StartedAt)
            .ToListAsync(ct);
        return events.Select(e => ToDto(e, s.MeetingMaxMinutes)).ToList();
    }

    // ---- Meeting rules (FR-C3) ----

    public async Task<List<MeetingRuleDto>> ListRulesAsync(CancellationToken ct = default)
    {
        var rules = await db.MeetingRules.Include(r => r.Project).OrderBy(r => r.Id).ToListAsync(ct);
        var counts = await db.CalendarEvents.Where(e => e.ProjectId != null).Select(e => new { e.Title, e.ProjectId }).ToListAsync(ct);
        return rules.Select(r => new MeetingRuleDto(
            r.Id, r.TitlePattern, r.ProjectId, r.Project?.Name ?? "", r.Project?.Color ?? "",
            counts.Count(c => c.ProjectId == r.ProjectId && c.Title.Contains(r.TitlePattern, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    public async Task<List<MeetingRuleDto>> CreateRuleAsync(MeetingRuleInput input, CancellationToken ct = default)
    {
        var rule = new MeetingRule();
        await ApplyRuleAsync(rule, input, ct);
        db.MeetingRules.Add(rule);
        await db.SaveChangesAsync(ct);
        await ReapplyRulesAsync(ct);
        return await ListRulesAsync(ct);
    }

    public async Task<List<MeetingRuleDto>> UpdateRuleAsync(int id, MeetingRuleInput input, CancellationToken ct = default)
    {
        var rule = await db.MeetingRules.FindAsync([id], ct) ?? throw ApiException.NotFound("Meeting rule");
        await ApplyRuleAsync(rule, input, ct);
        await db.SaveChangesAsync(ct);
        await ReapplyRulesAsync(ct);
        return await ListRulesAsync(ct);
    }

    public async Task<List<MeetingRuleDto>> DeleteRuleAsync(int id, CancellationToken ct = default)
    {
        var rule = await db.MeetingRules.FindAsync([id], ct) ?? throw ApiException.NotFound("Meeting rule");
        db.MeetingRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        await ReapplyRulesAsync(ct);
        return await ListRulesAsync(ct);
    }

    /// <summary>Re-maps every stored event, so a new rule also applies to past meetings.</summary>
    private async Task ReapplyRulesAsync(CancellationToken ct)
    {
        var rules = await db.MeetingRules.ToListAsync(ct);
        foreach (var e in await db.CalendarEvents.ToListAsync(ct))
            e.ProjectId = Meetings.MatchProject(e.Title, rules);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyRuleAsync(MeetingRule rule, MeetingRuleInput input, CancellationToken ct)
    {
        var pattern = input.TitlePattern?.Trim() ?? "";
        if (pattern.Length == 0) throw ApiException.BadRequest("Type the text to look for in meeting titles.");
        if (pattern.Length > 200) throw ApiException.BadRequest("The pattern is too long (200 characters max).");
        if (!await db.Projects.AnyAsync(p => p.Id == input.ProjectId, ct)) throw ApiException.BadRequest("Unknown project.");
        rule.TitlePattern = pattern;
        rule.ProjectId = input.ProjectId;
    }

    internal static CalendarEventDto ToDto(CalendarEvent e, int maxMinutes)
    {
        var counts = Meetings.CountsAsMeeting(e, maxMinutes);
        var focus = Meetings.CountsAsPlannedFocus(e);
        string? reason = counts || focus ? null
            : e.IsAllDay ? "All-day"
            : e.IsFree ? "Free or declined"
            : e.MeetingType == MeetingType.Focus ? "Focus time"
            : $"Longer than {maxMinutes} min";
        return new CalendarEventDto(
            e.Id, e.Title, e.StartedAt, e.EndedAt, e.IsAllDay, e.AttendeeCount, e.IsRecurring, e.IsFree, e.MeetingType,
            e.ProjectId, e.Project?.Name, e.Project?.Color, counts, focus, reason, Mapping.Minutes(e.EndedAt - e.StartedAt));
    }

    /// <summary>The secret address is never sent back; just enough to recognise it.</summary>
    private static string? Hint(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var tail = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Host}/…{(tail.Length > 8 ? tail[^8..] : "")}";
    }

    private DateTime Now() => Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
