namespace Waypoint.Api.Data.Entities;

/// <summary>
/// A task was on the Today list for <see cref="Date"/>. Kept as history, because <see cref="WorkTask.PlannedDate"/>
/// moves when a task rolls over. This is the "planned" side of planned vs. actual.
/// </summary>
public class TaskPlan
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public WorkTask? Task { get; set; }
    /// <summary>Local calendar date.</summary>
    public DateOnly Date { get; set; }
}
