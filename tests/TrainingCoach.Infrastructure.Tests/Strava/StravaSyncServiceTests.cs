using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Strava;
using TrainingCoach.Infrastructure.Tests.Persistence;

namespace TrainingCoach.Infrastructure.Tests.Strava;

public sealed class StravaSyncServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _db = new();
    private readonly FakeStravaApi _api = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly TokenProtectorHolder _protector = new();

    private sealed class TokenProtectorHolder { public TokenProtector Value { get; } = StravaTestData.Protector(); }

    public void Dispose() => _db.Dispose();

    private StravaSyncService Service(AppDbContext db) => new(db, _api, _protector.Value, _time);

    private static StravaTokens Tokens(DateTimeOffset expires) => new("access-1", "refresh-1", expires, 42);

    private static StravaActivity Act(long id, int daysAgo) =>
        StravaTestData.Activity(id, Now.AddDays(-daysAgo));

    [Fact]
    public async Task Sync_without_connection_reports_not_connected()
    {
        await using var db = _db.CreateContext();

        var result = await Service(db).SyncAsync();

        Assert.Equal(new SyncResult(SyncStatus.NotConnected, 0, 0), result);
        Assert.Empty(_api.ActivityCalls);
    }

    [Fact]
    public async Task Tokens_are_stored_encrypted()
    {
        await using var db = _db.CreateContext();
        await Service(db).ConnectAsync(Tokens(Now.AddHours(6)));

        var row = await db.Connections.SingleAsync();

        Assert.DoesNotContain("access-1", row.AccessTokenProtected);
        Assert.DoesNotContain("refresh-1", row.RefreshTokenProtected);
        Assert.Equal("access-1", _protector.Value.Unprotect(row.AccessTokenProtected));
        Assert.Equal(42, row.ExternalAthleteId);
    }

    [Fact]
    public async Task First_sync_fetches_the_full_history_over_several_pages()
    {
        _api.PageSize = 2;
        _api.Activities.AddRange(Enumerable.Range(1, 5).Select(i => Act(i, 100 - i)));
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(Now.AddHours(6)));

        var result = await service.SyncAsync();

        Assert.Equal(new SyncResult(SyncStatus.Ok, 5, 0), result);
        Assert.Equal(5, await db.Activities.CountAsync());
        Assert.All(_api.ActivityCalls, c => Assert.Null(c.After));
        Assert.Equal(new[] { 1, 2, 3, 4 }, _api.ActivityCalls.Select(c => c.Page)); // pages of 2, 2, 1, then the empty page ends the run
    }

    [Fact]
    public async Task Second_sync_is_incremental_and_does_not_duplicate_activities()
    {
        _api.Activities.AddRange([Act(1, 30), Act(2, 20)]);
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(Now.AddHours(6)));
        await service.SyncAsync();
        _api.ActivityCalls.Clear();
        _api.Activities.Add(Act(3, 1));

        var result = await service.SyncAsync();

        Assert.Equal(SyncStatus.Ok, result.Status);
        Assert.Equal(1, result.Added);
        Assert.Equal(3, await db.Activities.CountAsync());
        var after = _api.ActivityCalls[0].After;
        Assert.Equal(Now.AddDays(-20) - StravaSyncService.Overlap, after);
    }

    [Fact]
    public async Task Overlap_window_refetches_recent_activities_as_updates()
    {
        _api.Activities.Add(Act(1, 2));
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(Now.AddHours(6)));
        await service.SyncAsync();

        _api.Activities[0] = _api.Activities[0] with { Name = "Hernoemd", AverageHeartrate = 150 };
        var result = await service.SyncAsync();

        Assert.Equal(new SyncResult(SyncStatus.Ok, 0, 1), result);
        var row = await db.Activities.SingleAsync();
        Assert.Equal("Hernoemd", row.Name);
        Assert.Equal(150, row.AverageHeartRate);
    }

    [Fact]
    public async Task Preserves_rpe_entered_by_the_athlete_when_an_activity_is_updated()
    {
        _api.Activities.Add(Act(1, 2));
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(Now.AddHours(6)));
        await service.SyncAsync();
        (await db.Activities.SingleAsync()).Rpe = 7;
        await db.SaveChangesAsync();

        await service.SyncAsync();

        Assert.Equal(7, (await db.Activities.AsNoTracking().SingleAsync()).Rpe);
    }

    [Fact]
    public async Task Expired_token_is_refreshed_and_the_rotated_refresh_token_is_stored()
    {
        _api.Activities.Add(Act(1, 5));
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(Now.AddMinutes(-1)));

        await service.SyncAsync();

        Assert.Equal(1, _api.RefreshCalls);
        Assert.Equal("refreshed-access", _api.LastAccessToken);
        var row = await db.Connections.AsNoTracking().SingleAsync();
        Assert.Equal("refreshed-refresh", _protector.Value.Unprotect(row.RefreshTokenProtected));
    }

    [Fact]
    public async Task Valid_token_is_not_refreshed()
    {
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(Now.AddHours(3)));

        await service.SyncAsync();

        Assert.Equal(0, _api.RefreshCalls);
        Assert.Equal("access-1", _api.LastAccessToken);
    }

    [Fact]
    public async Task Failed_refresh_reports_auth_failed()
    {
        _api.RefreshFails = true;
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(Now.AddMinutes(-1)));

        var result = await service.SyncAsync();

        Assert.Equal(SyncStatus.AuthFailed, result.Status);
    }

    [Fact]
    public async Task Rate_limit_keeps_fetched_pages_but_does_not_move_the_cursor()
    {
        _api.PageSize = 2;
        _api.Activities.AddRange(Enumerable.Range(1, 6).Select(i => Act(i, 100 - i)));
        _api.FailWithRateLimitOnCall = 2; // second page is refused
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(Now.AddHours(6)));

        var result = await service.SyncAsync();

        Assert.Equal(new SyncResult(SyncStatus.RateLimited, 2, 0), result);
        Assert.Null((await db.Connections.AsNoTracking().SingleAsync()).SyncCursorUtc);

        _api.FailWithRateLimitOnCall = null;
        var retry = await service.SyncAsync();
        Assert.Equal(new SyncResult(SyncStatus.Ok, 4, 2), retry); // first two are re-fetched as updates
        Assert.Equal(6, await db.Activities.CountAsync());
    }

    [Fact]
    public async Task Status_reports_connection_last_sync_and_count()
    {
        _api.Activities.AddRange([Act(1, 3), Act(2, 2)]);
        await using var db = _db.CreateContext();
        var service = Service(db);
        Assert.Equal(new StravaStatus(false, null, 0), await service.GetStatusAsync());

        await service.ConnectAsync(Tokens(Now.AddHours(6)));
        await service.SyncAsync();

        var status = await service.GetStatusAsync();
        Assert.True(status.Connected);
        Assert.Equal(Now.UtcDateTime, status.LastSyncUtc);
        Assert.Equal(2, status.ActivityCount);
    }

    [Fact]
    public async Task Connecting_a_different_athlete_restarts_from_the_beginning()
    {
        _api.Activities.Add(Act(1, 3));
        await using var db = _db.CreateContext();
        var service = Service(db);
        await service.ConnectAsync(Tokens(Now.AddHours(6)));
        await service.SyncAsync();

        await service.ConnectAsync(new StravaTokens("a", "r", Now.AddHours(6), AthleteId: 99));
        _api.ActivityCalls.Clear();
        await service.SyncAsync();

        Assert.Null(_api.ActivityCalls.First().After);
    }
}
