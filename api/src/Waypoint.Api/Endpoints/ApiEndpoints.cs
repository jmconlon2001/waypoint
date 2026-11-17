using Waypoint.Api.Contracts;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Services;

namespace Waypoint.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapWaypointApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("Health");

        // Settings
        api.MapGet("/settings", (SettingsService s, CancellationToken ct) => s.GetDtoAsync(ct));
        api.MapPut("/settings", (SettingsInput input, SettingsService s, CancellationToken ct) => s.UpdateAsync(input, ct));

        // Projects
        var projects = api.MapGroup("/projects");
        projects.MapGet("/", (bool? includeInactive, ProjectService s, CancellationToken ct) => s.ListAsync(includeInactive ?? false, ct));
        projects.MapPost("/", async (ProjectInput input, ProjectService s, CancellationToken ct) =>
        {
            var p = await s.CreateAsync(input, ct);
            return Results.Created($"/api/projects/{p.Id}", p);
        });
        projects.MapPut("/{id:int}", (int id, ProjectInput input, ProjectService s, CancellationToken ct) => s.UpdateAsync(id, input, ct));
        projects.MapDelete("/{id:int}", async (int id, ProjectService s, CancellationToken ct) =>
        {
            var archived = await s.DeleteAsync(id, ct);
            return archived is null ? Results.NoContent() : Results.Ok(archived);
        });

        // People (interruption autocomplete)
        var people = api.MapGroup("/people");
        people.MapGet("/", (string? q, PeopleService s, CancellationToken ct) => s.SearchAsync(q, ct));
        people.MapPost("/", (PersonInput input, PeopleService s, CancellationToken ct) => s.CreateAsync(input, ct));
        people.MapPut("/{id:int}", (int id, PersonInput input, PeopleService s, CancellationToken ct) => s.RenameAsync(id, input, ct));
        people.MapDelete("/{id:int}", async (int id, PeopleService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // Tasks
        var tasks = api.MapGroup("/tasks");
        tasks.MapGet("/", (string? view, WorkTaskStatus? status, int? projectId, string? q, TaskService s, CancellationToken ct) =>
            s.ListAsync(view, status, projectId, q, ct));
        tasks.MapGet("/today", (TaskService s, CancellationToken ct) => s.TodayAsync(ct));
        tasks.MapGet("/{id:int}", (int id, TaskService s, CancellationToken ct) => s.GetAsync(id, ct));
        tasks.MapPost("/", async (TaskInput input, TaskService s, CancellationToken ct) =>
        {
            var t = await s.CreateAsync(input, ct);
            return Results.Created($"/api/tasks/{t.Id}", t);
        });
        tasks.MapPost("/quick-add", (QuickAddRequest input, TaskService s, CancellationToken ct) => s.QuickAddAsync(input, ct));
        tasks.MapPut("/{id:int}", (int id, TaskInput input, TaskService s, CancellationToken ct) => s.UpdateAsync(id, input, ct));
        tasks.MapPost("/{id:int}/plan", (int id, PlanRequest input, TaskService s, CancellationToken ct) => s.PlanAsync(id, input.Today, ct));
        tasks.MapPost("/{id:int}/status", (int id, StatusRequest input, TaskService s, CancellationToken ct) => s.SetStatusAsync(id, input.Status, ct));
        tasks.MapPost("/{id:int}/pin", (int id, PinRequest input, TaskService s, CancellationToken ct) => s.PinAsync(id, input.Slot, ct));
        tasks.MapDelete("/{id:int}", async (int id, TaskService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // Timer
        var timer = api.MapGroup("/timer");
        timer.MapGet("/", (TimerService s, CancellationToken ct) => s.GetStateAsync(ct));
        timer.MapPost("/start", (StartTimerRequest input, TimerService s, CancellationToken ct) => s.StartAsync(input, ct));
        timer.MapPost("/pinned/{slot:int}", (int slot, TimerService s, CancellationToken ct) => s.StartPinnedAsync(slot, ct));
        timer.MapPost("/stop", (TimerService s, CancellationToken ct) => s.StopAsync(ct));

        // Time entries
        var entries = api.MapGroup("/time-entries");
        entries.MapGet("/", (DateOnly? date, TimeEntryService s, CancellationToken ct) => s.ListAsync(date, ct));
        entries.MapPost("/", async (TimeEntryInput input, TimeEntryService s, CancellationToken ct) =>
        {
            var e = await s.CreateAsync(input, ct);
            return Results.Created($"/api/time-entries/{e.Id}", e);
        });
        entries.MapPost("/fill-gap", (GapFillRequest input, TimeEntryService s, CancellationToken ct) => s.FillGapAsync(input, ct));
        entries.MapPut("/{id:int}", (int id, TimeEntryInput input, TimeEntryService s, CancellationToken ct) => s.UpdateAsync(id, input, ct));
        entries.MapDelete("/{id:int}", async (int id, TimeEntryService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // Interruptions
        var interruptions = api.MapGroup("/interruptions");
        interruptions.MapGet("/", (DateOnly? date, InterruptionService s, CancellationToken ct) => s.ListAsync(date, ct));
        interruptions.MapGet("/{id:int}", (int id, InterruptionService s, CancellationToken ct) => s.GetAsync(id, ct));
        interruptions.MapPost("/start", (InterruptionDetails? input, InterruptionService s, CancellationToken ct) => s.StartAsync(input, ct));
        interruptions.MapPut("/{id:int}", (int id, InterruptionDetails input, InterruptionService s, CancellationToken ct) => s.UpdateAsync(id, input, ct));
        interruptions.MapPut("/{id:int}/times", (int id, TimesInput input, InterruptionService s, CancellationToken ct) =>
            s.SetTimesAsync(id, input.StartedAt, input.EndedAt, ct));
        interruptions.MapPost("/{id:int}/resume", (int id, InterruptionDetails? input, InterruptionService s, CancellationToken ct) => s.ResumeAsync(id, input, ct));
        interruptions.MapPost("/{id:int}/convert", (int id, ConvertRequest input, InterruptionService s, CancellationToken ct) => s.ConvertAsync(id, input, ct));
        interruptions.MapDelete("/{id:int}", async (int id, InterruptionService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // Daily review and reports
        api.MapGet("/review", (DateOnly? date, ReviewService s, CancellationToken ct) => s.GetDayAsync(date, ct));
        var reports = api.MapGroup("/reports");
        reports.MapGet("/daily", (DateOnly? date, ReportService s, CancellationToken ct) => s.GetDailyAsync(date, ct));
        reports.MapGet("/weekly", (DateOnly? date, ReportService s, CancellationToken ct) => s.GetWeekAsync(date, ct));
        reports.MapGet("/interruptions", (DateOnly? from, DateOnly? to, ReportService s, CancellationToken ct) => s.GetInterruptionStatsAsync(from, to, ct));
        reports.MapGet("/plan", (DateOnly? date, ReportService s, CancellationToken ct) => s.GetPlanAsync(date, ct));

        // CSV export: time-entries, interruptions, meetings, daily
        api.MapGet("/export/{kind}.csv", async (string kind, DateOnly? from, DateOnly? to, ReportService s, CancellationToken ct) =>
        {
            var (content, fileName) = await s.ExportCsvAsync(kind, from, to, ct);
            return Results.File(content, "text/csv; charset=utf-8", fileName);
        });

        // Roll-over of unfinished Today tasks (end-of-day review)
        tasks.MapPost("/roll-over", (RollOverRequest input, TaskService s, CancellationToken ct) => s.RollOverAsync(input, ct));

        // Calendar (read-only Google Calendar import)
        var calendar = api.MapGroup("/calendar");
        calendar.MapGet("/status", (CalendarService s, CancellationToken ct) => s.GetStatusAsync(ct));
        calendar.MapPut("/source", (CalendarSourceInput input, CalendarService s, CancellationToken ct) => s.SetSourceAsync(input, ct));
        calendar.MapPost("/sync", (CalendarService s, CancellationToken ct) => s.SyncAsync(ct));
        calendar.MapGet("/events", (DateOnly? date, CalendarService s, CancellationToken ct) => s.ListAsync(date, ct));
        calendar.MapGet("/rules", (CalendarService s, CancellationToken ct) => s.ListRulesAsync(ct));
        calendar.MapPost("/rules", (MeetingRuleInput input, CalendarService s, CancellationToken ct) => s.CreateRuleAsync(input, ct));
        calendar.MapPut("/rules/{id:int}", (int id, MeetingRuleInput input, CalendarService s, CancellationToken ct) => s.UpdateRuleAsync(id, input, ct));
        calendar.MapDelete("/rules/{id:int}", (int id, CalendarService s, CancellationToken ct) => s.DeleteRuleAsync(id, ct));

        return app;
    }

    public sealed record TimesInput(DateTime StartedAt, DateTime EndedAt);
}
