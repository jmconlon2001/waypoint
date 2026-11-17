using System.Globalization;
using System.Text;

namespace Waypoint.Api.Domain;

/// <summary>
/// A small iCalendar (RFC 5545) reader for the "secret address in iCal format" that Google Calendar
/// publishes. It handles VEVENTs with UTC, TZID and floating date-times and all-day dates.
/// Recurrences are expanded within a window: RRULE with FREQ DAILY/WEEKLY/MONTHLY/YEARLY, INTERVAL,
/// COUNT, UNTIL, BYDAY (incl. ordinals like 2TU / -1FR for MONTHLY) and BYMONTHDAY, plus EXDATE and
/// RECURRENCE-ID overrides. Other RRULE parts (BYSETPOS, BYWEEKNO, …) are not supported; such
/// series fall back to their first occurrence.
/// </summary>
public static class ICalParser
{
    private const int MaxIterations = 20_000;

    public sealed record Property(string Name, Dictionary<string, string> Parameters, string Value);

    private sealed class VEvent
    {
        public List<Property> Props { get; } = [];
        public Property? Get(string name) => Props.FirstOrDefault(p => p.Name == name);
        public IEnumerable<Property> All(string name) => Props.Where(p => p.Name == name);
    }

    /// <summary>Events overlapping [windowStartUtc, windowEndUtc), recurrences expanded.</summary>
    public static List<ImportedEvent> Parse(string ics, DateTime windowStartUtc, DateTime windowEndUtc, TimeZoneInfo defaultTz)
    {
        var events = ReadEvents(ics);
        var masters = new List<VEvent>();
        var overrides = new Dictionary<(string Uid, DateTime RecurrenceUtc), VEvent>();
        foreach (var ev in events)
        {
            var uid = ev.Get("UID")?.Value ?? "";
            if (ev.Get("RECURRENCE-ID") is { } rid && ParseDateValue(rid, defaultTz) is { } r)
                overrides[(uid, r.Utc)] = ev;
            else
                masters.Add(ev);
        }

        var result = new List<ImportedEvent>();
        foreach (var ev in masters)
        {
            var uid = ev.Get("UID")?.Value ?? Guid.NewGuid().ToString();
            if (ev.Get("DTSTART") is not { } dtStartProp || ParseDateValue(dtStartProp, defaultTz) is not { } start) continue;
            var duration = EventDuration(ev, start, defaultTz);
            var rrule = ev.Get("RRULE")?.Value;
            var recurring = rrule is not null;

            IEnumerable<DateValue> starts = recurring
                ? Expand(start, rrule!, ExDates(ev, defaultTz), windowStartUtc - duration, windowEndUtc)
                : [start];

            foreach (var occ in starts)
            {
                var source = overrides.TryGetValue((uid, occ.Utc), out var ov) ? ov : ev;
                overrides.Remove((uid, occ.Utc));
                if (ToImported(source, uid, occ, duration, recurring, defaultTz) is { } imported &&
                    imported.EndUtc > windowStartUtc && imported.StartUtc < windowEndUtc)
                    result.Add(imported);
            }
        }

        // Overrides whose original occurrence fell outside the window (moved into it).
        foreach (var ((uid, recurrence), ov) in overrides)
        {
            if (ov.Get("DTSTART") is not { } p || ParseDateValue(p, defaultTz) is not { } s) continue;
            var imported = ToImported(ov, uid, new DateValue(recurrence, recurrence, s.AllDay, s.Tz), EventDuration(ov, s, defaultTz), true, defaultTz);
            if (imported is not null && imported.EndUtc > windowStartUtc && imported.StartUtc < windowEndUtc) result.Add(imported);
        }

        return result.OrderBy(e => e.StartUtc).ToList();
    }

