using Microsoft.EntityFrameworkCore;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Strava;

public enum SyncStatus
{
    Ok,
    NotConnected,
    /// <summary>Strava rate limit hit; whatever was fetched is stored, the rest follows on the next run.</summary>
    RateLimited,
    /// <summary>Token revoked or refresh failed; the athlete has to connect again.</summary>
    AuthFailed,
}

public sealed record SyncResult(SyncStatus Status, int Added, int Updated);

public sealed record StravaStatus(bool Connected, DateTime? LastSyncUtc, int ActivityCount);

/// <summary>Connects the athlete to Strava and syncs activities: full history first, then incrementally (SPEC 5.1, D11).</summary>
public sealed class StravaSyncService(AppDbContext db, IStravaApi api, TokenProtector protector, TimeProvider time)
{
    /// <summary>Re-fetch window before the cursor, so activities uploaded late or edited recently are picked up.</summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromDays(7);
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    public async Task ConnectAsync(StravaTokens tokens, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var connection = await db.Connections.SingleOrDefaultAsync(c => c.UserId == userId && c.Provider == Providers.Strava, ct);
        if (connection is null)
        {
            connection = new ProviderConnectionEntity { UserId = userId, Provider = Providers.Strava, ConnectedUtc = time.GetUtcNow().UtcDateTime };
            db.Connections.Add(connection);
        }

        // A different Strava athlete means a different history: restart from the beginning.
        if (connection.ExternalAthleteId is { } existing && tokens.AthleteId is { } incoming && existing != incoming)
            connection.SyncCursorUtc = null;

        connection.ExternalAthleteId = tokens.AthleteId ?? connection.ExternalAthleteId;
        Store(connection, tokens);
        await db.SaveChangesAsync(ct);
    }

    public async Task<StravaStatus> GetStatusAsync(int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId && c.Provider == Providers.Strava, ct);
        var count = await db.Activities.CountAsync(a => a.UserId == userId && a.StravaId != null, ct);
        return new StravaStatus(connection is not null, connection?.LastSyncUtc, count);
    }

    public async Task<SyncResult> SyncAsync(int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var connection = await db.Connections.SingleOrDefaultAsync(c => c.UserId == userId && c.Provider == Providers.Strava, ct);
        if (connection is null) return new SyncResult(SyncStatus.NotConnected, 0, 0);

        int added = 0, updated = 0;
        try
        {
            var accessToken = await GetFreshAccessTokenAsync(connection, ct);
            DateTimeOffset? after = connection.SyncCursorUtc is { } cursor
                ? new DateTimeOffset(DateTime.SpecifyKind(cursor, DateTimeKind.Utc)) - Overlap
                : null;

            var newestStart = connection.SyncCursorUtc;
            for (var page = 1; ; page++)
            {
                var activities = await api.GetActivitiesAsync(accessToken, after, page, ct);
                if (activities.Count == 0) break;

                var ids = activities.Select(a => a.Id).ToList();
                var existing = await db.Activities.Where(a => a.UserId == userId && ids.Contains(a.StravaId!.Value))
                    .ToDictionaryAsync(a => a.StravaId!.Value, ct);

                foreach (var activity in activities)
                {
                    if (existing.TryGetValue(activity.Id, out var entity)) updated++;
                    else
                    {
                        entity = new ActivityEntity { UserId = userId };
                        db.Activities.Add(entity);
                        added++;
                    }
                    ActivityMapper.Apply(activity, entity);

                    if (newestStart is null || entity.StartUtc > newestStart) newestStart = entity.StartUtc;
                }
                await db.SaveChangesAsync(ct);
            }

            // Only a complete run moves the cursor; an interrupted run is simply repeated (upserts are idempotent).
            connection.SyncCursorUtc = newestStart;
            connection.LastSyncUtc = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return new SyncResult(SyncStatus.Ok, added, updated);
        }
        catch (StravaRateLimitedException)
        {
            return new SyncResult(SyncStatus.RateLimited, added, updated);
        }
        catch (StravaAuthException)
        {
            return new SyncResult(SyncStatus.AuthFailed, added, updated);
        }
    }

    private async Task<string> GetFreshAccessTokenAsync(ProviderConnectionEntity connection, CancellationToken ct)
    {
        var expires = DateTime.SpecifyKind(connection.AccessTokenExpiresUtc, DateTimeKind.Utc);
        if (expires - RefreshMargin > time.GetUtcNow().UtcDateTime)
            return protector.Unprotect(connection.AccessTokenProtected);

        var tokens = await api.RefreshAsync(protector.Unprotect(connection.RefreshTokenProtected), ct);
        Store(connection, tokens); // Strava rotates the refresh token: always keep the newest one.
        await db.SaveChangesAsync(ct);
        return tokens.AccessToken;
    }

    private void Store(ProviderConnectionEntity connection, StravaTokens tokens)
    {
        connection.AccessTokenProtected = protector.Protect(tokens.AccessToken);
        connection.RefreshTokenProtected = protector.Protect(tokens.RefreshToken);
        connection.AccessTokenExpiresUtc = tokens.ExpiresAt.UtcDateTime;
    }
}
