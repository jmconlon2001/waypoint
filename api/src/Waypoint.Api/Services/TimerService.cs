using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>
/// The single running timer. A running timer is a <see cref="TimeEntry"/> with EndedAt == null, so it
/// survives browser restarts. Starting a timer always stops the current one.
/// </summary>
public sealed class TimerService(WaypointDbContext db, SettingsService settings, TimeProvider clock)
{
    public async Task<TimerStateDto> GetStateAsync(CancellationToken ct = default)
    {
        var now = Now();
        var setting = await settings.GetAsync(ct);
        var tz = LocalTime.FindTimeZone(setting.TimeZone);

        var running = await RunningQuery().FirstOrDefaultAsync(ct);
        var warnings = running is null
            ? Array.Empty<string>()
            : TimerWarnings.For(running.StartedAt, now, setting.TimerWarningMinutes, tz);

        var open = await db.Interruptions
            .Include(i => i.Person)
            .Include(i => i.PausedTimeEntry).ThenInclude(e => e!.Task)
            .Where(i => i.EndedAt == null)
            .OrderByDescending(i => i.StartedAt)
            .FirstOrDefaultAsync(ct);

        var last = await db.TimeEntries
            .Where(e => e.TaskId != null && e.Task!.Status != WorkTaskStatus.Done)
            .OrderByDescending(e => e.StartedAt)
            .Select(e => new TaskRef(e.Task!.Id, e.Task.Title))
            .FirstOrDefaultAsync(ct);

        var pinned = await db.Tasks.Include(t => t.Project)
            .Where(t => t.PinSlot != null && t.Status != WorkTaskStatus.Done)
            .OrderBy(t => t.PinSlot)
            .Select(t => new PinnedTaskDto(t.PinSlot!.Value, t.Id, t.Title, t.Project != null ? t.Project.Color : null))
            .ToListAsync(ct);

        return new TimerStateDto(running?.ToDto(now), warnings, open?.ToDto(now), last, pinned, setting.TimerWarningMinutes, now);
    }

    public async Task<TimerStateDto> StartAsync(StartTimerRequest request, CancellationToken ct = default)
    {
        var now = Now();
        WorkTask? task = null;
        int? projectId = request.ProjectId;
        var kind = request.Kind ?? TimeEntryKind.Task;

        if (request.TaskId is not null)
        {
            task = await db.Tasks.FindAsync([request.TaskId.Value], ct) ?? throw ApiException.NotFound("Task");
            projectId ??= task.ProjectId;
        }
        else if (projectId is null && kind != TimeEntryKind.Untracked)
        {
            throw ApiException.BadRequest("Pick a task or a project to start a timer.");
        }
        if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw ApiException.BadRequest("Unknown project.");

        await StopRunningAsync(now, ct);
        await CloseOpenInterruptionsAsync(now, ct);

        var entry = new TimeEntry
        {
            TaskId = task?.Id,
            ProjectId = projectId,
            Kind = kind,
            StartedAt = now,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };
        db.TimeEntries.Add(entry);

        if (task is not null)
        {
            if (task.Status != WorkTaskStatus.Doing)
            {
                task.Status = WorkTaskStatus.Doing;
                task.CompletedAt = null;
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetStateAsync(ct);
    }

    /// <summary>Starts the task in pin slot 1-5.</summary>
    public async Task<TimerStateDto> StartPinnedAsync(int slot, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.PinSlot == slot && t.Status != WorkTaskStatus.Done, ct)
                   ?? throw ApiException.NotFound($"Pinned task {slot}");
        return await StartAsync(new StartTimerRequest(task.Id, null, null, null), ct);
    }

    public async Task<TimerStateDto> StopAsync(CancellationToken ct = default)
    {
        await StopRunningAsync(Now(), ct);
        await db.SaveChangesAsync(ct);
        return await GetStateAsync(ct);
    }

    /// <summary>Ends the running entry (if any) at <paramref name="at"/>. Does not save. Returns the stopped entry.</summary>
    internal async Task<TimeEntry?> StopRunningAsync(DateTime at, CancellationToken ct)
    {
        var running = await db.TimeEntries.Include(e => e.Task).Where(e => e.EndedAt == null).ToListAsync(ct);
        TimeEntry? latest = null;
        foreach (var e in running)
        {
            e.EndedAt = at > e.StartedAt ? at : e.StartedAt;
            if (e.Task is { Status: WorkTaskStatus.Doing }) e.Task.Status = WorkTaskStatus.Today;
            if (latest is null || e.StartedAt > latest.StartedAt) latest = e;
        }
        return latest;
    }

    private async Task CloseOpenInterruptionsAsync(DateTime at, CancellationToken ct)
    {
        var open = await db.Interruptions.Where(i => i.EndedAt == null).ToListAsync(ct);
        foreach (var i in open) i.EndedAt = at > i.StartedAt ? at : i.StartedAt;
    }

    private IQueryable<TimeEntry> RunningQuery() => db.TimeEntries
        .Include(e => e.Task)
        .Include(e => e.Project)
        .Where(e => e.EndedAt == null)
        .OrderByDescending(e => e.StartedAt);

    private DateTime Now() => Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);
}
