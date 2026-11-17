using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Data;
using Waypoint.Api.Integrations;
using Waypoint.Api.Services;

namespace Waypoint.Api.Tests;

/// <summary>A clock the tests can move.</summary>
public sealed class FakeClock(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Services wired to a fresh in-memory database with the default seed data.</summary>
public sealed class TestApp : IDisposable
{
    public WaypointDbContext Db { get; }
    public FakeClock Clock { get; }
    public SettingsService Settings { get; }
    public TaskService Tasks { get; }
    public TimerService Timer { get; }
    public PeopleService People { get; }
    public InterruptionService Interruptions { get; }
    public TimeEntryService Entries { get; }
    public ReviewService Review { get; }
    public ReportService Reports { get; }
    public CalendarService Calendar { get; }
    public FakeCalendarFeed Feed { get; } = new();

    /// <param name="utcNow">Defaults to Tue 17 Nov 2026 10:00 in New York (15:00 UTC).</param>
    public TestApp(DateTime? utcNow = null)
    {
        var options = new DbContextOptionsBuilder<WaypointDbContext>()
            .UseInMemoryDatabase($"waypoint-{Guid.NewGuid()}")
            .Options;
        Db = new WaypointDbContext(options);
        DbSeeder.SeedAsync(Db).GetAwaiter().GetResult();
        Clock = new FakeClock(utcNow ?? new DateTime(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc));
        Settings = new SettingsService(Db);
        Tasks = new TaskService(Db, Settings, Clock);
        Timer = new TimerService(Db, Settings, Clock);
        People = new PeopleService(Db);
        Interruptions = new InterruptionService(Db, Timer, People, Settings, Clock);
        Entries = new TimeEntryService(Db, Settings, Clock);
        Reports = new ReportService(Db, Settings, Clock);
        Review = new ReviewService(Reports, Tasks, Settings, Clock);
        Calendar = new CalendarService(Db, Settings, Feed, Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<CalendarService>.Instance);
    }

    public int ProjectId(string name) => Db.Projects.Single(p => p.Name == name).Id;

    public void Dispose() => Db.Dispose();
}

/// <summary>Serves a fixed iCalendar document instead of calling Google.</summary>
public sealed class FakeCalendarFeed : ICalendarFeed
{
    public string Ics { get; set; } = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n";
    public Exception? Error { get; set; }
    public Task<string> DownloadAsync(string url, CancellationToken ct = default) =>
        Error is null ? Task.FromResult(Ics) : Task.FromException<string>(Error);
}
