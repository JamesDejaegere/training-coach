using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrainingCoach.Infrastructure.Strava;

namespace TrainingCoach.Infrastructure.Polar;

/// <summary>
/// Catch-up sync shortly after start, then hourly (D18). Polar only exposes the last 30 days of exercises and
/// 28 nights, so the app has to run at least that often to miss nothing. Does nothing until Polar is connected.
/// </summary>
public sealed class PolarSyncHostedService(
    IServiceScopeFactory scopes, IOptions<PolarOptions> options, ILogger<PolarSyncHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
        {
            logger.LogWarning("Polar client id/secret are not configured (user secrets \"Polar:ClientId\" / \"Polar:ClientSecret\"); Polar sync is disabled.");
            return;
        }

        try { await Task.Delay(StartDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<PolarSyncService>().SyncAsync(ct: stoppingToken);
                if (result.Status != SyncStatus.NotConnected)
                    logger.LogInformation("Polar sync: {Status}, {Added} added, {Merged} merged with Strava, {Updated} updated, {Recovery} recovery days",
                        result.Status, result.Added, result.Merged, result.Updated, result.RecoveryDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Polar sync failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