    private static ImportedEvent? ToImported(VEvent ev, string uid, DateValue occurrence, TimeSpan masterDuration, bool recurring, TimeZoneInfo defaultTz)
    {
        // An override carries its own DTSTART/DTEND; a plain occurrence uses the series start + duration.
        DateValue start = occurrence;
        TimeSpan duration = masterDuration;
        if (ev.Get("RECURRENCE-ID") is not null && ev.Get("DTSTART") is { } p && ParseDateValue(p, defaultTz) is { } s)
        {
            start = s;
            duration = EventDuration(ev, s, defaultTz);
        }

        var cancelled = string.Equals(ev.Get("STATUS")?.Value, "CANCELLED", StringComparison.OrdinalIgnoreCase);
        if (cancelled) return null;

        var attendees = ev.All("ATTENDEE")
            .Where(a => !a.Parameters.TryGetValue("CUTYPE", out var cu) || (cu != "RESOURCE" && cu != "ROOM"))
            .Select(a => a.Value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? a.Value[7..] : a.Value)
            .Where(a => a.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var endUtc = start.AllDay
            ? LocalTime.ToUtc(DateOnly.FromDateTime(start.Local).AddDays(Math.Max(1, (int)Math.Round(duration.TotalDays))), TimeOnly.MinValue, start.Tz)
            : start.Utc + duration;
        var externalId = recurring ? $"{uid}_{occurrence.Utc:yyyyMMdd'T'HHmmss'Z'}" : uid;

        return new ImportedEvent(
            externalId,
            Unescape(ev.Get("SUMMARY")?.Value ?? "(no title)"),
            start.Utc, endUtc, start.AllDay, attendees, recurring, "default", Declined: false, Cancelled: false);
    }

    private static TimeSpan EventDuration(VEvent ev, DateValue start, TimeZoneInfo defaultTz)
    {
        if (ev.Get("DTEND") is { } endProp && ParseDateValue(endProp, defaultTz) is { } end)
            return start.AllDay ? DateOnly.FromDateTime(end.Local).ToDateTime(TimeOnly.MinValue) - DateOnly.FromDateTime(start.Local).ToDateTime(TimeOnly.MinValue) : end.Utc - start.Utc;
        if (ev.Get("DURATION") is { } d && ParseDuration(d.Value) is { } span) return span;
        return start.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
    }

    // ---------------------------------------------------------------- recurrence

    private static IEnumerable<DateValue> Expand(DateValue start, string rrule, HashSet<DateTime> exDates, DateTime fromUtc, DateTime toUtc)
    {
        var parts = rrule.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1].ToUpperInvariant());

