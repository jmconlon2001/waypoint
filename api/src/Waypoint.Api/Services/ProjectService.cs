using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

public sealed class ProjectService(WaypointDbContext db)
{
    public async Task<List<ProjectDto>> ListAsync(bool includeInactive, CancellationToken ct = default) =>
        (await db.Projects.Where(p => includeInactive || p.Active).OrderBy(p => p.Name).ToListAsync(ct))
            .Select(p => p.ToDto()).ToList();

    public async Task<ProjectDto> CreateAsync(ProjectInput input, CancellationToken ct = default)
    {
        var name = ValidName(input.Name);
        if (await db.Projects.AnyAsync(p => p.Name.ToLower() == name.ToLower(), ct))
            throw ApiException.Conflict($"A project named '{name}' already exists.");
        var count = await db.Projects.CountAsync(ct);
        var p = new Project { Name = name, Color = ProjectPalette.Normalize(input.Color, count), Active = input.Active ?? true };
        db.Projects.Add(p);
        await db.SaveChangesAsync(ct);
        return p.ToDto();
    }

    public async Task<ProjectDto> UpdateAsync(int id, ProjectInput input, CancellationToken ct = default)
    {
        var p = await db.Projects.FindAsync([id], ct) ?? throw ApiException.NotFound("Project");
        var name = ValidName(input.Name);
        if (await db.Projects.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct))
            throw ApiException.Conflict($"A project named '{name}' already exists.");
        p.Name = name;
        if (input.Color is not null)
        {
            if (!ProjectPalette.IsValid(input.Color)) throw ApiException.BadRequest("Pick one of the project palette colors.");
            p.Color = input.Color.ToLowerInvariant();
        }
        if (input.Active is not null) p.Active = input.Active.Value;
        await db.SaveChangesAsync(ct);
        return p.ToDto();
    }

    /// <summary>Deletes an unused project; a project with tasks or time is archived (made inactive) instead.</summary>
    public async Task<ProjectDto?> DeleteAsync(int id, CancellationToken ct = default)
    {
        var p = await db.Projects.FindAsync([id], ct) ?? throw ApiException.NotFound("Project");
        var used = await db.Tasks.AnyAsync(t => t.ProjectId == id, ct) || await db.TimeEntries.AnyAsync(e => e.ProjectId == id, ct);
        if (used)
        {
            p.Active = false;
            await db.SaveChangesAsync(ct);
            return p.ToDto();
        }
        db.Projects.Remove(p);
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static string ValidName(string? name)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) throw ApiException.BadRequest("Project name is required.");
        if (n.Length > 100) throw ApiException.BadRequest("Project name is too long (100 characters max).");
        return n;
    }
}
