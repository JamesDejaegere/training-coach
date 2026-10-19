using Microsoft.EntityFrameworkCore;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Strava;

namespace TrainingCoach.Infrastructure.Polar;

public sealed record PolarSyncResult(SyncStatus Status, int Added, int Merged, int Updated, int RecoveryDays);

public sealed record PolarStatus(bool Connected, DateTime? LastSyncUtc, int ActivityCount, int RecoveryDayCount, DateTime? TokenExpiresUtc);

/// <summary>
/// Connects to Polar AccessLink and syncs exercises (last 30 days, with HR samples) and Nightly Recharge / sleep
/// (last 28 nights) (SPEC 5.2). Everything is idempotent, so every run simply re-reads the whole available window.
/// </summary>
public sealed class PolarSyncService(AppDbContext db, IPolarApi api, TokenProtector protector, TimeProvider time)
{
    /// <summary>Identifies our user at Polar (member-id of the registration).</summary>
    public static string MemberId(int userId) => $"training-coach-{userId}";

    public async Task ConnectAsync(PolarTokens tokens, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        await api.RegisterUserAsync(tokens.AccessToken, MemberId(userId), ct);

        var connection = await db.Connections.SingleOrDefaultAsync(c => c.UserId == userId && c.Provider == Providers.Polar, ct);
        if (connection is null)
        {
            connection = new ProviderConnectionEntity { UserId = userId, Provider = Providers.Polar, ConnectedUtc = time.GetUtcNow().UtcDateTime };
            db.Connections.Add(connection);
        }

        connection.ExternalAthleteId = tokens.PolarUserId;
        connection.AccessTokenProtected = protector.Protect(tokens.AccessToken);
        connection.RefreshTokenProtected = ""; // AccessLink has no refresh token
        connection.AccessTokenExpiresUtc = tokens.ExpiresAt.UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public async Task<PolarStatus> GetStatusAsync(int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId && c.Provider == Providers.Polar, ct);
        var activities = await db.Activities.CountAsync(a => a.UserId == userId && a.PolarId != null, ct);
        var recovery = await db.RecoveryDays.CountAsync(r => r.UserId == userId, ct);
        return new PolarStatus(connection is not null, connection?.LastSyncUtc, activities, recovery, connection?.AccessTokenExpiresUtc);
    }

    public async Task<PolarSyncResult> SyncAsync(int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var connection = await db.Connections.SingleOrDefaultAsync(c => c.UserId == userId && c.Provider == Providers.Polar, ct);
        if (connection is null) return new PolarSyncResult(SyncStatus.NotConnected, 0, 0, 0, 0);

        if (DateTime.SpecifyKind(connection.AccessTokenExpiresUtc, DateTimeKind.Utc) <= time.GetUtcNow().UtcDateTime)
            return new PolarSyncResult(SyncStatus.AuthFailed, 0, 0, 0, 0);

        int added = 0, merged = 0, updated = 0, recoveryDays = 0;
        try
        {
            var token = protector.Unprotect(connection.AccessTokenProtected);

            foreach (var exercise in await api.GetExercisesAsync(token, ct))
            {
                var existing = await db.Activities.SingleOrDefaultAsync(a => a.UserId == userId && a.PolarId == exercise.Id, ct);
                if (existing is not null)
                {
                    PolarMapper.Apply(exercise, existing, createdFromPolar: false);
                    updated++;
                    continue;
                }

                var counterpart = await ActivityLinker.FindStravaOnlyAsync(db, userId, PolarMapper.KeyOf(exercise), ct);
                if (counterpart is not null)
                {
                    PolarMapper.Apply(exercise, counterpart, createdFromPolar: false);
                    merged++;
                    continue;
                }

                var created = new ActivityEntity { UserId = userId };
                PolarMapper.Apply(exercise, created, createdFromPolar: true);
                db.Activities.Add(created);
                added++;
                await db.SaveChangesAsync(ct); // so the next exercise of this run can see this row when looking for counterparts
            }
            await db.SaveChangesAsync(ct);

            recoveryDays = await SyncRecoveryAsync(token, userId, ct);

            connection.LastSyncUtc = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return new PolarSyncResult(SyncStatus.Ok, added, merged, updated, recoveryDays);
        }
        catch (PolarRateLimitedException)
        {
            await db.SaveChangesAsync(CancellationToken.None);
            return new PolarSyncResult(SyncStatus.RateLimited, added, merged, updated, recoveryDays);
        }
        catch (PolarAuthException)
        {
            return new PolarSyncResult(SyncStatus.AuthFailed, added, merged, updated, recoveryDays);
        }
    }

    private async Task<int> SyncRecoveryAsync(string token, int userId, CancellationToken ct)
    {
        var recharges = (await api.GetNightlyRechargesAsync(token, ct)).ToDictionary(r => r.Date);
        var nights = (await api.GetSleepAsync(token, ct)).ToDictionary(s => s.Date);
        var dates = recharges.Keys.Union(nights.Keys).ToList();
        if (dates.Count == 0) return 0;

        var existing = (await db.RecoveryDays.Where(r => r.UserId == userId).ToListAsync(ct)).ToDictionary(r => r.Date);
        foreach (var date in dates)
        {
            if (!existing.TryGetValue(date, out var row))
            {
                row = new RecoveryDayEntity { UserId = userId, Date = date };
                db.RecoveryDays.Add(row);
            }

            if (recharges.TryGetValue(date, out var r))
            {
                row.Hrv = r.HeartRateVariabilityAvg;
                row.RestingHeartRate = r.HeartRateAvg;
                row.BreathingRate = r.BreathingRateAvg;
                row.AnsCharge = r.AnsCharge;
                row.AnsChargeStatus = r.AnsChargeStatus;
                row.NightlyRechargeStatus = r.NightlyRechargeStatus;
            }

            if (nights.TryGetValue(date, out var s))
            {
                row.SleepScore = s.SleepScore;
                row.DeepSleepSeconds = s.DeepSleep;
                row.RemSleepSeconds = s.RemSleep;
                row.SleepSeconds = s.LightSleep is null && s.DeepSleep is null && s.RemSleep is null
                    ? null
                    : (s.LightSleep ?? 0) + (s.DeepSleep ?? 0) + (s.RemSleep ?? 0);
            }
        }
        await db.SaveChangesAsync(ct);
        return dates.Count;
    }
}
