using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Contracts;

// ---- Calendar ----

public sealed record CalendarEventDto(
    int Id,
    string Source,
    string Title,
    DateTime StartedAt,
    DateTime EndedAt,
    bool AllDay,
    int AttendeeCount,
    bool Recurring,
    string EventType,
    MeetingType MeetingType,
    int? ProjectId,
    string? ProjectName,
    string? ProjectColor,
    // Counts as meeting time (FR-C2).
    bool Counted,
    // Why it doesn't count (all-day, longer than N min, focus time, declined…), or null.
    string? ExclusionReason,
    // Google focus-time block (FR-C4).
    bool PlannedFocus,
    double Minutes);

/// <summary>A task timer that overlaps a counted meeting (time is probably double-counted).</summary>
public sealed record MeetingOverlapDto(int TimeEntryId, int CalendarEventId, string EventTitle, DateTime Start, DateTime End, double Minutes);

public sealed record CalendarStatusDto(
    CalendarSourceKind Source,
    bool GoogleConfigured,
    bool GoogleConnected,
    string? GoogleAccount,
    bool ICalConfigured,
    DateTime? LastSyncAt,
    string? LastSyncError,
    int EventCount,
    int SyncIntervalMinutes);

public sealed record SyncResultDto(CalendarSourceKind Source, int Added, int Updated, int Removed, int Total, DateTime SyncedAt);

public sealed record MeetingRuleDto(int Id, string Pattern, int ProjectId, string? ProjectName, string? ProjectColor, int SortOrder, bool Active);
public sealed record MeetingRuleInput(string Pattern, int ProjectId, int? SortOrder, bool? Active);
public sealed record RuleTestRequest(string Title);
public sealed record RuleTestResult(int? RuleId, int? ProjectId, string? ProjectName);

// ---- Tasks ----

/// <summary>Rolls unfinished tasks planned on/before <see cref="Date"/> (default today) to the next day; skipped ones go back to the backlog.</summary>
public sealed record RolloverRequest(DateOnly? Date, IReadOnlyList<int>? SkipTaskIds);
public sealed record RolloverResult(DateOnly ToDate, int Rolled, int Unplanned);

// ---- Metrics ----

public sealed record FocusBlockDto(int TaskId, string? TaskTitle, DateTime Start, DateTime End, double Minutes);

public sealed record DailyAggregateDto(
    DateOnly Date,
    double TaskMinutes,
    double ReactiveMinutes,
    double UntrackedMinutes,
    double MeetingMinutes,
    double PlannedFocusMinutes,
    double UnloggedMinutes,
    int InterruptionCount,
    double InterruptionMinutes,
    int CouldWaitCount,
    int TaskSwitches,
    int FocusBlocks,
    double LongestFocusBlockMinutes,
    int PlannedTasks,
    int PlannedTasksWorked,
    double PlannedTaskMinutes,
    double UnplannedTaskMinutes,
    int MeetingCount,
    DateTime ComputedAt);

public sealed record NamedCountDto(string Key, string Label, int Count, double Minutes, int CouldWaitCount);

public sealed record InterruptionStatsDto(
    DateOnly From,
    DateOnly To,
    int Count,
    double Minutes,
    int CouldWaitCount,
    IReadOnlyList<NamedCountDto> BySource,
    IReadOnlyList<NamedCountDto> ByPerson,
    // [weekday Monday=0][hour 0-23] counts in local time.
    int[][] Heatmap,
    IReadOnlyList<NamedCountDto> ByDay);

public sealed record KindTotalsDto(double Meetings, double Focus, double Reactive, double Untracked, double Unlogged, double PlannedFocus);

public sealed record WeekTrendDto(DateOnly WeekStart, double Meetings, double Focus, double Reactive, double Untracked, double Unlogged, int Interruptions, int FocusBlocks, int TaskSwitches);

public sealed record PlannedTaskDto(
    int TaskId,
    string Title,
    string? ProjectName,
    string? ProjectColor,
    WorkTaskStatus Status,
    IReadOnlyList<DateOnly> PlannedDates,
    double MinutesOnPlannedDays,
    double MinutesInRange);

public sealed record WeeklyReportDto(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    IReadOnlyList<DailyAggregateDto> Days,
    IReadOnlyList<ProjectTotalDto> ByProject,
    KindTotalsDto Kinds,
    InterruptionStatsDto Interruptions,
    FocusBlockDto? LongestFocusBlock,
    int FocusBlocks,
    int TaskSwitches,
    IReadOnlyList<WeekTrendDto> Trend,
    IReadOnlyList<PlannedTaskDto> PlannedVsActual,
    double PlannedTaskMinutes,
    double UnplannedTaskMinutes);

public sealed record RecomputeRequest(DateOnly From, DateOnly To);
public sealed record RecomputeResult(DateOnly From, DateOnly To, int Days);
