namespace Waypoint.Api.Data.Entities;

/// <summary>Title regex → project. The first active rule (by SortOrder) that matches wins.</summary>
public class MeetingRule
{
    public int Id { get; set; }
    public string Pattern { get; set; } = "";
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
}
