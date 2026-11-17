namespace Waypoint.Api.Data.Entities;

public enum TimeEntryKind
{
    /// <summary>Planned/focus work on a task or project.</summary>
    Task,
    /// <summary>Reactive work (e.g. an interruption converted into a task).</summary>
    Reactive,
    /// <summary>Time explicitly marked as not attributable.</summary>
    Untracked,
}

public class TimeEntry
{
    public int Id { get; set; }
    public int? TaskId { get; set; }
    public WorkTask? Task { get; set; }
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public TimeEntryKind Kind { get; set; } = TimeEntryKind.Task;
    /// <summary>UTC.</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>UTC; null while the timer is running.</summary>
    public DateTime? EndedAt { get; set; }
    public string? Note { get; set; }
}
