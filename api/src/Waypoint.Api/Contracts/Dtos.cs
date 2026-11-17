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

/// <summary>Times are local wall-clock "HH:mm".</summary>
public sealed record SettingsDto(string WorkdayStart, string WorkdayEnd, int TimerWarningMinutes, string TimeZone, IReadOnlyList<string> ProjectColors);
public sealed record SettingsInput(string WorkdayStart, string WorkdayEnd, int TimerWarningMinutes, string TimeZone);

public sealed record GapDto(DateTime Start, DateTime End, double Minutes);
public sealed record ProjectTotalDto(int? ProjectId, string Name, string? Color, double Minutes);

public sealed record DailyTotalsDto(
    DateOnly Date,
    double TaskMinutes,
    double ReactiveMinutes,
    double UntrackedMinutes,
    double TrackedMinutes,
    int InterruptionCount,
    double InterruptionMinutes,
    double UnloggedMinutes,
    IReadOnlyList<ProjectTotalDto> ByProject);

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
    DailyTotalsDto Totals);

/// <summary>One-step gap fill: a project (as focus or reactive work) or "untracked".</summary>
public sealed record GapFillRequest(DateTime Start, DateTime End, int? ProjectId, TimeEntryKind Kind, string? Note);
