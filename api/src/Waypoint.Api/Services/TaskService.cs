using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

public sealed class TaskService(WaypointDbContext db, SettingsService settings, TimeProvider clock)
{
    public const int MaxPinSlots = 5;

    /// <summary>
    /// Lists tasks. <paramref name="view"/>: "open" (default, not done), "today", "done", "all".
    /// </summary>
    public async Task<List<TaskDto>> ListAsync(string? view, WorkTaskStatus? status, int? projectId, string? search, CancellationToken ct = default)
    {
        var query = db.Tasks.Include(t => t.Project).AsQueryable();

        switch ((view ?? "open").ToLowerInvariant())
        {
            case "today":
                return await TodayAsync(ct);
            case "done":
                query = query.Where(t => t.Status == WorkTaskStatus.Done);
                break;
            case "all":
                break;
            default:
                query = query.Where(t => t.Status != WorkTaskStatus.Done);
                break;
        }

        if (status is not null) query = query.Where(t => t.Status == status);
        if (projectId is not null) query = query.Where(t => t.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(term) || (t.Notes != null && t.Notes.ToLower().Contains(term)));
        }

        var tasks = await query
            .OrderBy(t => t.Status == WorkTaskStatus.Done)
            .ThenBy(t => t.Priority)
            .ThenBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.CreatedAt)
            .Take(500)
            .ToListAsync(ct);
        return await WithTrackedMinutesAsync(tasks, ct);
    }

    /// <summary>
    /// The Today list: open tasks planned for today or earlier (roll-over), or marked Today/Doing,
    /// plus tasks completed today.
    /// </summary>
    public async Task<List<TaskDto>> TodayAsync(CancellationToken ct = default)
    {
        var tz = await settings.TimeZoneAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var today = LocalTime.LocalDate(now, tz);
        var day = LocalTime.DayBoundsUtc(today, tz);

        var tasks = await db.Tasks.Include(t => t.Project)
            .Where(t =>
                (t.Status != WorkTaskStatus.Done &&
                    (t.Status == WorkTaskStatus.Today || t.Status == WorkTaskStatus.Doing ||
                     (t.PlannedDate != null && t.PlannedDate <= today))) ||
                (t.Status == WorkTaskStatus.Done && t.CompletedAt >= day.Start && t.CompletedAt < day.End))
            .ToListAsync(ct);

        var ordered = tasks
            .OrderBy(t => t.Status == WorkTaskStatus.Done)
            .ThenBy(t => t.PinSlot ?? int.MaxValue)
            .ThenBy(t => t.Status == WorkTaskStatus.Blocked)
            .ThenBy(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToList();
        return await WithTrackedMinutesAsync(ordered, ct);
    }

    public async Task<TaskDto> GetAsync(int id, CancellationToken ct = default)
    {
        var t = await db.Tasks.Include(x => x.Project).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Task");
        return (await WithTrackedMinutesAsync([t], ct))[0];
    }

    public async Task<TaskDto> CreateAsync(TaskInput input, CancellationToken ct = default)
    {
        var task = new WorkTask { CreatedAt = Now() };
        await ApplyAsync(task, input, ct);
        db.Tasks.Add(task);
        await PlanLog.SyncAsync(db, task, null, await LocalTodayAsync(ct), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(task.Id, ct);
    }

    public async Task<TaskDto> UpdateAsync(int id, TaskInput input, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([id], ct) ?? throw ApiException.NotFound("Task");
        var previous = task.PlannedDate;
        await ApplyAsync(task, input, ct);
        await PlanLog.SyncAsync(db, task, previous, await LocalTodayAsync(ct), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([id], ct) ?? throw ApiException.NotFound("Task");
        // Keep the time: entries lose the task link but keep their project.
        var running = await db.TimeEntries.Where(e => e.TaskId == id && e.EndedAt == null).ToListAsync(ct);
        foreach (var e in running) e.EndedAt = Now();
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
    }

    public async Task<QuickAddResponse> QuickAddAsync(QuickAddRequest request, CancellationToken ct = default)
    {
        var parsed = QuickAddParser.Parse(request.Text);
        if (parsed.Title.Length == 0) throw ApiException.BadRequest("Type a title, e.g. \"Review AG failover #origination !1\".");

        string? warning = null;
        Project? project = null;
        if (parsed.ProjectToken is not null)
        {
            var projects = await db.Projects.Where(p => p.Active).ToListAsync(ct);
            project = QuickAddParser.MatchProject(parsed.ProjectToken, projects, p => p.Name);
            if (project is null) warning = $"No single project matches '#{parsed.ProjectToken}'; the task was added without a project.";
        }

        var today = request.Today ? LocalTime.LocalDate(Now(), await settings.TimeZoneAsync(ct)) : (DateOnly?)null;
        var task = new WorkTask
        {
            Title = Truncate(parsed.Title, 300),
            ProjectId = project?.Id,
            Priority = parsed.Priority ?? 2,
            Status = request.Today ? WorkTaskStatus.Today : WorkTaskStatus.Backlog,
            PlannedDate = today,
            CreatedAt = Now(),
        };
        db.Tasks.Add(task);
        if (today is DateOnly d) await PlanLog.RecordAsync(db, task, d, ct);
        await db.SaveChangesAsync(ct);
        return new QuickAddResponse(await GetAsync(task.Id, ct), warning);
    }

    /// <summary>Adds the task to (or removes it from) today's plan.</summary>
    public async Task<TaskDto> PlanAsync(int id, bool today, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([id], ct) ?? throw ApiException.NotFound("Task");
        var localToday = await LocalTodayAsync(ct);
        var previous = task.PlannedDate;
        if (today)
        {
            task.PlannedDate = localToday;
            if (task.Status is WorkTaskStatus.Backlog or WorkTaskStatus.Done)
            {
                task.Status = WorkTaskStatus.Today;
                task.CompletedAt = null;
            }
        }
        else
        {
            task.PlannedDate = null;
            task.PinSlot = null;
            if (task.Status is WorkTaskStatus.Today or WorkTaskStatus.Doing) task.Status = WorkTaskStatus.Backlog;
            // Taken off today's list the same day: it wasn't really planned.
            await PlanLog.ForgetAsync(db, task.Id, localToday, ct);
        }
        await PlanLog.SyncAsync(db, task, previous, localToday, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDto> SetStatusAsync(int id, WorkTaskStatus status, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([id], ct) ?? throw ApiException.NotFound("Task");
        var previous = task.PlannedDate;
        await SetStatusInternalAsync(task, status, ct);
        await PlanLog.SyncAsync(db, task, previous, await LocalTodayAsync(ct), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// End-of-day roll-over: moves unfinished tasks to <paramref name="request"/>.ToDate (usually tomorrow),
    /// or back to the backlog when ToDate is null. Today's plan is kept as history either way.
    /// </summary>
    public async Task<RollOverResponse> RollOverAsync(RollOverRequest request, CancellationToken ct = default)
    {
        var today = await LocalTodayAsync(ct);
        if (request.ToDate is DateOnly to && to <= today) throw ApiException.BadRequest("Roll tasks over to a later day.");
        var ids = (request.TaskIds ?? []).Distinct().ToList();
        var tasks = await db.Tasks.Where(t => ids.Contains(t.Id) && t.Status != WorkTaskStatus.Done).ToListAsync(ct);

        var running = await db.TimeEntries.Where(e => e.EndedAt == null && e.TaskId != null && ids.Contains(e.TaskId.Value)).ToListAsync(ct);
        foreach (var e in running) e.EndedAt = Now();

        foreach (var task in tasks)
        {
            var previous = task.PlannedDate;
            task.PlannedDate = request.ToDate;
            if (request.ToDate is null) task.PinSlot = null;
            // Blocked stays blocked; anything else waits in the backlog until its day comes round.
            if (task.Status is WorkTaskStatus.Today or WorkTaskStatus.Doing) task.Status = WorkTaskStatus.Backlog;
            await PlanLog.SyncAsync(db, task, previous, today, ct);
        }
        await db.SaveChangesAsync(ct);
        return new RollOverResponse(tasks.Count, request.ToDate);
    }

    /// <summary>Pins the task to keyboard slot 1-5 (taking the slot from any other task), or unpins it.</summary>
    public async Task<TaskDto> PinAsync(int id, int? slot, CancellationToken ct = default)
    {
        if (slot is not null && (slot < 1 || slot > MaxPinSlots)) throw ApiException.BadRequest($"Pin slot must be 1-{MaxPinSlots}.");
        var task = await db.Tasks.FindAsync([id], ct) ?? throw ApiException.NotFound("Task");
        if (slot is not null)
        {
            if (task.Status == WorkTaskStatus.Done) throw ApiException.BadRequest("Done tasks can't be pinned.");
            var holders = await db.Tasks.Where(t => t.PinSlot == slot && t.Id != id).ToListAsync(ct);
            foreach (var h in holders) h.PinSlot = null;
        }
        task.PinSlot = slot;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task ApplyAsync(WorkTask task, TaskInput input, CancellationToken ct)
    {
        var title = input.Title?.Trim() ?? "";
        if (title.Length == 0) throw ApiException.BadRequest("Title is required.");
        if (title.Length > 300) throw ApiException.BadRequest("Title is too long (300 characters max).");
        var priority = input.Priority ?? task.Priority;
        if (priority is < 1 or > 3) throw ApiException.BadRequest("Priority must be 1, 2 or 3.");
        if (input.ProjectId is not null && !await db.Projects.AnyAsync(p => p.Id == input.ProjectId, ct))
            throw ApiException.BadRequest("Unknown project.");

        task.Title = title;
        task.ProjectId = input.ProjectId;
        task.Priority = priority;
        task.DueDate = input.DueDate;
        task.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        task.PlannedDate = input.PlannedDate;
        if (input.Status is not null) await SetStatusInternalAsync(task, input.Status.Value, ct);
        if (task.Status == WorkTaskStatus.Today && task.PlannedDate is null)
            task.PlannedDate = LocalTime.LocalDate(Now(), await settings.TimeZoneAsync(ct));
    }

    private async Task SetStatusInternalAsync(WorkTask task, WorkTaskStatus status, CancellationToken ct)
    {
        if (status == task.Status) return;
        if (status == WorkTaskStatus.Done)
        {
            task.CompletedAt = Now();
            task.PinSlot = null;
            // Finishing a task stops its timer.
            if (task.Id != 0)
            {
                var running = await db.TimeEntries.Where(e => e.TaskId == task.Id && e.EndedAt == null).ToListAsync(ct);
                foreach (var e in running) e.EndedAt = Now();
            }
        }
        else
        {
            task.CompletedAt = null;
        }
        if (status == WorkTaskStatus.Today && task.PlannedDate is null)
            task.PlannedDate = LocalTime.LocalDate(Now(), await settings.TimeZoneAsync(ct));
        task.Status = status;
    }

    private async Task<List<TaskDto>> WithTrackedMinutesAsync(List<WorkTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var ids = tasks.Select(t => t.Id).ToList();
        var entries = await db.TimeEntries
            .Where(e => e.TaskId != null && ids.Contains(e.TaskId.Value))
            .Select(e => new { e.TaskId, e.StartedAt, e.EndedAt })
            .ToListAsync(ct);
        var now = Now();
        var totals = entries
            .GroupBy(e => e.TaskId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(e => Mapping.Minutes((e.EndedAt ?? now) - e.StartedAt)));
        return tasks.Select(t => t.ToDto(totals.GetValueOrDefault(t.Id))).ToList();
    }

    private DateTime Now() => Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);

    private async Task<DateOnly> LocalTodayAsync(CancellationToken ct) => LocalTime.LocalDate(Now(), await settings.TimeZoneAsync(ct));

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
