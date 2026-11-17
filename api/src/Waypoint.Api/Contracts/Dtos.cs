using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Contracts;

// All DateTime values are UTC on the wire (ISO 8601 with "Z"); DateOnly values are local calendar dates.

public sealed record ProjectDto(int Id, string Name, string Color, bool Active);
public sealed record ProjectInput(string Name, string? Color, bool? Active);

public sealed record PersonDto(int Id, string Name);
public sealed record PersonInput(string Name);

public sealed record TaskDto(
    int Id,
    string Title,
    int? ProjectId,
    string? ProjectName,
    string? ProjectColor,
    int Priority,
    DateOnly? DueDate,
    string? Notes,
    WorkTaskStatus Status,
    DateOnly? PlannedDate,
    int? PinSlot,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    double TrackedMinutes);

public sealed record TaskInput(
    string Title,
    int? ProjectId,
    int? Priority,
    DateOnly? DueDate,
    string? Notes,
    WorkTaskStatus? Status,
    DateOnly? PlannedDate);

public sealed record QuickAddRequest(string Text, bool Today);
public sealed record QuickAddResponse(TaskDto Task, string? Warning);
public sealed record PlanRequest(bool Today);
public sealed record PinRequest(int? Slot);
public sealed record StatusRequest(WorkTaskStatus Status);

public sealed record TimeEntryDto(
    int Id,
    int? TaskId,
    string? TaskTitle,
    int? ProjectId,
    string? ProjectName,
    string? ProjectColor,
    TimeEntryKind Kind,
    DateTime StartedAt,
    DateTime? EndedAt,
    string? Note,
    double Minutes);

public sealed record TimeEntryInput(
    int? TaskId,
    int? ProjectId,
    TimeEntryKind Kind,
    DateTime StartedAt,
    DateTime EndedAt,
    string? Note);

public sealed record StartTimerRequest(int? TaskId, int? ProjectId, TimeEntryKind? Kind, string? Note);
public sealed record PinnedTaskDto(int Slot, int TaskId, string Title, string? ProjectColor);
public sealed record TaskRef(int Id, string Title);

public sealed record TimerStateDto(
    TimeEntryDto? Running,
    IReadOnlyList<string> Warnings,
    InterruptionDto? OpenInterruption,
    TaskRef? LastTask,
    IReadOnlyList<PinnedTaskDto> Pinned,
    int WarningThresholdMinutes,
    DateTime ServerNow);

public sealed record InterruptionDto(
    int Id,
    DateTime StartedAt,
    DateTime? EndedAt,
    InterruptionSource? SourceType,
    int? PersonId,
    string? PersonName,
    string? Note,
    bool? CouldWait,
    int? PausedTimeEntryId,
    int? PausedTaskId,
    string? PausedTaskTitle,
    int? ConvertedTaskId,
    double Minutes);

/// <summary>Details captured by the interruption form. PersonName creates a Person when it doesn't exist yet.</summary>
public sealed record InterruptionDetails(
    InterruptionSource? SourceType,
    int? PersonId,
    string? PersonName,
    string? Note,
    bool? CouldWait);

public sealed record ResumeResponse(InterruptionDto Interruption, TimeEntryDto? Resumed);
public sealed record ConvertRequest(string? Title, int? ProjectId, WorkTaskStatus? Status);
public sealed record ConvertResponse(InterruptionDto Interruption, TaskDto Task, TimeEntryDto Entry);

/// <summary>Times are local wall-clock "HH:mm". ReviewReminderTime null = reminder off.</summary>
public sealed record SettingsDto(
    string WorkdayStart,
    string WorkdayEnd,
    int TimerWarningMinutes,
    string TimeZone,
    IReadOnlyList<string> ProjectColors,
    int MeetingMaxMinutes,
    string? ReviewReminderTime);

/// <summary>MeetingMaxMinutes null keeps the current value. ReviewReminderTime null or "" turns the reminder off.</summary>
public sealed record SettingsInput(
    string WorkdayStart,
    string WorkdayEnd,
    int TimerWarningMinutes,
    string TimeZone,
    int? MeetingMaxMinutes = null,
    string? ReviewReminderTime = null);

public sealed record GapDto(DateTime Start, DateTime End, double Minutes);
public sealed record ProjectTotalDto(int? ProjectId, string Name, string? Color, double Minutes);

/// <summary>
/// TaskMinutes is focus (task) work. Meeting time wins where a time entry overlaps a counted meeting, so
/// task + reactive + untracked + meetings + unlogged adds up to the logged part of the day.
/// </summary>
public sealed record DailyTotalsDto(
    DateOnly Date,
    double TaskMinutes,
    double ReactiveMinutes,
    double UntrackedMinutes,
    double TrackedMinutes,
    int InterruptionCount,
    double InterruptionMinutes,
    double UnloggedMinutes,
    IReadOnlyList<ProjectTotalDto> ByProject,
    double MeetingMinutes,
    int MeetingCount,
    double PlannedFocusMinutes,
    int FocusBlockCount,
    double FocusBlockMinutes,
    double LongestFocusBlockMinutes,
    int TaskSwitches);

public sealed record ReviewDto(
    DateOnly Date,
    string TimeZone,
    DateTime DayStart,
    DateTime DayEnd,
    DateTime WorkStart,
    DateTime WorkEnd,
    IReadOnlyList<TimeEntryDto> Entries,
    IReadOnlyList<InterruptionDto> Interruptions,
    IReadOnlyList<GapDto> Gaps,
    DailyTotalsDto Totals,
    IReadOnlyList<CalendarEventDto> Meetings,
    IReadOnlyList<OverlapDto> Overlaps,
    IReadOnlyList<FocusBlockDto> FocusBlocks,
    IReadOnlyList<TaskDto> Unfinished,
    PlannedVsActualDto Plan);

