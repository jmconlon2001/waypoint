namespace Waypoint.Api.Domain;

/// <summary>
/// The fixed set of project colors. Chosen to stay clear of the UI accent hues
/// (blue / cyan / green) and of the warning amber used for interruptions.
/// </summary>
public static class ProjectPalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#e11d48", // rose
        "#7c3aed", // violet
        "#f97316", // orange
        "#c026d3", // fuchsia
        "#64748b", // slate
        "#be185d", // raspberry
        "#a16207", // bronze
        "#4f46e5", // indigo
    ];

    public static bool IsValid(string? color) =>
        color is not null && Colors.Contains(color.ToLowerInvariant());

    public static string Normalize(string? color, int fallbackIndex = 0) =>
        IsValid(color) ? color!.ToLowerInvariant() : Colors[Math.Abs(fallbackIndex) % Colors.Count];
}
