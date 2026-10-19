using Microsoft.EntityFrameworkCore;
using TrainingCoach.Domain.Planning;

namespace TrainingCoach.Infrastructure.Persistence;

public sealed record StoredSession(int Id, PlannedSession Session, PlannedStatus Status, DateOnly? OriginalDate);

/// <summary>Persistence of the training plan; after the one-off import the database is the source of truth (SPEC 5.4).</summary>
public sealed class PlanStore(AppDbContext db)
{
    /// <summary>Stores the plan unless the user already has one (so edits in the app are never overwritten).</summary>
    public async Task<bool> ImportAsync(TrainingPlan plan, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        if (await db.PlannedSessions.AnyAsync(s => s.UserId == userId, ct))
            return false;

        db.Workouts.AddRange(plan.Workouts.Values.Select(w => new WorkoutTemplateEntity
        {
            UserId = userId,
            Name = w.Name,
            TotalKm = w.TotalKm,
            Phases = w.Phases.Select(p => new WorkoutPhaseEntity
            {
                Number = p.Number,
                Name = p.Name,
                Kind = p.Kind,
                DistanceKm = p.DistanceKm,
                Duration = p.Duration,
                Repeats = p.Repeats,
                RepeatFromPhase = p.RepeatFromPhase,
                RepeatToPhase = p.RepeatToPhase,
                Target = p.Target,
                Notes = p.Notes,
            }).ToList(),
        }));

        db.PlannedSessions.AddRange(plan.Sessions.Select(s => new PlannedSessionEntity
        {
            UserId = userId,
            Date = s.Date,
            Sport = s.Sport,
            Title = s.Title,
            Description = s.Description,
            WorkoutName = s.WorkoutName,
            PlannedDistanceKm = s.PlannedDistanceKm,
            Week = s.Week,
            Phase = s.Phase,
            IsCommute = s.IsCommute,
        }));

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> HasPlanAsync(int userId = UserIds.Owner, CancellationToken ct = default) =>
        await db.PlannedSessions.AnyAsync(s => s.UserId == userId, ct);

    public async Task<IReadOnlyList<StoredSession>> GetSessionsAsync(
        DateOnly from, DateOnly to, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var rows = await db.PlannedSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date).ThenBy(s => s.Id)
            .ToListAsync(ct);

        return rows.Select(s => new StoredSession(
            s.Id,
            new PlannedSession(s.Date, s.Sport, s.Title, s.Description, s.WorkoutName, s.PlannedDistanceKm, s.Week, s.Phase, s.IsCommute),
            s.Status,
            s.OriginalDate)).ToList();
    }

    public async Task<WorkoutTemplate?> GetWorkoutAsync(string name, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var w = await db.Workouts.AsNoTracking().Include(x => x.Phases)
            .SingleOrDefaultAsync(x => x.UserId == userId && x.Name == name, ct);
        if (w is null) return null;

        return new WorkoutTemplate(w.Name, w.TotalKm, w.Phases.OrderBy(p => p.Number).Select(p => new WorkoutPhase(
            p.Number, p.Name, p.Kind, p.DistanceKm, p.Duration, p.Repeats, p.RepeatFromPhase, p.RepeatToPhase, p.Target, p.Notes)).ToList());
    }

    public async Task SetStatusAsync(int sessionId, PlannedStatus status, CancellationToken ct = default)
    {
        var session = await db.PlannedSessions.SingleAsync(s => s.Id == sessionId, ct);
        session.Status = status;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Puts a skipped or moved session back as planned on its original date.</summary>
    public async Task RestoreAsync(int sessionId, CancellationToken ct = default)
    {
        var session = await db.PlannedSessions.SingleAsync(s => s.Id == sessionId, ct);
        if (session.OriginalDate is { } original) session.Date = original;
        session.OriginalDate = null;
        session.Status = PlannedStatus.Planned;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Moves a session to another day; keeps the original date and marks it as moved.</summary>
    public async Task MoveAsync(int sessionId, DateOnly newDate, CancellationToken ct = default)
    {
        var session = await db.PlannedSessions.SingleAsync(s => s.Id == sessionId, ct);
        session.OriginalDate ??= session.Date;
        session.Date = newDate;
        session.Status = PlannedStatus.Moved;
        await db.SaveChangesAsync(ct);
    }
}
