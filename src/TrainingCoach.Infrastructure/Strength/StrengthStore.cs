using Microsoft.EntityFrameworkCore;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Strength;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Strength;

public sealed record PreviousPerformance(DateOnly Date, int? Rpe, IReadOnlyList<SetResult> Sets);

public sealed record EntryView(
    StrengthEntryEntity Entry,
    ExerciseDefinition Exercise,
    IReadOnlyList<StrengthSetEntity> Sets,
    PreviousPerformance? Previous);

public sealed record StrengthSessionView(StrengthSessionEntity Session, IReadOnlyList<EntryView> Entries);

public sealed record StrengthSessionSummary(
    int Id, DateTime StartedUtc, StrengthSessionStatus Status, int? Rpe, int ExerciseCount, int SetCount, int CompletedSetCount, int? ActivityId);

public sealed record LadderStatus(ProgressionLadder Ladder, ExerciseDefinition? Current, ExerciseDefinition? Suggestion);

/// <summary>Live strength sessions (SPEC 9): sets, previous performance, finishing into a strength activity, ladder progress.</summary>
public sealed class StrengthStore(AppDbContext db, ExerciseCatalog catalog, TimeProvider time)
{
    // ---- sessions ----

    public async Task<int> StartSessionAsync(IReadOnlyList<string> exerciseKeys, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var session = new StrengthSessionEntity { UserId = userId, StartedUtc = time.GetUtcNow().UtcDateTime };

        for (var i = 0; i < exerciseKeys.Count; i++)
            session.Entries.Add(await NewEntryAsync(exerciseKeys[i], i, userId, beforeUtc: null, ct));

        db.StrengthSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.Id;
    }

    public async Task<StrengthSessionView?> GetSessionAsync(int sessionId, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var session = await db.StrengthSessions.AsNoTracking()
            .Include(s => s.Entries).ThenInclude(e => e.Sets)
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return null;

        var entries = new List<EntryView>();
        foreach (var entry in session.Entries.OrderBy(e => e.Order))
        {
            var exercise = await catalog.FindAsync(entry.ExerciseKey, userId, ct)
                           ?? throw new InvalidOperationException($"Exercise '{entry.ExerciseKey}' no longer exists.");
            var previous = await PreviousAsync(entry.ExerciseKey, userId, session.StartedUtc, ct);
            entries.Add(new EntryView(entry, exercise, entry.Sets.OrderBy(s => s.Number).ThenBy(s => s.Side).ToList(), previous));
        }
        return new StrengthSessionView(session, entries);
    }