/// <summary>One-step gap fill: a project (as focus or reactive work) or "untracked".</summary>
public sealed record GapFillRequest(DateTime Start, DateTime End, int? ProjectId, TimeEntryKind Kind, string? Note);

// ---- Phase 2: calendar ----

/// <summary>CountsAsMeeting / IsPlannedFocus apply the current threshold and type; Excluded explains why not.</summary>
public sealed record CalendarEventDto(
    int Id,
    string Title,
    DateTime StartedAt,
    DateTime EndedAt,
    bool IsAllDay,
    int AttendeeCount,
    bool IsRecurring,
    bool IsFree,
    MeetingType MeetingType,
    int? ProjectId,
    string? ProjectName,
    string? ProjectColor,
    bool CountsAsMeeting,
    bool IsPlannedFocus,
    string? ExcludedReason,
    double Minutes);

public sealed record CalendarStatusDto(
    bool Configured,
    string? AddressHint,
    string? InternalDomains,
    DateTime? LastSyncAt,
    string? LastError,
    int EventCount,
    int SyncIntervalMinutes);

/// <summary>IcsUrl null keeps the saved address; "" removes it (and turns import off).</summary>
public sealed record CalendarSourceInput(string? IcsUrl, string? InternalDomains);

public sealed record SyncResultDto(int Added, int Updated, int Removed, int Total, DateTime SyncedAt, CalendarStatusDto Status);

public sealed record MeetingRuleDto(int Id, string TitlePattern, int ProjectId, string ProjectName, string ProjectColor, int MatchCount);
public sealed record MeetingRuleInput(string TitlePattern, int ProjectId);

public sealed record OverlapDto(int EntryId, string Title, int MeetingId, string MeetingTitle, double Minutes);
public sealed record FocusBlockDto(DateTime Start, DateTime End, double Minutes, int TaskId, string? TaskTitle);

// ---- Phase 2: planning and roll-over ----

public sealed record PlannedTaskDto(int TaskId, string Title, string? ProjectName, string? ProjectColor, WorkTaskStatus Status, bool Done, double Minutes);
public sealed record UnplannedTaskDto(int? TaskId, string Title, string? ProjectColor, double Minutes);

/// <summary>Planned = tasks on the Today list for the day. Actual = time logged that day.</summary>
public sealed record PlannedVsActualDto(
    DateOnly Date,
    IReadOnlyList<PlannedTaskDto> Planned,
    int PlannedCount,
    int PlannedDoneCount,
    double PlannedMinutes,
    double UnplannedTaskMinutes,
    double ReactiveMinutes,
    double MeetingMinutes,
    IReadOnlyList<UnplannedTaskDto> Unplanned);

/// <summary>ToDate null sends the tasks back to the backlog.</summary>
public sealed record RollOverRequest(IReadOnlyList<int> TaskIds, DateOnly? ToDate);
public sealed record RollOverResponse(int Moved, DateOnly? ToDate);

// ---- Phase 2: reports ----

public sealed record CountMinutesDto(string Key, string Label, int Count, double Minutes);

public sealed record InterruptionStatsDto(
    DateOnly From,
    DateOnly To,
    int Count,
    double Minutes,
    int CouldWaitCount,
    int UrgentCount,
    IReadOnlyList<CountMinutesDto> BySource,
    IReadOnlyList<CountMinutesDto> ByPerson,
    IReadOnlyList<int> ByHour,
    // [weekday 0 = Monday .. 6 = Sunday][hour 0-23] counts, by local start time.
    IReadOnlyList<IReadOnlyList<int>> Heatmap,
    IReadOnlyList<CountMinutesDto> ByDay);

public sealed record DayBreakdownDto(
    DateOnly Date,
    double FocusMinutes,
    double ReactiveMinutes,
    double UntrackedMinutes,
    double MeetingMinutes,
    double UnloggedMinutes,
    double PlannedFocusMinutes,
    int InterruptionCount,
    double InterruptionMinutes,
    int TaskSwitches,
    int FocusBlockCount,
    double LongestFocusBlockMinutes);

public sealed record PeriodTotalsDto(
    double FocusMinutes,
    double ReactiveMinutes,
    double UntrackedMinutes,
    double MeetingMinutes,
    double UnloggedMinutes,
    double PlannedFocusMinutes,
    int InterruptionCount,
    double InterruptionMinutes,
    int TaskSwitches,
    int FocusBlockCount);

public sealed record LongestBlockDto(DateOnly Date, DateTime Start, DateTime End, double Minutes, string? TaskTitle);

public sealed record WeeklySummaryDto(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    string TimeZone,
    IReadOnlyList<DayBreakdownDto> Days,
    PeriodTotalsDto Totals,
    // Average per week over the 4 weeks before this one.
    PeriodTotalsDto TrendAverage,
    int TrendWeeks,
    IReadOnlyList<ProjectTotalDto> ByProject,
    IReadOnlyList<CountMinutesDto> ByMeetingType,
    LongestBlockDto? LongestFocusBlock,
    InterruptionStatsDto Interruptions,
    int PlannedCount,
    int PlannedDoneCount,
    double PlannedMinutes,
    double UnplannedTaskMinutes);
