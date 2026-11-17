namespace Waypoint.Api.Data.Entities;

/// <summary>Single-row settings table (Id is always 1).</summary>
public class Setting
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public TimeOnly WorkdayStart { get; set; } = new(8, 45);
    public TimeOnly WorkdayEnd { get; set; } = new(17, 30);
    public int TimerWarningMinutes { get; set; } = 180;
    /// <summary>IANA time zone used to display times and define "a day".</summary>
    public string TimeZone { get; set; } = "America/New_York";
}
