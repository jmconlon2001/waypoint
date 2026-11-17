using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Services;

namespace Waypoint.Api.Tests;

public class ServiceTests
{
    [Fact]
    public async Task Quick_add_resolves_project_and_priority()
    {
        using var app = new TestApp();
        var r = await app.Tasks.QuickAddAsync(new QuickAddRequest("Review AG failover #origination !1", Today: true));

        Assert.Null(r.Warning);
        Assert.Equal("Review AG failover", r.Task.Title);
        Assert.Equal("Origination", r.Task.ProjectName);
        Assert.Equal(1, r.Task.Priority);
        Assert.Equal(WorkTaskStatus.Today, r.Task.Status);
        Assert.Equal(new DateOnly(2026, 11, 17), r.Task.PlannedDate);
    }

    [Fact]
    public async Task Quick_add_with_unknown_project_warns_and_still_adds()
    {
        using var app = new TestApp();
        var r = await app.Tasks.QuickAddAsync(new QuickAddRequest("Lunch #nope", Today: false));
        Assert.NotNull(r.Warning);
        Assert.Null(r.Task.ProjectId);
        Assert.Equal(WorkTaskStatus.Backlog, r.Task.Status);
    }

    [Fact]
    public async Task Starting_a_timer_stops_the_running_one()
    {
        using var app = new TestApp();
        var a = (await app.Tasks.QuickAddAsync(new QuickAddRequest("A #admin", true))).Task;
        var b = (await app.Tasks.QuickAddAsync(new QuickAddRequest("B #admin", true))).Task;

        await app.Timer.StartAsync(new StartTimerRequest(a.Id, null, null, null));
        app.Clock.Advance(TimeSpan.FromMinutes(25));
        var state = await app.Timer.StartAsync(new StartTimerRequest(b.Id, null, null, null));

        Assert.Equal(b.Id, state.Running!.TaskId);
        Assert.Equal(1, await app.Db.TimeEntries.CountAsync(e => e.EndedAt == null));
        var first = await app.Db.TimeEntries.SingleAsync(e => e.TaskId == a.Id);
        Assert.Equal(TimeSpan.FromMinutes(25), first.EndedAt - first.StartedAt);
        Assert.Equal(WorkTaskStatus.Today, (await app.Db.Tasks.FindAsync(a.Id))!.Status);
        Assert.Equal(WorkTaskStatus.Doing, (await app.Db.Tasks.FindAsync(b.Id))!.Status);
    }

