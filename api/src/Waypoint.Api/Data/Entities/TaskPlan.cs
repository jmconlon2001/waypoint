namespace Waypoint.Api.Data.Entities;

/// <summary>
/// History of "planned for day X" (a task's PlannedDate moves when it rolls over, so the plan for a
/// past day is kept here). Used for planned-vs-actual.
/// </summary>
public class TaskPlan
{
    public int TaskId { get; set; }
    public WorkTask? Task { get; set; }
    public DateOnly Date { get; set; }
}
