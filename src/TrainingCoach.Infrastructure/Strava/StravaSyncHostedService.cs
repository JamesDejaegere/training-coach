using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TrainingCoach.Infrastructure.Strava;

/// <summary>Catch-up sync shortly after start, then hourly (D11, D18). Does nothing until Strava is connected.</summary>
public sealed class StravaSyncHostedService(
    IServiceScopeFactory scopes, IOptions<StravaOptions> options, ILogger<StravaSyncHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
        {
            logger.LogWarning("Strava client id/secret are not configured (user secrets \"Strava:ClientId\" / \"Strava:ClientSecret\"); Strava sync is disabled.");
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
                var result = await scope.ServiceProvider.GetRequiredService<StravaSyncService>().SyncAsync(ct: stoppingToken);
                if (result.Status != SyncStatus.NotConnected)
                    logger.LogInformation("Strava sync: {Status}, {Added} added, {Updated} updated", result.Status, result.Added, result.Updated);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Strava sync failed");
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
