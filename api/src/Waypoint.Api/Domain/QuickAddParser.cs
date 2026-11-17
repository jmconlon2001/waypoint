using System.Text;

namespace Waypoint.Api.Domain;

public sealed record QuickAddParsed(string Title, string? ProjectToken, int? Priority);

/// <summary>
/// Parses quick-add text such as <c>Review AG failover #origination !1</c>:
/// the first <c>#token</c> names a project, <c>!1</c>-<c>!3</c> sets the priority,
/// everything else is the title.
/// </summary>
public static class QuickAddParser
{
    public static QuickAddParsed Parse(string? text)
    {
        string? project = null;
        int? priority = null;
        var words = new List<string>();

        foreach (var token in (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (project is null && token.Length > 1 && token[0] == '#' && Slug(token[1..]).Length > 0)
            {
                project = token[1..];
                continue;
            }
            if (token.Length == 2 && token[0] == '!' && token[1] is >= '1' and <= '3')
            {
                priority = token[1] - '0';
                continue;
            }
            words.Add(token);
        }

        return new QuickAddParsed(string.Join(' ', words), project, priority);
    }

    /// <summary>
    /// Resolves a project token against project names: exact slug match, then unique prefix,
    /// then unique substring. Returns null when nothing (or more than one project) matches.
    /// </summary>
    public static T? MatchProject<T>(string? token, IEnumerable<T> candidates, Func<T, string> name) where T : class
    {
        var t = Slug(token ?? "");
        if (t.Length == 0) return null;
        var list = candidates.Select(c => (Item: c, Slug: Slug(name(c)))).ToList();

        foreach (var tier in new Func<string, bool>[] { s => s == t, s => s.StartsWith(t, StringComparison.Ordinal), s => s.Contains(t, StringComparison.Ordinal) })
        {
            var matches = list.Where(x => tier(x.Slug)).ToList();
            if (matches.Count == 1) return matches[0].Item;
            if (matches.Count > 1) return null;
        }
        return null;
    }

    /// <summary>Lower-case letters and digits only: "SOX/J-SOX" becomes "soxjsox".</summary>
    public static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
