using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Services;

/// <summary>
/// Keeps the <see cref="TaskPlan"/> history in step with <see cref="WorkTask.PlannedDate"/>. Past plans are
/// never rewritten: rolling a task over adds tomorrow's row and keeps today's, so planned vs. actual still
/// shows that it was planned (and not finished) today.
/// </summary>
internal static class PlanLog
{
    /// <summary>Call after changing PlannedDate. Does not save.</summary>
    public static async Task SyncAsync(WaypointDbContext db, WorkTask task, DateOnly? previous, DateOnly today, CancellationToken ct)
    {
        if (task.PlannedDate == previous) return;
        if (task.PlannedDate is DateOnly planned) await RecordAsync(db, task, planned, ct);
        // A future plan that moved is just a reschedule, not history.
        if (previous is DateOnly old && old > today) await ForgetAsync(db, task.Id, old, ct);
    }

    public static async Task RecordAsync(WaypointDbContext db, WorkTask task, DateOnly date, CancellationToken ct)
    {
        if (db.TaskPlans.Local.Any(p => p.Date == date && (p.Task == task || (task.Id != 0 && p.TaskId == task.Id)))) return;
        if (task.Id != 0 && await db.TaskPlans.AnyAsync(p => p.TaskId == task.Id && p.Date == date, ct)) return;
        db.TaskPlans.Add(new TaskPlan { Task = task, Date = date });
    }

    public static async Task ForgetAsync(WaypointDbContext db, int taskId, DateOnly date, CancellationToken ct)
    {
        if (taskId == 0) return;
        var rows = await db.TaskPlans.Where(p => p.TaskId == taskId && p.Date == date).ToListAsync(ct);
        db.TaskPlans.RemoveRange(rows);
    }
}
