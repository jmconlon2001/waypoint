namespace Waypoint.Api.Data.Entities;

public enum InterruptionSource
{
    WalkUp,
    GoogleChat,
    Email,
    Phone,
    MeetingOverrun,
    SelfDistraction,
}

public class Interruption
{
    public int Id { get; set; }
    /// <summary>UTC.</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>UTC; null while the interruption is still open.</summary>
    public DateTime? EndedAt { get; set; }
    /// <summary>Required by the UI; null only for the few seconds between clicking "Interrupted" and saving the form.</summary>
    public InterruptionSource? SourceType { get; set; }
    public int? PersonId { get; set; }
    public Person? Person { get; set; }
    public string? Note { get; set; }
    public bool? CouldWait { get; set; }
    /// <summary>The time entry that was running when the interruption started (resumed on "Back to work").</summary>
    public int? PausedTimeEntryId { get; set; }
    public TimeEntry? PausedTimeEntry { get; set; }
    public int? ConvertedTaskId { get; set; }
    public WorkTask? ConvertedTask { get; set; }
}
