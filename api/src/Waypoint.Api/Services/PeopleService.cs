using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Services;

public sealed class PeopleService(WaypointDbContext db)
{
    public async Task<List<PersonDto>> SearchAsync(string? q, CancellationToken ct = default)
    {
        var query = db.People.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }
        return (await query.OrderBy(p => p.Name).Take(50).ToListAsync(ct)).Select(p => p.ToDto()).ToList();
    }

    public async Task<PersonDto> CreateAsync(PersonInput input, CancellationToken ct = default) =>
        (await GetOrCreateAsync(input.Name, ct)).ToDto();

    public async Task<PersonDto> RenameAsync(int id, PersonInput input, CancellationToken ct = default)
    {
        var p = await db.People.FindAsync([id], ct) ?? throw ApiException.NotFound("Person");
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) throw ApiException.BadRequest("Name is required.");
        if (await db.People.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct))
            throw ApiException.Conflict($"'{name}' already exists.");
        p.Name = name;
        await db.SaveChangesAsync(ct);
        return p.ToDto();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var p = await db.People.FindAsync([id], ct) ?? throw ApiException.NotFound("Person");
        db.People.Remove(p);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Finds a person by name (case-insensitive) or adds and saves them.</summary>
    internal async Task<Person> GetOrCreateAsync(string? name, CancellationToken ct)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) throw ApiException.BadRequest("Name is required.");
        if (n.Length > 100) throw ApiException.BadRequest("Name is too long (100 characters max).");
        var lower = n.ToLower();
        var existing = await db.People.FirstOrDefaultAsync(p => p.Name.ToLower() == lower, ct)
                       ?? db.People.Local.FirstOrDefault(p => p.Name.ToLower() == lower);
        if (existing is not null) return existing;
        var person = new Person { Name = n };
        db.People.Add(person);
        await db.SaveChangesAsync(ct);
        return person;
    }
}
