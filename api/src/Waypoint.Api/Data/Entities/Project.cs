namespace Waypoint.Api.Data.Entities;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Hex color from the fixed project palette (see <see cref="Domain.ProjectPalette"/>).</summary>
    public string Color { get; set; } = "#64748b";
    public bool Active { get; set; } = true;
}