    [Fact]
    public async Task Timer_state_reports_long_running_warning()
    {
        using var app = new TestApp();
        var a = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Deep work #origination", true))).Task;
        await app.Timer.StartAsync(new StartTimerRequest(a.Id, null, null, null));
        app.Clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(1));
        var state = await app.Timer.GetStateAsync();
        Assert.Contains("long-running", state.Warnings);
    }

    [Fact]
    public async Task Interrupt_pauses_and_back_to_work_resumes_the_task()
    {
        using var app = new TestApp();
        var task = (await app.Tasks.QuickAddAsync(new QuickAddRequest("Write controls memo #sox", true))).Task;
        await app.Timer.StartAsync(new StartTimerRequest(task.Id, null, null, null));
        app.Clock.Advance(TimeSpan.FromMinutes(30));

        var i = await app.Interruptions.StartAsync(null);
        Assert.Null((await app.Timer.GetStateAsync()).Running);
        Assert.Equal(task.Id, i.PausedTaskId);

        await app.Interruptions.UpdateAsync(i.Id, new InterruptionDetails(InterruptionSource.WalkUp, null, "Dana", "Access question", CouldWait: true));
        app.Clock.Advance(TimeSpan.FromMinutes(12));
        var resumed = await app.Interruptions.ResumeAsync(i.Id, null);

        Assert.Equal(12, resumed.Interruption.Minutes);
        Assert.Equal("Dana", resumed.Interruption.PersonName);
        Assert.NotNull(resumed.Resumed);
        Assert.Equal(task.Id, resumed.Resumed!.TaskId);
        Assert.Equal(2, await app.Db.TimeEntries.CountAsync(e => e.TaskId == task.Id));
        Assert.Single(await app.People.SearchAsync("da"));
    }

    [Fact]
    public async Task Update_requires_a_source_type()
    {
        using var app = new TestApp();
        var i = await app.Interruptions.StartAsync(null);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            app.Interruptions.UpdateAsync(i.Id, new InterruptionDetails(null, null, null, "x", null)));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Converting_an_interruption_creates_a_task_with_its_time()
    {
        using var app = new TestApp();
        var i = await app.Interruptions.StartAsync(new InterruptionDetails(InterruptionSource.GoogleChat, null, null, "Prod access for new hire", null));
        app.Clock.Advance(TimeSpan.FromMinutes(18));
        await app.Interruptions.ResumeAsync(i.Id, null);

        var r = await app.Interruptions.ConvertAsync(i.Id, new ConvertRequest(null, app.ProjectId("Admin"), null));

        Assert.Equal("Prod access for new hire", r.Task.Title);
        Assert.Equal(WorkTaskStatus.Done, r.Task.Status);
        Assert.Equal(18, r.Task.TrackedMinutes);
        Assert.Equal(TimeEntryKind.Reactive, r.Entry.Kind);
        Assert.Equal(r.Task.Id, r.Interruption.ConvertedTaskId);
        await Assert.ThrowsAsync<ApiException>(() => app.Interruptions.ConvertAsync(i.Id, new ConvertRequest(null, null, null)));
    }

    [Fact]
    public async Task Review_shows_gaps_and_filling_one_removes_it()
    {
        // 11:00 local; work day starts 08:45, so there's a 2h15m gap with nothing logged.
        using var app = new TestApp(new DateTime(2026, 11, 17, 16, 0, 0, DateTimeKind.Utc));
        var review = await app.Review.GetDayAsync(null);
        var gap = Assert.Single(review.Gaps);
        Assert.Equal(135, gap.Minutes);

        await app.Entries.FillGapAsync(new GapFillRequest(gap.Start, gap.End, app.ProjectId("Origination"), TimeEntryKind.Task, null));

        review = await app.Review.GetDayAsync(null);
        Assert.Empty(review.Gaps);
        Assert.Equal(135, review.Totals.TaskMinutes);
        Assert.Equal("Origination", review.Totals.ByProject.Single().Name);
    }

    [Fact]
    public async Task Manual_entries_are_validated()
    {
        using var app = new TestApp();
        var start = app.Clock.UtcNow.AddHours(-2);
        await Assert.ThrowsAsync<ApiException>(() => app.Entries.CreateAsync(
            new TimeEntryInput(null, null, TimeEntryKind.Task, start, start.AddHours(1), null)));   // no task/project
        await Assert.ThrowsAsync<ApiException>(() => app.Entries.CreateAsync(
            new TimeEntryInput(null, null, TimeEntryKind.Untracked, start, start.AddMinutes(-5), null))); // end before start
        var ok = await app.Entries.CreateAsync(new TimeEntryInput(null, null, TimeEntryKind.Untracked, start, start.AddMinutes(30), "lunch"));
        Assert.Equal(30, ok.Minutes);
    }

    [Fact]
    public async Task Pinning_takes_the_slot_from_another_task()
    {
        using var app = new TestApp();
        var a = (await app.Tasks.QuickAddAsync(new QuickAddRequest("A", true))).Task;
        var b = (await app.Tasks.QuickAddAsync(new QuickAddRequest("B", true))).Task;
        await app.Tasks.PinAsync(a.Id, 1);
        await app.Tasks.PinAsync(b.Id, 1);

        var state = await app.Timer.StartPinnedAsync(1);
        Assert.Equal(b.Id, state.Running!.TaskId);
        Assert.Null((await app.Db.Tasks.FindAsync(a.Id))!.PinSlot);
    }

    [Fact]
    public async Task Completing_a_task_stops_its_timer()
    {
        using var app = new TestApp();
        var a = (await app.Tasks.QuickAddAsync(new QuickAddRequest("A", true))).Task;
        await app.Timer.StartAsync(new StartTimerRequest(a.Id, null, null, null));
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        var done = await app.Tasks.SetStatusAsync(a.Id, WorkTaskStatus.Done);
        Assert.NotNull(done.CompletedAt);
        Assert.Null((await app.Timer.GetStateAsync()).Running);
        Assert.Contains(await app.Tasks.TodayAsync(), t => t.Id == a.Id); // done today still shows
    }
}
