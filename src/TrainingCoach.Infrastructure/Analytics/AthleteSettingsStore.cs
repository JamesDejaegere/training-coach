using Microsoft.EntityFrameworkCore;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Analytics;

/// <summary>Reads and saves the athlete settings (SPEC 6, screen "Instellingen").</summary>
public sealed class AthleteSettingsStore(AppDbContext db)
{
    /// <summary>The stored settings, or a new default instance (not yet saved).</summary>
    public async Task<AthleteSettingsEntity> GetAsync(int userId = UserIds.Owner, CancellationToken ct = default) =>
        await db.AthleteSettings.SingleOrDefaultAsync(s => s.UserId == userId, ct) ?? new AthleteSettingsEntity { UserId = userId };

    public async Task SaveAsync(AthleteSettingsEntity settings, CancellationToken ct = default)
    {
        if (settings.HrMax is < 100 or > 230)
            throw new ArgumentOutOfRangeException(nameof(settings), "Max HR must be between 100 and 230.");
        if (settings.RestingHrOverride is { } rest && (rest < 30 || rest >= settings.HrMax))
            throw new ArgumentOutOfRangeException(nameof(settings), "Resting HR must be between 30 and max HR.");

        if (settings.Id == 0) db.AthleteSettings.Add(settings);
        await db.SaveChangesAsync(ct);
    }
}
