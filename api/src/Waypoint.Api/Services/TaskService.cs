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
    /// The Today list: open tasks planned for today or earlier (carried over), Doing tasks, tasks marked
    /// Today without a date, plus tasks completed today. Tasks rolled over to tomorrow don't show.
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
                    (t.Status == WorkTaskStatus.Doing ||
                     (t.PlannedDate != null ? t.PlannedDate <= today : t.Status == WorkTaskStatus.Today))) ||
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
        await RecordPlanAsync(task, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(task.Id, ct);
    }

    public async Task<TaskDto> UpdateAsync(int id, TaskInput input, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([id], ct) ?? throw ApiException.NotFound("Task");
        await ApplyAsync(task, input, ct);
        await RecordPlanAsync(task, ct);
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
        await RecordPlanAsync(task, ct);
        await db.SaveChangesAsync(ct);
        return new QuickAddResponse(await GetAsync(task.Id, ct), warning);
    }

    /// <summary>Adds the task to (or removes it from) today's plan.</summary>
    public async Task<TaskDto> PlanAsync(int id, bool today, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([id], ct) ?? throw ApiException.NotFound("Task");
        var todayDate = LocalTime.LocalDate(Now(), await settings.TimeZoneAsync(ct));
        if (today)
        {
            task.PlannedDate = todayDate;
            if (task.Status is WorkTaskStatus.Backlog or WorkTaskStatus.Done)
            {
                task.Status = WorkTaskStatus.Today;
                task.CompletedAt = null;
            }
            await RecordPlanAsync(task, ct);
        }
        else
        {
            // Taken off today's plan the same day: it wasn't really planned.
            var plan = await db.TaskPlans.FirstOrDefaultAsync(p => p.TaskId == id && p.Date == todayDate, ct);
            if (plan is not null) db.TaskPlans.Remove(plan);
            task.PlannedDate = null;
            task.PinSlot = null;
            if (task.Status is WorkTaskStatus.Today or WorkTaskStatus.Doing) task.Status = WorkTaskStatus.Backlog;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDto> SetStatusAsync(int id, WorkTaskStatus status, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([id], ct) ?? throw ApiException.NotFound("Task");
        await SetStatusInternalAsync(task, status, ct);
        await RecordPlanAsync(task, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
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

    /// <summary>Unfinished tasks on the Today list of <paramref name="date"/> (default today): what rollover would move.</summary>
    public async Task<List<TaskDto>> RolloverCandidatesAsync(DateOnly? date, CancellationToken ct = default)
    {
        var day = date ?? LocalTime.LocalDate(Now(), await settings.TimeZoneAsync(ct));
        var tasks = await RolloverQuery(day).Include(t => t.Project).OrderBy(t => t.Priority).ThenBy(t => t.CreatedAt).ToListAsync(ct);
        return await WithTrackedMinutesAsync(tasks, ct);
    }

    /// <summary>
    /// FR-T5 end-of-day rollover: unfinished tasks planned on/before <c>date</c> (or marked Today/Doing)
    /// move to the next day. Tasks in <c>SkipTaskIds</c> are opted out: they go back to the backlog.
    /// </summary>
    public async Task<RolloverResult> RolloverAsync(RolloverRequest request, CancellationToken ct = default)
    {
        var day = request.Date ?? LocalTime.LocalDate(Now(), await settings.TimeZoneAsync(ct));
        var next = day.AddDays(1);
        var skip = (request.SkipTaskIds ?? []).ToHashSet();
        var tasks = await RolloverQuery(day).ToListAsync(ct);
        int rolled = 0, unplanned = 0;
        foreach (var t in tasks)
        {
            if (skip.Contains(t.Id))
            {
                t.PlannedDate = null;
                t.PinSlot = null;
                if (t.Status is WorkTaskStatus.Today or WorkTaskStatus.Doing) t.Status = WorkTaskStatus.Backlog;
                unplanned++;
                continue;
            }
            t.PlannedDate = next;
            if (t.Status == WorkTaskStatus.Doing) t.Status = WorkTaskStatus.Today;
            if (t.Status == WorkTaskStatus.Backlog) t.Status = WorkTaskStatus.Today;
            await RecordPlanAsync(t, ct);
            rolled++;
        }
        await db.SaveChangesAsync(ct);
        return new RolloverResult(next, rolled, unplanned);
    }

    private IQueryable<WorkTask> RolloverQuery(DateOnly day) => db.Tasks.Where(t =>
        t.Status != WorkTaskStatus.Done &&
        (t.PlannedDate != null ? t.PlannedDate <= day : (t.Status == WorkTaskStatus.Today || t.Status == WorkTaskStatus.Doing)));

    /// <summary>Remembers that the task was planned for its PlannedDate (planned-vs-actual history). Does not save.</summary>
    private async Task RecordPlanAsync(WorkTask task, CancellationToken ct)
    {
        if (task.PlannedDate is not { } date || task.Status == WorkTaskStatus.Done) return;
        if (db.TaskPlans.Local.Any(p => (p.Task == task || (task.Id != 0 && p.TaskId == task.Id)) && p.Date == date)) return;
        if (task.Id != 0 && await db.TaskPlans.AnyAsync(p => p.TaskId == task.Id && p.Date == date, ct)) return;
        db.TaskPlans.Add(new TaskPlan { Task = task, Date = date });
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

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
