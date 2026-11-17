using Waypoint.Api.Services;

namespace Waypoint.Api.Integrations;

/// <summary>Syncs the calendar every 15 minutes while an iCal address is configured (FR-C1).</summary>
public sealed class CalendarSyncWorker(IServiceScopeFactory scopes, ILogger<CalendarSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup (migrations) finish first.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(CalendarService.SyncIntervalMinutes));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var calendar = scope.ServiceProvider.GetRequiredService<CalendarService>();
                if ((await calendar.GetStatusAsync(stoppingToken)).Configured)
                {
                    var r = await calendar.SyncAsync(stoppingToken);
                    logger.LogInformation("Calendar synced: {Total} events ({Added} new, {Removed} removed)", r.Total, r.Added, r.Removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The error is saved on the settings row and shown in the UI; keep trying on the next tick.
                logger.LogWarning("Scheduled calendar sync failed: {Message}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