        if (!parts.TryGetValue("FREQ", out var freq) || parts.Keys.Any(k => k is "BYSETPOS" or "BYWEEKNO" or "BYYEARDAY" or "BYHOUR" or "BYMINUTE"))
        {
            yield return start;
            yield break;
        }
        var interval = parts.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var n) && n > 0 ? n : 1;
        int? count = parts.TryGetValue("COUNT", out var c) && int.TryParse(c, out var cn) ? cn : null;
        DateTime? untilUtc = parts.TryGetValue("UNTIL", out var u) ? ParseUntil(u, start.Tz) : null;
        var byDay = parts.TryGetValue("BYDAY", out var bd) ? bd.Split(',').Select(ParseByDay).Where(x => x is not null).Select(x => x!.Value).ToList() : [];
        var byMonthDay = parts.TryGetValue("BYMONTHDAY", out var bmd) ? bmd.Split(',').Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v != 0).ToList() : [];
        var byMonth = parts.TryGetValue("BYMONTH", out var bm) ? bm.Split(',').Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v is >= 1 and <= 12).ToList() : [];

        var startDate = DateOnly.FromDateTime(start.Local);
        var timeOfDay = TimeOnly.FromDateTime(start.Local);
        var produced = 0;
        var iterations = 0;

        foreach (var date in CandidateDates(freq, startDate, interval, byDay, byMonthDay, byMonth))
        {
            if (++iterations > MaxIterations) yield break;
            if (date < startDate) continue;
            var occ = Occurrence(date, timeOfDay, start);
            if (untilUtc is not null && occ.Utc > untilUtc) yield break;
            if (count is not null && produced >= count) yield break;
            produced++; // EXDATEs still count toward COUNT (RFC 5545).
            if (occ.Utc >= toUtc) yield break;
            if (exDates.Contains(occ.Utc)) continue;
            if (occ.Utc >= fromUtc) yield return occ;
        }
    }

    private static DateValue Occurrence(DateOnly date, TimeOnly time, DateValue start)
    {
        var local = date.ToDateTime(time);
        var utc = start.IsUtcAnchored ? DateTime.SpecifyKind(local, DateTimeKind.Utc) : LocalTime.ToUtc(date, time, start.Tz);
        return new DateValue(utc, local, start.AllDay, start.Tz) { IsUtcAnchored = start.IsUtcAnchored, Floating = start.Floating };
    }

    private static IEnumerable<DateOnly> CandidateDates(string freq, DateOnly start, int interval, List<(int Ordinal, DayOfWeek Day)> byDay, List<int> byMonthDay, List<int> byMonth)
    {
        switch (freq)
        {
            case "DAILY":
                for (var d = start; d.Year < start.Year + 60; d = d.AddDays(interval))
                    if ((byDay.Count == 0 || byDay.Any(b => b.Day == d.DayOfWeek)) && (byMonth.Count == 0 || byMonth.Contains(d.Month)))
                        yield return d;
                yield break;
            case "WEEKLY":
            {
                var days = byDay.Count > 0 ? byDay.Select(b => b.Day).Distinct().OrderBy(DayIndex).ToList() : [start.DayOfWeek];
                var weekStart = start.AddDays(-DayIndex(start.DayOfWeek)); // weeks start Monday (WKST=MO)
                for (var w = weekStart; w.Year < start.Year + 60; w = w.AddDays(7 * interval))
                    foreach (var day in days)
                        yield return w.AddDays(DayIndex(day));
                yield break;
            }
            case "MONTHLY":
                for (var m = new DateOnly(start.Year, start.Month, 1); m.Year < start.Year + 60; m = m.AddMonths(interval))
                {
                    if (byMonth.Count > 0 && !byMonth.Contains(m.Month)) continue;
                    foreach (var d in DaysInMonth(m, start, byDay, byMonthDay)) yield return d;
                }
                yield break;
            case "YEARLY":
                for (var y = start.Year; y < start.Year + 60; y += interval)
                {
                    var months = byMonth.Count > 0 ? byMonth.OrderBy(x => x).ToList() : [start.Month];
                    foreach (var month in months)
                    {
                        var m = new DateOnly(y, month, 1);
                        if (byDay.Count == 0 && byMonthDay.Count == 0)
                        {
                            if (start.Day <= DateTime.DaysInMonth(y, month)) yield return new DateOnly(y, month, start.Day);
                        }
                        else
                        {
                            foreach (var d in DaysInMonth(m, start, byDay, byMonthDay)) yield return d;
                        }
                    }
                }
                yield break;
            default:
                yield return start;
                yield break;
        }
    }

    private static IEnumerable<DateOnly> DaysInMonth(DateOnly month, DateOnly start, List<(int Ordinal, DayOfWeek Day)> byDay, List<int> byMonthDay)
    {
        var daysIn = DateTime.DaysInMonth(month.Year, month.Month);
        var set = new SortedSet<DateOnly>();
        foreach (var md in byMonthDay)
        {
            var day = md > 0 ? md : daysIn + md + 1;
            if (day >= 1 && day <= daysIn) set.Add(new DateOnly(month.Year, month.Month, day));
        }
        foreach (var (ordinal, dow) in byDay)
        {
            var matches = Enumerable.Range(1, daysIn).Select(d => new DateOnly(month.Year, month.Month, d)).Where(d => d.DayOfWeek == dow).ToList();
            if (ordinal == 0) foreach (var d in matches) set.Add(d);
            else if (ordinal > 0 && ordinal <= matches.Count) set.Add(matches[ordinal - 1]);
            else if (ordinal < 0 && -ordinal <= matches.Count) set.Add(matches[matches.Count + ordinal]);
        }
        if (byMonthDay.Count == 0 && byDay.Count == 0 && start.Day <= daysIn) set.Add(new DateOnly(month.Year, month.Month, start.Day));
        return set;
    }

    private static int DayIndex(DayOfWeek d) => ((int)d + 6) % 7; // Monday = 0

    private static (int, DayOfWeek)? ParseByDay(string token)
    {
        if (token.Length < 2) return null;
        var code = token[^2..];
        DayOfWeek? day = code switch
        {
            "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null,
        };
        if (day is null) return null;
        var prefix = token[..^2];
        var ordinal = prefix.Length == 0 ? 0 : int.TryParse(prefix, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var o) ? o : 0;
        return (ordinal, day.Value);
    }

    private static DateTime? ParseUntil(string value, TimeZoneInfo tz)
    {
        if (value.Length == 8 && DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return LocalTime.ToUtc(d.AddDays(1), TimeOnly.MinValue, tz).AddTicks(-1);
        if (DateTime.TryParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return value.EndsWith('Z') ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : LocalTime.ToUtc(DateOnly.FromDateTime(dt), TimeOnly.FromDateTime(dt), tz);
        return null;
    }

    private static HashSet<DateTime> ExDates(VEvent ev, TimeZoneInfo defaultTz)
    {
        var set = new HashSet<DateTime>();
        foreach (var p in ev.All("EXDATE"))
            foreach (var v in p.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (ParseDateValue(p with { Value = v.Trim() }, defaultTz) is { } d) set.Add(d.Utc);
        return set;
    }

    // ---------------------------------------------------------------- values

    private sealed record DateValue(DateTime Utc, DateTime Local, bool AllDay, TimeZoneInfo Tz)
    {
        /// <summary>Written in UTC ("Z"): recurrences repeat at the same UTC time.</summary>
        public bool IsUtcAnchored { get; init; }
        public bool Floating { get; init; }
    }

    private static DateValue? ParseDateValue(Property p, TimeZoneInfo defaultTz)
    {
        var v = p.Value.Trim();
        var tz = p.Parameters.TryGetValue("TZID", out var tzid) ? FindTz(tzid) ?? defaultTz : defaultTz;
        if ((p.Parameters.TryGetValue("VALUE", out var kind) && kind == "DATE") || v.Length == 8)
        {
            if (!DateOnly.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return null;
            return new DateValue(LocalTime.ToUtc(d, TimeOnly.MinValue, tz), d.ToDateTime(TimeOnly.MinValue), true, tz);
        }
        var isUtc = v.EndsWith('Z');
        if (!DateTime.TryParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return null;
        if (isUtc)
        {
            var utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return new DateValue(utc, utc, false, TimeZoneInfo.Utc) { IsUtcAnchored = true };
        }
        return new DateValue(LocalTime.ToUtc(DateOnly.FromDateTime(dt), TimeOnly.FromDateTime(dt), tz), dt, false, tz)
        {
            Floating = !p.Parameters.ContainsKey("TZID"),
        };
    }

    private static TimeZoneInfo? FindTz(string id)
    {
        id = id.Trim('"');
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && TimeZoneInfo.TryFindSystemTimeZoneById(iana, out tz)) return tz;
        return null;
    }

    /// <summary>ISO 8601 / RFC 5545 duration such as PT1H30M or P1D.</summary>
    public static TimeSpan? ParseDuration(string value)
    {
        var v = value.Trim().ToUpperInvariant();
        var negative = v.StartsWith('-');
        v = v.TrimStart('+', '-');
        if (!v.StartsWith('P')) return null;
        var total = TimeSpan.Zero;
        var number = new StringBuilder();
        var inTime = false;
        foreach (var ch in v[1..])
        {
            if (char.IsDigit(ch)) { number.Append(ch); continue; }
            if (ch == 'T') { inTime = true; continue; }
            if (number.Length == 0) return null;
            var n = int.Parse(number.ToString(), CultureInfo.InvariantCulture);
            number.Clear();
            total += ch switch
            {
                'W' => TimeSpan.FromDays(7 * n),
                'D' => TimeSpan.FromDays(n),
                'H' when inTime => TimeSpan.FromHours(n),
                'M' when inTime => TimeSpan.FromMinutes(n),
                'S' when inTime => TimeSpan.FromSeconds(n),
                _ => TimeSpan.Zero,
            };
        }
        return negative ? -total : total;
    }

    // ---------------------------------------------------------------- lexing

    private static List<VEvent> ReadEvents(string ics)
    {
        var events = new List<VEvent>();
        VEvent? current = null;
        var depth = 0; // nested components inside a VEVENT (VALARM) are skipped
        foreach (var line in Unfold(ics))
        {
            if (line.Length == 0) continue;
            var prop = ParseLine(line);
            if (prop is null) continue;
            if (prop.Name == "BEGIN")
            {
                if (prop.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase) && current is null) current = new VEvent();
                else if (current is not null) depth++;
                continue;
            }
            if (prop.Name == "END")
            {
                if (current is not null && depth > 0) depth--;
                else if (current is not null && prop.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) { events.Add(current); current = null; }
                continue;
            }
            if (current is not null && depth == 0) current.Props.Add(prop);
        }
        return events;
    }

    /// <summary>Joins folded lines (CRLF followed by a space or tab).</summary>
    public static IEnumerable<string> Unfold(string ics)
    {
        var sb = new StringBuilder();
        foreach (var raw in ics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                sb.Append(raw, 1, raw.Length - 1);
                continue;
            }
            if (sb.Length > 0) yield return sb.ToString();
            sb.Clear().Append(raw);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    public static Property? ParseLine(string line)
    {
        // NAME;PARAM=VALUE;PARAM="QUOTED:VALUE":PROPERTY-VALUE
        var inQuotes = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (line[i] == ':' && !inQuotes) { colon = i; break; }
        }
        if (colon < 0) return null;
        var head = line[..colon];
        var value = line[(colon + 1)..];
        var segments = SplitOutsideQuotes(head, ';');
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seg in segments.Skip(1))
        {
            var eq = seg.IndexOf('=');
            if (eq <= 0) continue;
            var name = seg[..eq].ToUpperInvariant();
            var pv = seg[(eq + 1)..].Trim('"');
            parameters[name] = name == "TZID" ? pv : pv.ToUpperInvariant();
        }
        return new Property(segments[0].ToUpperInvariant(), parameters, value);
    }

    private static List<string> SplitOutsideQuotes(string s, char sep)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in s)
        {
            if (ch == '"') inQuotes = !inQuotes;
            if (ch == sep && !inQuotes) { parts.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        parts.Add(sb.ToString());
        return parts;
    }

    /// <summary>Un-escapes RFC 5545 TEXT (\n, \, \; \\).</summary>
    public static string Unescape(string value)
    {
        if (!value.Contains('\\')) return value;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }
}
