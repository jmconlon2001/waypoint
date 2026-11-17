namespace Waypoint.Api.Data.Entities;

/// <summary>
/// Per-day metrics, filled by the nightly job and recomputable on demand. Minutes are rounded
/// to 0.01. Used for trends and CSV export; the current day is always computed live.
/// </summary>
public class DailyAggregate
{
    public DateOnly Date { get; set; }
    public double TaskMinutes { get; set; }
    public double ReactiveMinutes { get; set; }
    public double UntrackedMinutes { get; set; }
    public double MeetingMinutes { get; set; }
    public double PlannedFocusMinutes { get; set; }
    public double UnloggedMinutes { get; set; }
    public int InterruptionCount { get; set; }
    public double InterruptionMinutes { get; set; }
    public int CouldWaitCount { get; set; }
    public int TaskSwitches { get; set; }
    public int FocusBlocks { get; set; }
    public double LongestFocusBlockMinutes { get; set; }
    public int PlannedTasks { get; set; }
    public int PlannedTasksWorked { get; set; }
    public double PlannedTaskMinutes { get; set; }
    public double UnplannedTaskMinutes { get; set; }
    public int MeetingCount { get; set; }
    public DateTime ComputedAt { get; set; }
}
