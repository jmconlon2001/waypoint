using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Data;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>FR-D7 CSV exports for a local date range. Times are given both in local time and UTC.</summary>
public sealed class ExportService(WaypointDbContext db, MetricsService metrics, SettingsService settings)
{
    public static readonly string[] Datasets = ["time-entries", "interruptions", "calendar-events", "daily-aggregates"];

    public async Task<string> ExportAsync(string dataset, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) throw ApiException.BadRequest("The end date must be on or after the start date.");
        if (to.DayNumber - from.DayNumber > 3700) throw ApiException.BadRequest("Pick a range of at most ten years.");
        var s = await settings.GetAsync(ct);
        var tz = LocalTime.FindTimeZone(s.TimeZone);
        var start = LocalTime.ToUtc(from, TimeOnly.MinValue, tz);
        var end = LocalTime.ToUtc(to.AddDays(1), TimeOnly.MinValue, tz);
        string Local(DateTime? utc) => utc is null ? "" : LocalTime.ToLocal(utc.Value, tz).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        var csv = new Csv();
        switch (dataset)
        {
            case "time-entries":
            {
                csv.Row("id", "date", "start_local", "end_local", "start_utc", "end_utc", "minutes", "kind", "task_id", "task", "project", "note");
                var rows = await db.TimeEntries.Include(e => e.Task).Include(e => e.Project)
                    .Where(e => e.StartedAt >= start && e.StartedAt < end).OrderBy(e => e.StartedAt).ToListAsync(ct);
                foreach (var e in rows)
                    csv.Row(e.Id, LocalTime.LocalDate(e.StartedAt, tz), Local(e.StartedAt), Local(e.EndedAt), e.StartedAt, e.EndedAt,
                        e.EndedAt is null ? null : Mapping.Minutes(e.EndedAt.Value - e.StartedAt), e.Kind.ToString(), e.TaskId, e.Task?.Title, e.Project?.Name, e.Note);
                break;
            }
            case "interruptions":
            {
                csv.Row("id", "date", "start_local", "end_local", "start_utc", "end_utc", "minutes", "source", "person", "could_wait", "paused_task", "converted_task_id", "note");
                var rows = await db.Interruptions.Include(i => i.Person).Include(i => i.PausedTimeEntry).ThenInclude(e => e!.Task)
                    .Where(i => i.StartedAt >= start && i.StartedAt < end).OrderBy(i => i.StartedAt).ToListAsync(ct);
                foreach (var i in rows)
                    csv.Row(i.Id, LocalTime.LocalDate(i.StartedAt, tz), Local(i.StartedAt), Local(i.EndedAt), i.StartedAt, i.EndedAt,
                        i.EndedAt is null ? null : Mapping.Minutes(i.EndedAt.Value - i.StartedAt), MetricsService.SourceLabel(i.SourceType),
                        i.Person?.Name, i.CouldWait, i.PausedTimeEntry?.Task?.Title, i.ConvertedTaskId, i.Note);
                break;
            }
            case "calendar-events":
            {
                csv.Row("id", "date", "start_local", "end_local", "start_utc", "end_utc", "minutes", "title", "all_day", "attendees", "recurring", "event_type", "meeting_type", "project", "counted", "excluded_because", "source");
                var rows = await db.CalendarEvents.Include(e => e.Project)
                    .Where(e => e.StartedAt >= start && e.StartedAt < end).OrderBy(e => e.StartedAt).ToListAsync(ct);
                foreach (var e in rows)
                {
                    var reason = CalendarRules.ExclusionReason(e.AllDay, e.StartedAt, e.EndedAt, e.EventType, e.Declined, s.MaxMeetingMinutes);
                    csv.Row(e.Id, LocalTime.LocalDate(e.StartedAt, tz), Local(e.StartedAt), Local(e.EndedAt), e.StartedAt, e.EndedAt,
                        Mapping.Minutes(e.EndedAt - e.StartedAt), e.Title, e.AllDay, e.AttendeeCount, e.Recurring, e.EventType,
                        e.MeetingType.ToString(), e.Project?.Name, reason is null, reason, e.Source);
                }
                break;
            }
            case "daily-aggregates":
            {
                csv.Row("date", "task_minutes", "reactive_minutes", "untracked_minutes", "meeting_minutes", "meeting_count", "planned_focus_minutes",
                    "unlogged_minutes", "interruption_count", "interruption_minutes", "could_wait_count", "task_switches", "focus_blocks",
                    "longest_focus_block_minutes", "planned_tasks", "planned_tasks_worked", "planned_task_minutes", "unplanned_task_minutes", "computed_at_utc");
                foreach (var a in await metrics.GetAggregatesAsync(from, to, ct))
                    csv.Row(a.Date, a.TaskMinutes, a.ReactiveMinutes, a.UntrackedMinutes, a.MeetingMinutes, a.MeetingCount, a.PlannedFocusMinutes,
                        a.UnloggedMinutes, a.InterruptionCount, a.InterruptionMinutes, a.CouldWaitCount, a.TaskSwitches, a.FocusBlocks,
                        a.LongestFocusBlockMinutes, a.PlannedTasks, a.PlannedTasksWorked, a.PlannedTaskMinutes, a.UnplannedTaskMinutes, a.ComputedAt);
                break;
            }
            default:
                throw ApiException.NotFound($"Export '{dataset}'");
        }
        return csv.ToString();
    }
}
