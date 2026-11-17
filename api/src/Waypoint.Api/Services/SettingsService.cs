using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Waypoint.Api.Contracts;
using Waypoint.Api.Data;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Domain;

namespace Waypoint.Api.Services;

public sealed class SettingsService(WaypointDbContext db)
{
    public async Task<Setting> GetAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.FirstOrDefaultAsync(x => x.Id == Setting.SingletonId, ct);
        if (s is not null) return s;
        s = new Setting();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<TimeZoneInfo> TimeZoneAsync(CancellationToken ct = default) =>
        LocalTime.FindTimeZone((await GetAsync(ct)).TimeZone);

    public async Task<SettingsDto> GetDtoAsync(CancellationToken ct = default) => ToDto(await GetAsync(ct));

    public async Task<SettingsDto> UpdateAsync(SettingsInput input, CancellationToken ct = default)
    {
        var start = ParseTime(input.WorkdayStart, "Workday start");
        var end = ParseTime(input.WorkdayEnd, "Workday end");
        if (end <= start) throw ApiException.BadRequest("Workday end must be after workday start.");
        if (input.TimerWarningMinutes is < 0 or > 24 * 60)
            throw ApiException.BadRequest("Timer warning must be between 0 (off) and 1440 minutes.");
        if (!LocalTime.IsValidTimeZone(input.TimeZone))
            throw ApiException.BadRequest($"Unknown time zone '{input.TimeZone}'. Use an IANA name such as America/New_York.");
        if (input.MaxMeetingMinutes is < 0 or > 24 * 60)
            throw ApiException.BadRequest("Longest counted meeting must be between 0 (no limit) and 1440 minutes.");
        var reminder = input.ReviewReminderTime is null ? (TimeOnly?)null : ParseTime(input.ReviewReminderTime, "Review reminder time");
        var domains = input.InternalDomains is null ? null : string.Join(", ", CalendarRules.ParseDomainList(input.InternalDomains));
        if (domains is { Length: > 1000 }) throw ApiException.BadRequest("Too many internal domains.");

        var s = await GetAsync(ct);
        s.WorkdayStart = start;
        s.WorkdayEnd = end;
        s.TimerWarningMinutes = input.TimerWarningMinutes;
        s.TimeZone = input.TimeZone.Trim();
        if (input.CalendarSource is not null) s.CalendarSource = input.CalendarSource.Value;
        if (input.MaxMeetingMinutes is not null) s.MaxMeetingMinutes = input.MaxMeetingMinutes.Value;
        if (domains is not null) s.InternalDomains = domains;
        if (input.ReviewReminderEnabled is not null) s.ReviewReminderEnabled = input.ReviewReminderEnabled.Value;
        if (reminder is not null) s.ReviewReminderTime = reminder.Value;
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    private static SettingsDto ToDto(Setting s) => new(
        s.WorkdayStart.ToString("HH:mm", CultureInfo.InvariantCulture),
        s.WorkdayEnd.ToString("HH:mm", CultureInfo.InvariantCulture),
        s.TimerWarningMinutes, s.TimeZone, ProjectPalette.Colors,
        s.CalendarSource, s.MaxMeetingMinutes, s.InternalDomains,
        s.ReviewReminderEnabled, s.ReviewReminderTime.ToString("HH:mm", CultureInfo.InvariantCulture));

    private static TimeOnly ParseTime(string? value, string label) =>
        TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : throw ApiException.BadRequest($"{label} must be a time like 08:45.");
}
