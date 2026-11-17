using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>
/// "Interrupted" pauses the running timer and opens an interruption; "Back to work" closes it and
/// resumes the paused task as a new time entry.
/// </summary>
public sealed class InterruptionService(
    WaypointDbContext db,
    TimerService timer,
    PeopleService people,
    SettingsService settings,
    TimeProvider clock)
{
    public async Task<List<InterruptionDto>> ListAsync(DateOnly? date, CancellationToken ct = default)
    {
        var tz = await settings.TimeZoneAsync(ct);
        var now = Now();
        var day = LocalTime.DayBoundsUtc(date ?? LocalTime.LocalDate(now, tz), tz);
        var items = await Query()
            .Where(i => i.StartedAt < day.End && (i.EndedAt == null || i.EndedAt > day.Start))
            .OrderBy(i => i.StartedAt)
            .ToListAsync(ct);
        return items.Select(i => i.ToDto(now)).ToList();
    }

    public async Task<InterruptionDto> GetAsync(int id, CancellationToken ct = default) =>
        (await Query().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw ApiException.NotFound("Interruption")).ToDto(Now());

    /// <summary>Pauses the running timer and opens an interruption. Idempotent while one is already open.</summary>
    public async Task<InterruptionDto> StartAsync(InterruptionDetails? details, CancellationToken ct = default)
    {
        var open = await db.Interruptions.Where(i => i.EndedAt == null).OrderByDescending(i => i.StartedAt).FirstOrDefaultAsync(ct);
        if (open is not null)
        {
            if (details is not null) await ApplyDetailsAsync(open, details, requireSource: false, ct);
            await db.SaveChangesAsync(ct);
            return await GetAsync(open.Id, ct);
        }

        var now = Now();
        var paused = await timer.StopRunningAsync(now, ct);
        var interruption = new Interruption { StartedAt = now, PausedTimeEntry = paused };
        if (details is not null) await ApplyDetailsAsync(interruption, details, requireSource: false, ct);
        db.Interruptions.Add(interruption);
        await db.SaveChangesAsync(ct);
        return await GetAsync(interruption.Id, ct);
    }

    public async Task<InterruptionDto> UpdateAsync(int id, InterruptionDetails details, CancellationToken ct = default)
    {
        var i = await db.Interruptions.FindAsync([id], ct) ?? throw ApiException.NotFound("Interruption");
        await ApplyDetailsAsync(i, details, requireSource: true, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Edits the times of a finished interruption (e.g. from the daily review).</summary>
    public async Task<InterruptionDto> SetTimesAsync(int id, DateTime startedAt, DateTime endedAt, CancellationToken ct = default)
    {
        var i = await db.Interruptions.FindAsync([id], ct) ?? throw ApiException.NotFound("Interruption");
        var start = Mapping.TruncateToSecond(Mapping.Utc(startedAt));
        var end = Mapping.TruncateToSecond(Mapping.Utc(endedAt));
        if (end <= start) throw ApiException.BadRequest("End must be after start.");
        i.StartedAt = start;
        i.EndedAt = end;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>"Back to work": closes the interruption and resumes the paused task (as a new time entry).</summary>
    public async Task<ResumeResponse> ResumeAsync(int id, InterruptionDetails? details, CancellationToken ct = default)
    {
        var i = await db.Interruptions.Include(x => x.PausedTimeEntry).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw ApiException.NotFound("Interruption");
        if (details is not null) await ApplyDetailsAsync(i, details, requireSource: false, ct);

        var now = Now();
        i.EndedAt ??= now;

        TimeEntry? resumed = null;
        var paused = i.PausedTimeEntry;
        var timerRunning = await db.TimeEntries.AnyAsync(e => e.EndedAt == null, ct);
        if (paused is not null && !timerRunning && i.EndedAt == now)
        {
            resumed = new TimeEntry
            {
                TaskId = paused.TaskId,
                ProjectId = paused.ProjectId,
                Kind = paused.Kind,
                StartedAt = now,
                Note = paused.Note,
            };
            db.TimeEntries.Add(resumed);
            if (paused.TaskId is not null)
            {
                var task = await db.Tasks.FindAsync([paused.TaskId.Value], ct);
                if (task is not null && task.Status != WorkTaskStatus.Done) task.Status = WorkTaskStatus.Doing;
            }
        }

        await db.SaveChangesAsync(ct);

        TimeEntryDto? resumedDto = null;
        if (resumed is not null)
        {
            var loaded = await db.TimeEntries.Include(e => e.Task).Include(e => e.Project).FirstAsync(e => e.Id == resumed.Id, ct);
            resumedDto = loaded.ToDto(now);
        }
        return new ResumeResponse(await GetAsync(id, ct), resumedDto);
    }

    /// <summary>
    /// Turns an interruption into a task and attaches its duration as a reactive time entry.
    /// An open interruption is closed first (without resuming the paused task).
    /// </summary>
    public async Task<ConvertResponse> ConvertAsync(int id, ConvertRequest request, CancellationToken ct = default)
    {
        var i = await db.Interruptions.Include(x => x.Person).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw ApiException.NotFound("Interruption");
        if (i.ConvertedTaskId is not null) throw ApiException.Conflict("This interruption was already converted to a task.");
        if (request.ProjectId is not null && !await db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct))
            throw ApiException.BadRequest("Unknown project.");

        var now = Now();
        i.EndedAt ??= now;

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) title = DefaultTitle(i);
        if (title.Length > 300) title = title[..300];

        var status = request.Status ?? WorkTaskStatus.Done;
        var tz = await settings.TimeZoneAsync(ct);
        var task = new WorkTask
        {
            Title = title,
            ProjectId = request.ProjectId,
            Priority = 2,
            Status = status,
            Notes = i.Note,
            CreatedAt = now,
            CompletedAt = status == WorkTaskStatus.Done ? i.EndedAt : null,
            PlannedDate = status is WorkTaskStatus.Today or WorkTaskStatus.Doing ? LocalTime.LocalDate(now, tz) : null,
        };
        var entry = new TimeEntry
        {
            Task = task,
            ProjectId = request.ProjectId,
            Kind = TimeEntryKind.Reactive,
            StartedAt = i.StartedAt,
            EndedAt = i.EndedAt,
            Note = "From interruption",
        };
        db.Tasks.Add(task);
        db.TimeEntries.Add(entry);
        if (task.PlannedDate is DateOnly planned) await PlanLog.RecordAsync(db, task, planned, ct);
        i.ConvertedTask = task;
        await db.SaveChangesAsync(ct);

        var taskDto = (await db.Tasks.Include(t => t.Project).FirstAsync(t => t.Id == task.Id, ct))
            .ToDto(Mapping.Minutes(entry.EndedAt!.Value - entry.StartedAt));
        var entryDto = (await db.TimeEntries.Include(e => e.Task).Include(e => e.Project).FirstAsync(e => e.Id == entry.Id, ct)).ToDto(now);
        return new ConvertResponse(await GetAsync(id, ct), taskDto, entryDto);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var i = await db.Interruptions.FindAsync([id], ct) ?? throw ApiException.NotFound("Interruption");
        db.Interruptions.Remove(i);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyDetailsAsync(Interruption i, InterruptionDetails d, bool requireSource, CancellationToken ct)
    {
        if (requireSource && d.SourceType is null) throw ApiException.BadRequest("Pick where the interruption came from.");
        if (d.SourceType is not null) i.SourceType = d.SourceType;

        if (!string.IsNullOrWhiteSpace(d.PersonName))
        {
            i.Person = await people.GetOrCreateAsync(d.PersonName, ct);
            i.PersonId = i.Person.Id;
        }
        else if (d.PersonId is not null)
        {
            i.PersonId = await db.People.AnyAsync(p => p.Id == d.PersonId, ct) ? d.PersonId : throw ApiException.BadRequest("Unknown person.");
        }
        else
        {
            i.PersonId = null;
            i.Person = null;
        }

        i.Note = string.IsNullOrWhiteSpace(d.Note) ? null : d.Note.Trim();
        i.CouldWait = d.CouldWait;
    }

    private static string DefaultTitle(Interruption i)
    {
        if (!string.IsNullOrWhiteSpace(i.Note))
        {
            var firstLine = i.Note.Split('\n')[0].Trim();
            if (firstLine.Length > 0) return firstLine;
        }
        var source = i.SourceType switch
        {
            InterruptionSource.WalkUp => "Walk-up",
            InterruptionSource.GoogleChat => "Google Chat",
            InterruptionSource.Email => "Email",
            InterruptionSource.Phone => "Phone call",
            InterruptionSource.MeetingOverrun => "Meeting overrun",
            InterruptionSource.SelfDistraction => "Self-distraction",
            _ => "Interruption",
        };
        return i.Person is null ? source : $"{source}: {i.Person.Name}";
    }

    private IQueryable<Interruption> Query() => db.Interruptions
        .Include(i => i.Person)
        .Include(i => i.PausedTimeEntry).ThenInclude(e => e!.Task);

    private DateTime Now() => Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);
}
