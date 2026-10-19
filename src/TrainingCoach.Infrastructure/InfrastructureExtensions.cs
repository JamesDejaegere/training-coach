using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TrainingCoach.Infrastructure.Analytics;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.PlanImport;
using TrainingCoach.Infrastructure.Polar;
using TrainingCoach.Infrastructure.Strava;
using TrainingCoach.Infrastructure.Strength;

namespace TrainingCoach.Infrastructure;

/// <summary>Where the database and its backups live, derived from the data directory (SPEC 4.1).</summary>
public sealed record StorageLocations(string DataDirectory)
{
    public string DatabasePath => Path.Combine(DataDirectory, "trainingcoach.db");
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");
}

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string dataDirectory, IConfiguration configuration)
    {
        services.Configure<StravaOptions>(configuration.GetSection(StravaOptions.Section));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.Section));
        services.AddSingleton<TokenProtector>();
        services.AddHttpClient<IStravaApi, StravaClient>();
        services.AddScoped<StravaSyncService>();
        services.AddHostedService<StravaSyncHostedService>();
        services.Configure<PolarOptions>(configuration.GetSection(PolarOptions.Section));
        services.AddHttpClient<IPolarApi, PolarClient>();
        services.AddScoped<PolarSyncService>();
        services.AddHostedService<PolarSyncHostedService>();

        var storage = new StorageLocations(dataDirectory);
        services.AddSingleton(storage);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={storage.DatabasePath}"));
        services.AddScoped<PlanStore>();
        services.AddScoped<AthleteSettingsStore>();
        services.AddScoped<ActivityStore>();
        services.AddScoped<TrainingAnalytics>();
        services.AddScoped<ExerciseCatalog>();
        services.AddScoped<StrengthStore>();
        services.AddScoped<MuscleAnalytics>();
        services.AddHostedService<DatabaseBackupService>();
        return services;
    }

    /// <summary>Applies pending migrations; an existing database is backed up first.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<StorageLocations>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0) return;

        if (File.Exists(storage.DatabasePath) && (await db.Database.GetAppliedMigrationsAsync(ct)).Any())
        {
            var today = DateOnly.FromDateTime(scope.ServiceProvider.GetRequiredService<TimeProvider>().GetLocalNow().DateTime);
            DatabaseBackup.CreateDailyBackup(storage.DatabasePath, storage.BackupDirectory, today);
        }

        logger.LogInformation("Applying migrations: {Migrations}", string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>Development convenience: imports the seed workbook when the database has no plan yet.</summary>
    public static async Task SeedPlanIfEmptyAsync(this IServiceProvider services, string workbookPath, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<PlanStore>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PlanSeed");

        if (!File.Exists(workbookPath) || await store.HasPlanAsync(ct: ct)) return;

        var plan = ExcelPlanImporter.Import(workbookPath);
        await store.ImportAsync(plan, ct: ct);
        logger.LogInformation("Imported {Sessions} sessions and {Workouts} workouts from {Path}", plan.Sessions.Count, plan.Workouts.Count, workbookPath);
    }
}
