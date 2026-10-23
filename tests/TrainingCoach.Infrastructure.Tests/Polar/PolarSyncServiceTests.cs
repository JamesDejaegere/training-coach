using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TrainingCoach.Domain;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Polar;
using TrainingCoach.Infrastructure.Strava;
using TrainingCoach.Infrastructure.Tests.Persistence;
using TrainingCoach.Infrastructure.Tests.Strava;

namespace TrainingCoach.Infrastructure.Tests.Polar;

public sealed class PolarSyncServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _db = new();
    private readonly FakePolarApi _api = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly TokenProtector _protector = StravaTestData.Protector();

    public void Dispose() => _db.Dispose();

    private PolarSyncService Service(AppDbContext db) => new(db, _api, _protector, _time);

    private static PolarTokens Tokens(DateTimeOffset? expires = null) => new("polar-token", expires ?? Now.AddDays(300), 4242);

    private static readonly DateTime LocalStart = new(2026, 10, 19, 8, 30, 0); // 06:30 UTC with offset 120

    [Fact]
    public async Task Connect_registers_the_user_and_stores_the_token_encrypted_without_refresh_token()
    {
        await using var db = _db.CreateContext();

        await Service(db).ConnectAsync(Tokens());

        Assert.Equal(["training-coach-1"], _api.Registered);
        var row = await db.Connections.SingleAsync();
        Assert.Equal(Providers.Polar, row.Provider);
        Assert.Equal(4242, row.ExternalAthleteId);
        Assert.DoesNotContain("polar-token", row.AccessTokenProtected);
        Assert.Equal("polar-token", _protector.Unprotect(row.AccessTokenProtected));
        Assert.Equal("", row.RefreshTokenProtected);
    }

    [Fact]
    public async Task Sync_without_connection_reports_not_connected()
    {
        await using var db = _db.CreateContext();

        var result = await Service(db).SyncAsync();

        Assert.Equal(SyncStatus.NotConnected, result.Status);
    }

    [Fact]
    public async Task Exercises_and_recovery_are_stored()
    {
        _api.Exercises.Add(PolarTestData.Exercise("E1", LocalStart));
        _api.Recharges.Add(new PolarNightlyRecharge { Date = new DateOnly(2026, 10, 19), HeartRateVariabilityAvg = 61, HeartRateAvg = 52, AnsCharge = 3.5, AnsChargeStatus = 4 });
        _api.Nights.Add(new PolarSleep { Date = new DateOnly(2026, 10, 19), SleepScore = 82, LightSleep = 14000, DeepSleep = 6000, RemSleep = 5000 });
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens());

        var result = await service.SyncAsync();

        Assert.Equal(new PolarSyncResult(SyncStatus.Ok, 1, 0, 0, 1), result);
        var activity = await db.Activities.SingleAsync();
        Assert.Equal(("E1", null), (activity.PolarId, activity.StravaId));
        Assert.Equal("100,110,120", activity.HrSamples);

        var recovery = await db.RecoveryDays.SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 19), recovery.Date);
        Assert.Equal((61.0, 52.0, 3.5), (recovery.Hrv, recovery.RestingHeartRate, recovery.AnsCharge));
        Assert.Equal(82, recovery.SleepScore);
        Assert.Equal(25000, recovery.SleepSeconds);
        Assert.Equal("polar-token", _api.LastToken);
    }

    [Fact]
    public async Task Sync_is_idempotent_and_updates_existing_rows()
    {
        _api.Exercises.Add(PolarTestData.Exercise("E1", LocalStart));
        _api.Recharges.Add(new PolarNightlyRecharge { Date = new DateOnly(2026, 10, 19), HeartRateVariabilityAvg = 61 });
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens());
        await service.SyncAsync();

        _api.Exercises[0] = _api.Exercises[0] with { TrainingLoad = 101 };
        _api.Recharges[0] = _api.Recharges[0] with { HeartRateVariabilityAvg = 64 };
        var second = await service.SyncAsync();

        Assert.Equal(new PolarSyncResult(SyncStatus.Ok, 0, 0, 1, 1), second);
        Assert.Equal(1, await db.Activities.CountAsync());
        Assert.Equal(101, (await db.Activities.AsNoTracking().SingleAsync()).PolarTrainingLoad);
        Assert.Equal(64, (await db.RecoveryDays.AsNoTracking().SingleAsync()).Hrv);
    }

    [Fact]
    public async Task A_strava_activity_of_the_same_session_is_merged_not_duplicated()
    {
        await using var db = _db.CreateContext();
        db.Activities.Add(new ActivityEntity
        {
            UserId = 1, StravaId = 77, Name = "Ochtendloop", Sport = SportFamily.Run,
            StartUtc = new DateTime(2026, 10, 19, 6, 31, 0, DateTimeKind.Utc), StartLocal = LocalStart.AddMinutes(1),
            ElapsedSeconds = 3590, MovingSeconds = 3500, DistanceMeters = 10050, AverageHeartRate = 130, RelativeEffort = 57,
        });
        await db.SaveChangesAsync();
        _api.Exercises.Add(PolarTestData.Exercise("E1", LocalStart, duration: "PT1H"));
        var service = Service(db);
        await service.ConnectAsync(Tokens());

        var result = await service.SyncAsync();

        Assert.Equal((0, 1), (result.Added, result.Merged));
        var row = await db.Activities.AsNoTracking().SingleAsync();
        Assert.Equal((77L, "E1"), (row.StravaId, row.PolarId));
        Assert.Equal("Ochtendloop", row.Name);
        Assert.Equal(57, row.RelativeEffort);
        Assert.Equal(140, row.AverageHeartRate); // Polar preferred for HR
    }

    [Fact]
    public async Task A_session_already_imported_from_the_export_under_another_id_is_updated_not_duplicated()
    {
        await using var db = _db.CreateContext();
        db.Activities.Add(new ActivityEntity
        {
            UserId = 1, StravaId = 77, PolarId = "8300009631", Name = "Ochtendloop", Sport = SportFamily.Run,
            StartUtc = new DateTime(2026, 10, 19, 6, 30, 0, DateTimeKind.Utc), StartLocal = LocalStart,
            ElapsedSeconds = 3600, MovingSeconds = 3600, HrSamples = "100,101", HrSampleIntervalSeconds = 1,
        });
        await db.SaveChangesAsync();
        _api.Exercises.Add(PolarTestData.Exercise("E1", LocalStart, duration: "PT1H"));
        var service = Service(db);
        await service.ConnectAsync(Tokens());

        var result = await service.SyncAsync();

        Assert.Equal((0, 1), (result.Added, result.Merged));
        var row = await db.Activities.AsNoTracking().SingleAsync();
        Assert.Equal((77L, "E1", "Ochtendloop"), (row.StravaId, row.PolarId, row.Name));
        Assert.Equal("100,110,120", row.HrSamples);
    }

    [Fact]
    public async Task Different_sport_or_far_apart_start_stays_a_separate_activity()
    {
        await using var db = _db.CreateContext();
        db.Activities.Add(new ActivityEntity
        {
            UserId = 1, StravaId = 77, Sport = SportFamily.Ride, StartUtc = new DateTime(2026, 10, 19, 6, 30, 0, DateTimeKind.Utc),
            ElapsedSeconds = 3600,
        });
        db.Activities.Add(new ActivityEntity
        {
            UserId = 1, StravaId = 78, Sport = SportFamily.Run, StartUtc = new DateTime(2026, 10, 19, 9, 30, 0, DateTimeKind.Utc),
            ElapsedSeconds = 3600,
        });
        await db.SaveChangesAsync();
        _api.Exercises.Add(PolarTestData.Exercise("E1", LocalStart));
        var service = Service(db);
        await service.ConnectAsync(Tokens());

        var result = await service.SyncAsync();

        Assert.Equal((1, 0), (result.Added, result.Merged));
        Assert.Equal(3, await db.Activities.CountAsync());
    }

    [Fact]
    public async Task A_later_strava_sync_merges_into_an_existing_polar_row_and_keeps_polar_heart_rate()
    {
        _api.Exercises.Add(PolarTestData.Exercise("E1", LocalStart, duration: "PT1H"));
        await using var db = _db.CreateContext();
        var polar = Service(db);
        await polar.ConnectAsync(Tokens());
        await polar.SyncAsync();

        var stravaApi = new FakeStravaApi();
        stravaApi.Activities.Add(StravaTestData.Activity(77, new DateTimeOffset(2026, 10, 19, 6, 31, 0, TimeSpan.Zero)) with
        {
            ElapsedTime = 3580, AverageHeartrate = 120, SufferScore = 57,
        });
        var strava = new StravaSyncService(db, stravaApi, _protector, _time);
        await strava.ConnectAsync(new StravaTokens("a", "r", Now.AddHours(6), 1));

        var result = await strava.SyncAsync();

        Assert.Equal(new SyncResult(SyncStatus.Ok, 0, 1), result);
        var row = await db.Activities.AsNoTracking().SingleAsync();
        Assert.Equal((77L, "E1"), (row.StravaId, row.PolarId));
        Assert.Equal(140, row.AverageHeartRate);
        Assert.Equal(57, row.RelativeEffort);
        Assert.Equal("Activity 77", row.Name);
    }

    [Fact]
    public async Task Expired_token_reports_auth_failed_without_calling_polar()
    {
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(expires: Now.AddDays(-1)));

        var result = await service.SyncAsync();

        Assert.Equal(SyncStatus.AuthFailed, result.Status);
        Assert.Null(_api.LastToken);
    }

    [Fact]
    public async Task Revoked_token_and_rate_limit_are_reported()
    {
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens());

        _api.FailWithAuth = true;
        Assert.Equal(SyncStatus.AuthFailed, (await service.SyncAsync()).Status);

        _api.FailWithAuth = false;
        _api.FailWithRateLimit = true;
        Assert.Equal(SyncStatus.RateLimited, (await service.SyncAsync()).Status);
    }

    [Fact]
    public async Task Status_counts_polar_activities_and_recovery_days()
    {
        _api.Exercises.Add(PolarTestData.Exercise("E1", LocalStart));
        _api.Recharges.Add(new PolarNightlyRecharge { Date = new DateOnly(2026, 10, 19), HeartRateVariabilityAvg = 61 });
        await using var db = _db.CreateContext();
        var service = Service(db);
        Assert.False((await service.GetStatusAsync()).Connected);

        await service.ConnectAsync(Tokens());
        await service.SyncAsync();

        var status = await service.GetStatusAsync();
        Assert.Equal((true, 1, 1), (status.Connected, status.ActivityCount, status.RecoveryDayCount));
        Assert.Equal(Now.UtcDateTime, status.LastSyncUtc);
    }
}