    public async Task AddExerciseAsync(int sessionId, string exerciseKey, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var session = await db.StrengthSessions.Include(s => s.Entries).SingleAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        var order = session.Entries.Count == 0 ? 0 : session.Entries.Max(e => e.Order) + 1;
        session.Entries.Add(await NewEntryAsync(exerciseKey, order, userId, session.StartedUtc, ct));
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveExerciseAsync(int entryId, CancellationToken ct = default)
    {
        var entry = await db.StrengthEntries.Include(e => e.Sets).SingleAsync(e => e.Id == entryId, ct);
        db.StrengthEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task DiscardAsync(int sessionId, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var session = await db.StrengthSessions.Include(s => s.Entries).ThenInclude(e => e.Sets)
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return;
        db.StrengthSessions.Remove(session);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ends the session and records it as a strength activity so session-RPE load, plan matching and pain warnings apply.</summary>
    public async Task<int> FinishAsync(int sessionId, int? rpe, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        if (rpe is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(rpe), "RPE must be between 1 and 10.");

        var session = await db.StrengthSessions.SingleAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session.Status == StrengthSessionStatus.Finished) throw new InvalidOperationException("The session is already finished.");

        var ended = time.GetUtcNow().UtcDateTime;
        var seconds = (int)Math.Max(0, (ended - session.StartedUtc).TotalSeconds);
        var started = DateTime.SpecifyKind(session.StartedUtc, DateTimeKind.Utc);

        var activity = new ActivityEntity
        {
            UserId = userId,
            Name = "Krachttraining",
            Sport = SportFamily.Strength,
            StartUtc = started,
            StartLocal = TimeZoneInfo.ConvertTimeFromUtc(started, time.LocalTimeZone),
            MovingSeconds = seconds,
            ElapsedSeconds = seconds,
            Rpe = rpe,
        };
        db.Activities.Add(activity);

        session.EndedUtc = ended;
        session.Status = StrengthSessionStatus.Finished;
        session.Rpe = rpe;
        await db.SaveChangesAsync(ct);

        session.ActivityId = activity.Id;
        await db.SaveChangesAsync(ct);
        return activity.Id;
    }

    public async Task<IReadOnlyList<StrengthSessionSummary>> ListAsync(int take = 30, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var sessions = await db.StrengthSessions.AsNoTracking()
            .Include(s => s.Entries).ThenInclude(e => e.Sets)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedUtc).ThenByDescending(s => s.Id)
            .Take(take)
            .ToListAsync(ct);

        return sessions.Select(s => new StrengthSessionSummary(
            s.Id, s.StartedUtc, s.Status, s.Rpe,
            s.Entries.Count,
            s.Entries.Sum(e => e.Sets.Count),
            s.Entries.Sum(e => e.Sets.Count(x => x.Completed)),
            s.ActivityId)).ToList();
    }

    // ---- sets ----

    public async Task UpdateSetAsync(int setId, int reps, int durationSeconds, bool completed, double? addedLoadKg, CancellationToken ct = default)
    {
        var set = await db.StrengthSets.SingleAsync(s => s.Id == setId, ct);
        set.Reps = Math.Max(0, reps);
        set.DurationSeconds = Math.Max(0, durationSeconds);
        set.Completed = completed;
        set.AddedLoadKg = addedLoadKg;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds a set (both sides for one-sided exercises) copying the values of the last one.</summary>
    public async Task AddSetAsync(int entryId, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var entry = await db.StrengthEntries.Include(e => e.Sets).SingleAsync(e => e.Id == entryId, ct);
        var exercise = await catalog.FindAsync(entry.ExerciseKey, userId, ct) ?? throw new InvalidOperationException("Unknown exercise.");
        var last = entry.Sets.OrderBy(s => s.Number).ThenBy(s => s.Side).LastOrDefault();
        var number = entry.Sets.Count == 0 ? 1 : entry.Sets.Max(s => s.Number) + 1;

        foreach (var side in exercise.Unilateral ? new[] { SetSide.Left, SetSide.Right } : [SetSide.None])
        {
            entry.Sets.Add(new StrengthSetEntity
            {
                Number = number,
                Side = side.ToString(),
                Reps = last?.Reps ?? (exercise.Timed ? 0 : exercise.Target),
                DurationSeconds = last?.DurationSeconds ?? (exercise.Timed ? exercise.Target : 0),
                AddedLoadKg = last?.AddedLoadKg,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveSetAsync(int setId, CancellationToken ct = default)
    {
        var set = await db.StrengthSets.SingleAsync(s => s.Id == setId, ct);
        db.StrengthSets.Remove(set);
        await db.SaveChangesAsync(ct);
    }

    // ---- history and progression ----

    /// <summary>The most recent finished session (started before <paramref name="beforeUtc"/>) that contains the exercise.</summary>
    public async Task<PreviousPerformance?> PreviousAsync(string exerciseKey, int userId = UserIds.Owner, DateTime? beforeUtc = null, CancellationToken ct = default)
    {
        var query = db.StrengthSessions.AsNoTracking()
            .Include(s => s.Entries).ThenInclude(e => e.Sets)
            .Where(s => s.UserId == userId && s.Status == StrengthSessionStatus.Finished && s.Entries.Any(e => e.ExerciseKey == exerciseKey));
        if (beforeUtc is { } before) query = query.Where(s => s.StartedUtc < before);

        var session = await query.OrderByDescending(s => s.StartedUtc).FirstOrDefaultAsync(ct);
        var entry = session?.Entries.FirstOrDefault(e => e.ExerciseKey == exerciseKey);
        if (session is null || entry is null || !entry.Sets.Any(s => s.Completed)) return null;

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(session.StartedUtc, DateTimeKind.Utc), time.LocalTimeZone);
        return new PreviousPerformance(
            DateOnly.FromDateTime(local),
            session.Rpe,
            entry.Sets.OrderBy(s => s.Number).ThenBy(s => s.Side).Select(ToResult).ToList());
    }

    /// <summary>Where the athlete stands on each progression ladder and whether the next step is due (SPEC 9).</summary>
    public async Task<IReadOnlyList<LadderStatus>> LadderStatusAsync(int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var sessions = await db.StrengthSessions.AsNoTracking()
            .Include(s => s.Entries).ThenInclude(e => e.Sets)
            .Where(s => s.UserId == userId && s.Status == StrengthSessionStatus.Finished)
            .OrderByDescending(s => s.StartedUtc)
            .ToListAsync(ct);

        var result = new List<LadderStatus>();
        foreach (var ladder in ExerciseLibrary.Ladders)
        {
            var latest = sessions
                .SelectMany(s => s.Entries.Where(e => ladder.Steps.Contains(e.ExerciseKey) && e.Sets.Any(x => x.Completed)).Select(e => (Session: s, Entry: e)))
                .FirstOrDefault();

            if (latest.Entry is null)
            {
                result.Add(new LadderStatus(ladder, null, null));
                continue;
            }

            var current = ExerciseLibrary.ByKey[latest.Entry.ExerciseKey];
            var suggestion = ProgressionRule.Suggest(current, latest.Entry.Sets.Select(ToResult).ToList(), latest.Session.Rpe);
            result.Add(new LadderStatus(ladder, current, suggestion));
        }
        return result;
    }

    public static Dictionary<Muscle, double> MuscleLoadOf(StrengthSessionView view) =>
        MuscleLoad.FromSets(view.Entries.SelectMany(e => e.Sets.Select(s => (e.Exercise, ToResult(s)))));

    // ---- helpers ----

    private async Task<StrengthEntryEntity> NewEntryAsync(string key, int order, int userId, DateTime? beforeUtc, CancellationToken ct)
    {
        var exercise = await catalog.FindAsync(key, userId, ct) ?? throw new ArgumentException($"Unknown exercise '{key}'.", nameof(key));
        var previous = await PreviousAsync(key, userId, beforeUtc, ct);

        var entry = new StrengthEntryEntity { ExerciseKey = key, Order = order };
        var sides = exercise.Unilateral ? new[] { SetSide.Left, SetSide.Right } : [SetSide.None];
        for (var number = 1; number <= exercise.Sets; number++)
        {
            for (var sideIndex = 0; sideIndex < sides.Length; sideIndex++)
            {
                var side = sides[sideIndex];
                // Previous sets are ordered by number, then side: the same position holds the same set.
                var position = (number - 1) * sides.Length + sideIndex;
                SetResult? last = previous is not null && position < previous.Sets.Count ? previous.Sets[position] : null;
                entry.Sets.Add(new StrengthSetEntity
                {
                    Number = number,
                    Side = side.ToString(),
                    Reps = exercise.Timed ? 0 : last is { Completed: true } l ? l.Reps : exercise.Target,
                    DurationSeconds = exercise.Timed ? (last is { Completed: true } t ? t.DurationSeconds : exercise.Target) : 0,
                });
            }
        }
        return entry;
    }

    private static SetResult ToResult(StrengthSetEntity s) =>
        new(s.Reps, s.DurationSeconds, s.Completed, Enum.TryParse<SetSide>(s.Side, out var side) ? side : SetSide.None);
}
