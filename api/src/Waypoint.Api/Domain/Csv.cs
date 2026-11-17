using System.Globalization;
using System.Text;

namespace Waypoint.Api.Domain;

/// <summary>RFC 4180 CSV writing with spreadsheet formula-injection protection.</summary>
public sealed class Csv
{
    private readonly StringBuilder _sb = new();

    public Csv Row(params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) _sb.Append(',');
            _sb.Append(Field(values[i]));
        }
        _sb.Append("\r\n");
        return this;
    }

    public override string ToString() => _sb.ToString();

    /// <summary>Formats one field: numbers and dates invariant, text escaped and de-fanged.</summary>
    public static string Field(object? value) => value switch
    {
        null => "",
        string s => Escape(Defang(s)),
        bool b => b ? "true" : "false",
        DateTime d => d.ToString(d.Kind == DateTimeKind.Utc ? "yyyy-MM-dd'T'HH:mm:ss'Z'" : "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable f => Escape(f.ToString(null, CultureInfo.InvariantCulture)),
        _ => Escape(Defang(value.ToString() ?? "")),
    };

    /// <summary>Quotes a field when it contains a comma, quote, CR or LF (quotes doubled).</summary>
    public static string Escape(string s) =>
        s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>
    /// Text starting with = + - @ (or tab/CR) would run as a formula in Excel/Sheets; prefix it with
    /// an apostrophe. Applied to text only, never to numbers.
    /// </summary>
    public static string Defang(string s) =>
        s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + s : s;
}
