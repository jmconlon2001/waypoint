using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>Time entries added or edited after the fact (the timer itself lives in <see cref="TimerService"/>).</summary>
public sealed class TimeEntryService(WaypointDbContext db, SettingsService settings, TimeProvider clock)
{
    public async Task<List<TimeEntryDto>> ListAsync(DateOnly? date, CancellationToken ct = default)
    {
        var tz = await settings.TimeZoneAsync(ct);
        var now = Now();
        var day = LocalTime.DayBoundsUtc(date ?? LocalTime.LocalDate(now, tz), tz);
        var items = await Query()
            .Where(e => e.StartedAt < day.End && (e.EndedAt == null || e.EndedAt > day.Start))
            .OrderBy(e => e.StartedAt)
            .ToListAsync(ct);
        return items.Select(e => e.ToDto(now)).ToList();
    }

    public async Task<TimeEntryDto> CreateAsync(TimeEntryInput input, CancellationToken ct = default)
    {
        var entry = new TimeEntry();
        await ApplyAsync(entry, input, ct);
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return await GetAsync(entry.Id, ct);
    }

    public async Task<TimeEntryDto> UpdateAsync(int id, TimeEntryInput input, CancellationToken ct = default)
    {
        var entry = await db.TimeEntries.FindAsync([id], ct) ?? throw ApiException.NotFound("Time entry");
        if (entry.EndedAt is null)
            throw ApiException.BadRequest("Stop the running timer before editing it.");
        await ApplyAsync(entry, input, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entry = await db.TimeEntries.FindAsync([id], ct) ?? throw ApiException.NotFound("Time entry");
        // Interruptions that paused this entry keep their own record; just drop the link.
        var paused = await db.Interruptions.Where(i => i.PausedTimeEntryId == id).ToListAsync(ct);
        foreach (var i in paused) i.PausedTimeEntryId = null;
        db.TimeEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Fills a gap from the daily review in one step.</summary>
    public Task<TimeEntryDto> FillGapAsync(GapFillRequest request, CancellationToken ct = default) =>
        CreateAsync(new TimeEntryInput(null, request.Kind == TimeEntryKind.Untracked ? null : request.ProjectId,
            request.Kind, request.Start, request.End, request.Note), ct);

    private async Task<TimeEntryDto> GetAsync(int id, CancellationToken ct) =>
        (await Query().FirstAsync(e => e.Id == id, ct)).ToDto(Now());

    private async Task ApplyAsync(TimeEntry entry, TimeEntryInput input, CancellationToken ct)
    {
        var start = Mapping.TruncateToSecond(Mapping.Utc(input.StartedAt));
        var end = Mapping.TruncateToSecond(Mapping.Utc(input.EndedAt));
        if (end <= start) throw ApiException.BadRequest("End must be after start.");
        if (end - start > TimeSpan.FromHours(24)) throw ApiException.BadRequest("A time entry can't be longer than 24 hours.");
        if (end > Now().AddMinutes(1)) throw ApiException.BadRequest("Time entries can't end in the future.");

        int? projectId = input.ProjectId;
        if (input.TaskId is not null)
        {
            var task = await db.Tasks.FindAsync([input.TaskId.Value], ct) ?? throw ApiException.BadRequest("Unknown task.");
            projectId ??= task.ProjectId;
        }
        if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw ApiException.BadRequest("Unknown project.");
        if (input.Kind != TimeEntryKind.Untracked && input.TaskId is null && projectId is null)
            throw ApiException.BadRequest("Pick a task or a project (or mark the time as untracked).");

        entry.TaskId = input.TaskId;
        entry.ProjectId = projectId;
        entry.Kind = input.Kind;
        entry.StartedAt = start;
        entry.EndedAt = end;
        entry.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
    }

    private IQueryable<TimeEntry> Query() => db.TimeEntries.Include(e => e.Task).Include(e => e.Project);

    private DateTime Now() => Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);
}
