using Microsoft.EntityFrameworkCore;
using TrainingCoach.Domain.Planning;

namespace TrainingCoach.Infrastructure.Persistence;

/// <summary>Finds the row of the other source for a session that arrives from Strava or Polar (SPEC 5.5).</summary>
public static class ActivityLinker
{
    /// <summary>A stored Polar-only activity that is the same session as <paramref name="key"/> (arriving from Strava).</summary>
    public static Task<ActivityEntity?> FindPolarOnlyAsync(AppDbContext db, int userId, SessionKey key, CancellationToken ct) =>
        FindAsync(db, userId, key, a => a.PolarId != null && a.StravaId == null, ct);

    /// <summary>A stored Strava-only activity that is the same session as <paramref name="key"/> (arriving from Polar).</summary>
    public static Task<ActivityEntity?> FindStravaOnlyAsync(AppDbContext db, int userId, SessionKey key, CancellationToken ct) =>
        FindAsync(db, userId, key, a => a.StravaId != null && a.PolarId == null, ct);

    private static async Task<ActivityEntity?> FindAsync(
        AppDbContext db, int userId, SessionKey key, System.Linq.Expressions.Expression<Func<ActivityEntity, bool>> source, CancellationToken ct)
    {
        var low = key.StartUtc - SessionDeduplication.MaxStartDifference;
        var high = key.StartUtc + SessionDeduplication.MaxStartDifference;

        var candidates = await db.Activities
            .Where(a => a.UserId == userId)
            .Where(source)
            .Where(a => a.StartUtc >= low && a.StartUtc <= high)
            .ToListAsync(ct);

        return candidates
            .Select(a => (Entity: a, Distance: (a.StartUtc - key.StartUtc).Duration()))
            .Where(c => SessionDeduplication.IsSameSession(key, new SessionKey(c.Entity.Sport, c.Entity.StartUtc, c.Entity.ElapsedSeconds)))
            .OrderBy(c => c.Distance)
            .Select(c => c.Entity)
            .FirstOrDefault();
    }
}
