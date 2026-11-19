using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

/// <summary>Time entries added or edited after the fact (the timer itself lives in <see cref="TimerService"/>).</summary>
public sealed class TimeEntryService(WaypointDbContext db, SettingsService settings, TimeProvider clock)
{
    public async Task<List<TimeEntryDto>> ListAsync(DateOnly? date, CancellationToken ct = default)
    {
        var tz = await settings.TimeZoneAsync(ct);
        var now = Now();
        var day = LocalTime.DayBoundsUtc(date ?? LocalTime.LocalDate(now, tz), tz);
        var items = await Query()
            .Where(e => e.StartedAt < day.End && (e.EndedAt == null || e.EndedAt > day.Start))
            .OrderBy(e => e.StartedAt)
            .ToListAsync(ct);
        return items.Select(e => e.ToDto(now)).ToList();
    }

    /// <summary>Pieces of a carved entry shorter than this are dropped rather than kept as slivers.</summary>
    public static readonly TimeSpan MinPiece = TimeSpan.FromMinutes(1);

    /// <summary>Adds an entry. It wins over any entry it overlaps: those are trimmed, split around it, or replaced.</summary>
    public async Task<TimeEntryDto> CreateAsync(TimeEntryInput input, CancellationToken ct = default)
    {
        var entry = new TimeEntry();
        await ApplyAsync(entry, input, ct);
        var adjusted = await CarveAsync(entry, [new Interval(entry.StartedAt, entry.EndedAt!.Value)], ct);
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(entry.Id, ct)) with { Adjusted = adjusted };
    }

    /// <summary>
    /// Edits an entry. Only time it newly covers carves other entries, so changing the note (or shortening it)
    /// never touches its neighbours.
    /// </summary>
    public async Task<TimeEntryDto> UpdateAsync(int id, TimeEntryInput input, CancellationToken ct = default)
    {
        var entry = await db.TimeEntries.FindAsync([id], ct) ?? throw ApiException.NotFound("Time entry");
        if (entry.EndedAt is null)
            throw ApiException.BadRequest("Stop the running timer before editing it.");
        var before = new Interval(entry.StartedAt, entry.EndedAt.Value);
        await ApplyAsync(entry, input, ct);
        var added = Intervals.Subtract(new Interval(entry.StartedAt, entry.EndedAt!.Value), [before]);
        var adjusted = await CarveAsync(entry, added, ct);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct)) with { Adjusted = adjusted };
    }

    /// <summary>Resolves an existing overlap: this entry keeps its whole time and the entries it overlaps give way.</summary>
    public async Task<TimeEntryDto> KeepAsync(int id, CancellationToken ct = default)
    {
        var entry = await db.TimeEntries.FindAsync([id], ct) ?? throw ApiException.NotFound("Time entry");
        if (entry.EndedAt is null) throw ApiException.BadRequest("Stop the running timer first.");
        var adjusted = await CarveAsync(entry, [new Interval(entry.StartedAt, entry.EndedAt.Value)], ct);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct)) with { Adjusted = adjusted };
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entry = await db.TimeEntries.FindAsync([id], ct) ?? throw ApiException.NotFound("Time entry");
        await RemoveAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Makes room for <paramref name="winner"/> in <paramref name="ranges"/>: every other entry overlapping a range
    /// is trimmed (overlap at one end), split into before/after pieces (winner inside it), or removed (fully
    /// covered). A running timer that gets split keeps running from the end of the range. Does not save.
    /// Returns a short description of each change, for the UI.
    /// </summary>
    private async Task<IReadOnlyList<string>> CarveAsync(TimeEntry winner, IReadOnlyList<Interval> ranges, CancellationToken ct)
    {
        var notes = new List<string>();
        foreach (var range in ranges.Where(r => !r.IsEmpty))
        {
            var others = await db.TimeEntries.Include(e => e.Task).Include(e => e.Project)
                .Where(e => e.Id != winner.Id && e.StartedAt < range.End && (e.EndedAt == null || e.EndedAt > range.Start))
                .ToListAsync(ct);
            foreach (var o in others)
            {
                if (ReferenceEquals(o, winner)) continue;
                var name = $"\"{o.Task?.Title ?? o.Project?.Name ?? "Untracked"}\"";
                var keepBefore = range.Start - o.StartedAt >= MinPiece;
                var keepAfter = o.EndedAt is null || o.EndedAt.Value - range.End >= MinPiece;

                if (keepBefore && keepAfter)
                {
                    db.TimeEntries.Add(new TimeEntry
                    {
                        TaskId = o.TaskId, ProjectId = o.ProjectId, Kind = o.Kind, Note = o.Note,
                        StartedAt = range.End, EndedAt = o.EndedAt,
                    });
                    o.EndedAt = range.Start;
                    notes.Add($"Split {name} around this entry");
                }
                else if (keepBefore)
                {
                    o.EndedAt = range.Start;
                    notes.Add($"Trimmed the end of {name}");
                }
                else if (keepAfter)
                {
                    o.StartedAt = range.End;
                    notes.Add($"Trimmed the start of {name}");
                }
                else
                {
                    await RemoveAsync(o, ct);
                    notes.Add($"Replaced {name}");
                }
            }
            // Later ranges must see pieces created here (only possible when editing a saved entry).
            if (winner.Id != 0) await db.SaveChangesAsync(ct);
        }
        return notes.Distinct().ToList();
    }

    private async Task RemoveAsync(TimeEntry entry, CancellationToken ct)
    {
        // Interruptions that paused this entry keep their own record; just drop the link.
        var paused = await db.Interruptions.Where(i => i.PausedTimeEntryId == entry.Id).ToListAsync(ct);
        foreach (var i in paused) i.PausedTimeEntryId = null;
        db.TimeEntries.Remove(entry);
    }

    /// <summary>Fills a gap from the daily review in one step.</summary>
    public Task<TimeEntryDto> FillGapAsync(GapFillRequest request, CancellationToken ct = default) =>
        CreateAsync(new TimeEntryInput(null, request.Kind == TimeEntryKind.Untracked ? null : request.ProjectId,
            request.Kind, request.Start, request.End, request.Note), ct);

    private async Task<TimeEntryDto> GetAsync(int id, CancellationToken ct) =>
        (await Query().FirstAsync(e => e.Id == id, ct)).ToDto(Now());

    private async Task ApplyAsync(TimeEntry entry, TimeEntryInput input, CancellationToken ct)
    {
        var start = Mapping.TruncateToSecond(Mapping.Utc(input.StartedAt));
        var end = Mapping.TruncateToSecond(Mapping.Utc(input.EndedAt));
        if (end <= start) throw ApiException.BadRequest("End must be after start.");
        if (end - start > TimeSpan.FromHours(24)) throw ApiException.BadRequest("A time entry can't be longer than 24 hours.");
        // A small allowance (1 min) covers clock skew; the rest is the configurable future window.
        var futureMinutes = (await settings.GetAsync(ct)).FutureEntryMaxMinutes;
        if (end > Now().AddMinutes(futureMinutes + 1))
            throw ApiException.BadRequest(futureMinutes == 0
                ? "Time entries can't end in the future."
                : $"Time entries can end at most {FormatLimit(futureMinutes)} from now (see Projects & settings).");

        int? projectId = input.ProjectId;
        if (input.TaskId is not null)
        {
            var task = await db.Tasks.FindAsync([input.TaskId.Value], ct) ?? throw ApiException.BadRequest("Unknown task.");
            projectId ??= task.ProjectId;
        }
        if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw ApiException.BadRequest("Unknown project.");
        if (input.Kind != TimeEntryKind.Untracked && input.TaskId is null && projectId is null)
            throw ApiException.BadRequest("Pick a task or a project (or mark the time as untracked).");

        entry.TaskId = input.TaskId;
        entry.ProjectId = projectId;
        entry.Kind = input.Kind;
        entry.StartedAt = start;
        entry.EndedAt = end;
        entry.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
    }

    private static string FormatLimit(int minutes) =>
        minutes % 60 == 0 ? $"{minutes / 60} hour{(minutes == 60 ? "" : "s")}" : $"{minutes} minutes";

    private IQueryable<TimeEntry> Query() => db.TimeEntries.Include(e => e.Task).Include(e => e.Project);

    private DateTime Now() => Mapping.TruncateToSecond(clock.GetUtcNow().UtcDateTime);
}
