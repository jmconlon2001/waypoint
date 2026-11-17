using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Data;

namespace Waypoint.Api.Tests;

public class MigrationTests
{
    /// <summary>
    /// Fails when the entity model changed without a migration (run `dotnet ef migrations add <Name>`).
    /// Needs no database: it compares the model with the migrations' snapshot.
    /// </summary>
    [Fact]
    public void Model_has_no_pending_changes()
    {
        var options = new DbContextOptionsBuilder<WaypointDbContext>()
            .UseNpgsql("Host=localhost;Database=waypoint_model_check")
            .Options;
        using var db = new WaypointDbContext(options);
        Assert.False(db.Database.HasPendingModelChanges(), "The EF model differs from the migration snapshot.");
    }
}
