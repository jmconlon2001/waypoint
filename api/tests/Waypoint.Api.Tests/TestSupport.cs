using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;
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
    public MetricsService Metrics { get; }
    public ExportService Export { get; }
    public CalendarService Calendar { get; }
    public FakeFeed Feed { get; } = new();

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
        Metrics = new MetricsService(Db, Settings, Clock);
        Review = new ReviewService(Metrics, Settings, Clock);
        Export = new ExportService(Db, Metrics, Settings);
        Calendar = new CalendarService(Db, Settings, new ICalendarFeed[] { Feed }, Options.Create(new CalendarOptions()), Clock, NullLogger<CalendarService>.Instance);
    }

    public int ProjectId(string name) => Db.Projects.Single(p => p.Name == name).Id;

    public void Dispose() => Db.Dispose();
}

/// <summary>An iCal-kind feed that returns whatever the test puts in <see cref="Events"/>.</summary>
public sealed class FakeFeed : ICalendarFeed
{
    public List<ImportedEvent> Events { get; } = [];
    public CalendarSourceKind Kind => CalendarSourceKind.ICal;
    public string SourceName => "ical";
    public bool IsConfigured(Setting settings) => true;
    public Task<IReadOnlyList<ImportedEvent>> FetchAsync(Setting settings, DateTime fromUtc, DateTime toUtc, TimeZoneInfo tz, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ImportedEvent>>(Events.ToList());

    public static ImportedEvent Meeting(string id, string title, DateTime start, int minutes, int attendees = 3, bool allDay = false, string type = "default") =>
        new(id, title, start, start.AddMinutes(minutes), allDay,
            Enumerable.Range(0, attendees).Select(i => $"p{i}@cfs.canon.com").ToList(), false, type, false, false);
}
