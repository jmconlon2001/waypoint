namespace Waypoint.Api.Data.Entities;

public enum WorkTaskStatus
{
    Backlog,
    Today,
    Doing,
    Blocked,
    Done,
}

/// <summary>A todo. Named WorkTask to avoid clashing with System.Threading.Tasks.Task.</summary>
public class WorkTask
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    /// <summary>1 = high, 2 = normal, 3 = low.</summary>
    public int Priority { get; set; } = 2;
    public DateOnly? DueDate { get; set; }
    public string? Notes { get; set; }
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Backlog;
    /// <summary>The local date the task is planned for ("Today" list).</summary>
    public DateOnly? PlannedDate { get; set; }
    /// <summary>Keyboard pin slot 1-5, or null when not pinned.</summary>
    public int? PinSlot { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
