using Waypoint.Api.Contracts;
using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Services;

internal static class Mapping
{
    public static double Minutes(TimeSpan span) => Math.Round(Math.Max(0, span.TotalMinutes), 2);

    /// <summary>Npgsql only accepts UTC DateTimes for timestamptz; normalize anything coming off the wire.</summary>
    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>Timestamps are stored to the second; sub-second precision only adds noise to a time tracker.</summary>
    public static DateTime TruncateToSecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    public static ProjectDto ToDto(this Project p) => new(p.Id, p.Name, p.Color, p.Active);

    public static PersonDto ToDto(this Person p) => new(p.Id, p.Name);

    public static TaskDto ToDto(this WorkTask t, double trackedMinutes = 0) => new(
        t.Id, t.Title, t.ProjectId, t.Project?.Name, t.Project?.Color, t.Priority, t.DueDate, t.Notes,
        t.Status, t.PlannedDate, t.PinSlot, t.CreatedAt, t.CompletedAt, Math.Round(trackedMinutes, 2));

    public static TimeEntryDto ToDto(this TimeEntry e, DateTime now) => new(
        e.Id, e.TaskId, e.Task?.Title, e.ProjectId, e.Project?.Name, e.Project?.Color, e.Kind,
        e.StartedAt, e.EndedAt, e.Note, Minutes((e.EndedAt ?? now) - e.StartedAt));

    public static InterruptionDto ToDto(this Interruption i, DateTime now) => new(
        i.Id, i.StartedAt, i.EndedAt, i.SourceType, i.PersonId, i.Person?.Name, i.Note, i.CouldWait,
        i.PausedTimeEntryId, i.PausedTimeEntry?.TaskId, i.PausedTimeEntry?.Task?.Title, i.ConvertedTaskId,
        Minutes((i.EndedAt ?? now) - i.StartedAt));
}
