using Microsoft.Extensions.Options;
using Waypoint.Api.Data.Entities;
using Waypoint.Api.Integrations;
using Waypoint.Api.Services;

namespace Waypoint.Api.Workers;

/// <summary>FR-C1: imports calendar events every Calendar:SyncIntervalMinutes (default 15) when a source is selected.</summary>
public sealed class CalendarSyncWorker(IServiceScopeFactory scopes, IOptions<CalendarOptions> options, ILogger<CalendarSyncWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup (migrations) finish first.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.SyncIntervalMinutes));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var settings = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync(stoppingToken);
                if (settings.CalendarSource != CalendarSourceKind.None)
                    await scope.ServiceProvider.GetRequiredService<CalendarService>().SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (ApiException ex)
            {
                log.LogWarning("Calendar sync skipped: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Calendar sync failed");
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
