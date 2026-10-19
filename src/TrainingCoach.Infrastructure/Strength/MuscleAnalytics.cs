using Microsoft.EntityFrameworkCore;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Strength;
using TrainingCoach.Infrastructure.Analytics;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Strength;

/// <summary>Rolling muscle load for the heat map (SPEC 7.3): strength sets plus running and cycling.</summary>
public sealed class MuscleAnalytics(AppDbContext db, ExerciseCatalog catalog, TrainingAnalytics analytics)
{
    public async Task<Dictionary<Muscle, double>> RollingAsync(DateOnly endDate, int days = 7, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var from = endDate.AddDays(-(days - 1));
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        // Strength: completed sets of finished sessions in the window (by local start date).
        var sessions = await db.StrengthSessions.AsNoTracking()
            .Include(s => s.Entries).ThenInclude(e => e.Sets)
            .Where(s => s.UserId == userId && s.Status == StrengthSessionStatus.Finished)
            .ToListAsync(ct);

        var sets = new List<(ExerciseDefinition, SetResult)>();
        foreach (var session in sessions)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(session.StartedUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
            var date = await SessionDateAsync(session, ct) ?? DateOnly.FromDateTime(local);
            if (date < from || date > endDate) continue;

            foreach (var entry in session.Entries)
            {
                var exercise = await catalog.FindAsync(entry.ExerciseKey, userId, ct);
                if (exercise is null) continue;
                sets.AddRange(entry.Sets.Select(s => (exercise, new SetResult(s.Reps, s.DurationSeconds, s.Completed,
                    Enum.TryParse<SetSide>(s.Side, out var side) ? side : SetSide.None))));
            }
        }

        // Endurance: running and cycling in proportion to their training load.
        var profile = await analytics.ProfileAsync(userId, ct);
        var activities = await db.Activities.AsNoTracking()
            .Where(a => a.UserId == userId && a.StartLocal >= start && a.StartLocal < end && (a.Sport == SportFamily.Run || a.Sport == SportFamily.Ride))
            .ToListAsync(ct);
        var endurance = activities.Select(a => MuscleLoad.FromEndurance(a.Sport, TrainingAnalytics.LoadOf(a, profile).Value)).ToArray();

        return MuscleLoad.Merge([MuscleLoad.FromSets(sets), .. endurance]);
    }

    /// <summary>The local training day of a finished session: the date of its strength activity when it has one.</summary>
    private async Task<DateOnly?> SessionDateAsync(StrengthSessionEntity session, CancellationToken ct)
    {
        if (session.ActivityId is not { } id) return null;
        var start = await db.Activities.AsNoTracking().Where(a => a.Id == id).Select(a => (DateTime?)a.StartLocal).SingleOrDefaultAsync(ct);
        return start is { } s ? DateOnly.FromDateTime(s) : null;
    }
}
