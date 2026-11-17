using Waypoint.Api.Domain;
using Waypoint.Api.Services;

namespace Waypoint.Api.Workers;

/// <summary>
/// Fills the DailyAggregate table: at startup it backfills the last 35 days that are missing, then
/// every night at 00:20 local time it recomputes the previous 7 days (late edits are picked up).
/// </summary>
public sealed class NightlyAggregateWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<NightlyAggregateWorker> log) : BackgroundService
{
    private static readonly TimeOnly RunAt = new(0, 20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        await RunAsync(backfill: true, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var tz = await scope.ServiceProvider.GetRequiredService<SettingsService>().TimeZoneAsync(stoppingToken);
                var now = clock.GetUtcNow().UtcDateTime;
                var today = LocalTime.LocalDate(now, tz);
                var next = LocalTime.ToUtc(today, RunAt, tz);
                if (next <= now) next = LocalTime.ToUtc(today.AddDays(1), RunAt, tz);
                delay = next - now;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Could not schedule the nightly aggregate job; retrying in an hour");
                delay = TimeSpan.FromHours(1);
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
            await RunAsync(backfill: false, stoppingToken);
        }
    }

    private async Task RunAsync(bool backfill, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var tz = await scope.ServiceProvider.GetRequiredService<SettingsService>().TimeZoneAsync(ct);
            var metrics = scope.ServiceProvider.GetRequiredService<MetricsService>();
            var yesterday = LocalTime.LocalDate(clock.GetUtcNow().UtcDateTime, tz).AddDays(-1);
            if (backfill)
            {
                await metrics.GetAggregatesAsync(yesterday.AddDays(-34), yesterday, ct); // computes only the missing days
            }
            else
            {
                var r = await metrics.RecomputeAsync(yesterday.AddDays(-6), yesterday, ct);
                log.LogInformation("Daily aggregates recomputed for {From}..{To} ({Days} days)", r.From, r.To, r.Days);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Daily aggregate job failed");
        }
    }
}
