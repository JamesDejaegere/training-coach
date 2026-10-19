using Microsoft.EntityFrameworkCore;
using TrainingCoach.Domain;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Analytics;

/// <summary>Activity list/detail plus the after-session entries RPE and pain (D14).</summary>
public sealed class ActivityStore(AppDbContext db)
{
    public async Task<IReadOnlyList<ActivityEntity>> ListAsync(
        SportFamily? sport = null, int take = 100, int skip = 0, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var query = db.Activities.AsNoTracking().Where(a => a.UserId == userId);
        if (sport is { } s) query = query.Where(a => a.Sport == s);
        return await query.OrderByDescending(a => a.StartUtc).Skip(skip).Take(take).ToListAsync(ct);
    }

    public Task<ActivityEntity?> GetAsync(int id, int userId = UserIds.Owner, CancellationToken ct = default) =>
        db.Activities.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);

    public async Task SetRpeAsync(int activityId, int? rpe, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        if (rpe is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(rpe), "RPE must be between 1 and 10.");

        var activity = await db.Activities.SingleAsync(a => a.Id == activityId && a.UserId == userId, ct);
        activity.Rpe = rpe;
        await db.SaveChangesAsync(ct);
    }

    public async Task<PainReportEntity> AddPainAsync(
        int activityId, string location, int severity, string note = "", int userId = UserIds.Owner, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Location is required.", nameof(location));
        if (severity is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(severity), "Severity must be between 1 and 10.");

        var activity = await db.Activities.AsNoTracking().SingleAsync(a => a.Id == activityId && a.UserId == userId, ct);
        var report = new PainReportEntity
        {
            UserId = userId,
            ActivityId = activityId,
            Date = DateOnly.FromDateTime(activity.StartLocal),
            Location = location.Trim().ToLowerInvariant(),
            Severity = severity,
            Note = note.Trim(),
        };
        db.PainReports.Add(report);
        await db.SaveChangesAsync(ct);
        return report;
    }

    public async Task<IReadOnlyList<PainReportEntity>> PainForActivityAsync(int activityId, int userId = UserIds.Owner, CancellationToken ct = default) =>
        await db.PainReports.AsNoTracking().Where(p => p.UserId == userId && p.ActivityId == activityId).OrderBy(p => p.Id).ToListAsync(ct);

    public async Task DeletePainAsync(int painId, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var report = await db.PainReports.SingleOrDefaultAsync(p => p.Id == painId && p.UserId == userId, ct);
        if (report is null) return;
        db.PainReports.Remove(report);
        await db.SaveChangesAsync(ct);
    }
}
