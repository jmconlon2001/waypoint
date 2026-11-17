using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Data;

public static class DbSeeder
{
    /// <summary>Default projects, created only when the projects table is empty.</summary>
    public static readonly IReadOnlyList<(string Name, string Color)> DefaultProjects =
    [
        ("SOX/J-SOX", "#e11d48"),
        ("Origination", "#7c3aed"),
        ("Extractor migration", "#f97316"),
        ("People management", "#c026d3"),
        ("Admin", "#64748b"),
    ];

    public static async Task SeedAsync(WaypointDbContext db, CancellationToken ct = default)
    {
        if (!await db.Settings.AnyAsync(ct))
            db.Settings.Add(new Setting());

        if (!await db.Projects.AnyAsync(ct))
            foreach (var (name, color) in DefaultProjects)
                db.Projects.Add(new Project { Name = name, Color = color });

        await db.SaveChangesAsync(ct);
    }
}
