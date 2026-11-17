using System.Text;
using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Data.Entities;

namespace Waypoint.Api.Data;

public class WaypointDbContext(DbContextOptions<WaypointDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<Interruption> Interruptions => Set<Interruption>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Color).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<WorkTask>(e =>
        {
            e.ToTable("tasks");
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.PlannedDate);
        });

        b.Entity<TimeEntry>(e =>
        {
            e.ToTable("time_entries");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => x.EndedAt);
        });

        b.Entity<Interruption>(e =>
        {
            e.ToTable("interruptions");
            e.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.PausedTimeEntry).WithMany().HasForeignKey(x => x.PausedTimeEntryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ConvertedTask).WithMany().HasForeignKey(x => x.ConvertedTaskId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.StartedAt);
        });

        b.Entity<Person>(e =>
        {
            e.ToTable("people");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Setting>(e =>
        {
            e.ToTable("settings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        });

        // snake_case column, key, foreign key and index names (PostgreSQL convention).
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
            foreach (var key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetDefaultName() ?? ""));
            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnakeCase(fk.GetDefaultName() ?? ""));
            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnakeCase(index.GetDefaultDatabaseName() ?? ""));
        }
    }

    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var prev = i > 0 ? name[i - 1] : '\0';
                if (i > 0 && prev != '_' && (char.IsLower(prev) || char.IsDigit(prev) ||
                    (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(prev))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
