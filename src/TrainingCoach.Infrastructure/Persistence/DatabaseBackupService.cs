using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TrainingCoach.Infrastructure.Persistence;

/// <summary>Makes sure a backup exists for today: at start and then every hour (the app is not always running, D18).</summary>
public sealed class DatabaseBackupService(StorageLocations storage, TimeProvider time, ILogger<DatabaseBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        do
        {
            try
            {
                var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
                var path = await Task.Run(() => DatabaseBackup.CreateDailyBackup(storage.DatabasePath, storage.BackupDirectory, today), stoppingToken);
                if (path is not null) logger.LogInformation("Database backup written to {Path}", path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Database backup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
