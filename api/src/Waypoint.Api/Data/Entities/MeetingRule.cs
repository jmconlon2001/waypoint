namespace Waypoint.Api.Data.Entities;

/// <summary>Maps meetings to a project when the title contains <see cref="TitlePattern"/> (case-insensitive).</summary>
public class MeetingRule
{
    public int Id { get; set; }
    public string TitlePattern { get; set; } = "";
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
}
