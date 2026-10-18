using Microsoft.EntityFrameworkCore;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Planning;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.PlanImport;

namespace TrainingCoach.Infrastructure.Tests.Persistence;

public sealed class PlanStoreTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static TrainingPlan SmallPlan()
    {
        var phases = new List<WorkoutPhase>
        {
            new(1, "Opwarmen", PhaseKind.Distance, 1.0, null, null, null, null, "HR-zone 1–2", "Rustig"),
            new(2, "Herhaal 3×", PhaseKind.Repeat, null, null, 3, 3, 4, "—", "Herhaal fasen 3–4"),
            new(3, "Stevig", PhaseKind.Time, null, TimeSpan.FromMinutes(1), null, null, null, "Tempozone 4", ""),
            new(4, "Rustig", PhaseKind.Time, null, TimeSpan.FromSeconds(90), null, null, null, "HR-zone 1–2", ""),
        };
        var workout = new WorkoutTemplate("Fartlek 3×1'", 5.5, phases);
        var monday = new DateOnly(2026, 10, 19);
        return new TrainingPlan(
        [
            new PlannedSession(monday, SportFamily.Run, "Fartlek 3×1'", "Opwarmen → Stevig", "Fartlek 3×1'", 5.5, 1, "1 · Basis", false),
            new PlannedSession(monday.AddDays(1), SportFamily.Ride, "Pendel Aalter–Gent", "Pendel", null, 60, 1, "1 · Basis", true),
        ],
        new Dictionary<string, WorkoutTemplate> { [workout.Name] = workout });
    }

    [Fact]
    public async Task Import_then_load_roundtrips_sessions_and_workout_phases()
    {
        await using var db = _db.CreateContext();
        var store = new PlanStore(db);

        Assert.True(await store.ImportAsync(SmallPlan()));

        await using var fresh = _db.CreateContext();
        var sessions = await new PlanStore(fresh).GetSessionsAsync(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25));
        Assert.Equal(2, sessions.Count);
        Assert.Equal(SmallPlan().Sessions[0], sessions[0].Session);
        Assert.Equal(SmallPlan().Sessions[1], sessions[1].Session);
        Assert.All(sessions, s => Assert.Equal(PlannedStatus.Planned, s.Status));

        var workout = await new PlanStore(fresh).GetWorkoutAsync("Fartlek 3×1'");
        Assert.NotNull(workout);
        Assert.Equal(5.5, workout.TotalKm);
        Assert.Equal(SmallPlan().Workouts["Fartlek 3×1'"].Phases, workout.Phases);
    }

    [Fact]
    public async Task Second_import_is_refused_so_edits_in_the_app_are_not_overwritten()
    {
        await using var db = _db.CreateContext();
        var store = new PlanStore(db);
        await store.ImportAsync(SmallPlan());

        Assert.False(await store.ImportAsync(SmallPlan()));
        Assert.Equal(2, await db.PlannedSessions.CountAsync());
    }

    [Fact]
    public async Task Sessions_are_returned_for_the_requested_range_ordered_by_date()
    {
        await using var db = _db.CreateContext();
        var store = new PlanStore(db);
        await store.ImportAsync(SmallPlan());

        var tuesdayOnly = await store.GetSessionsAsync(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 20));

        Assert.Single(tuesdayOnly);
        Assert.True(tuesdayOnly[0].Session.IsCommute);
    }

    [Fact]
    public async Task Status_and_date_can_be_changed()
    {
        await using var db = _db.CreateContext();
        var store = new PlanStore(db);
        await store.ImportAsync(SmallPlan());
        var id = (await store.GetSessionsAsync(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19)))[0].Id;

        await store.SetStatusAsync(id, PlannedStatus.Skipped);
        await store.MoveAsync(id, new DateOnly(2026, 10, 22));

        await using var fresh = _db.CreateContext();
        var moved = (await new PlanStore(fresh).GetSessionsAsync(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22))).Single();
        Assert.Equal(id, moved.Id);
        Assert.Equal(PlannedStatus.Moved, moved.Status); // moving after skipping wins: the session is planned elsewhere
    }

    [Fact]
    public async Task Every_row_belongs_to_a_user()
    {
        await using var db = _db.CreateContext();
        await new PlanStore(db).ImportAsync(SmallPlan());

        Assert.All(await db.PlannedSessions.ToListAsync(), s => Assert.Equal(UserIds.Owner, s.UserId));
        Assert.All(await db.Workouts.ToListAsync(), w => Assert.Equal(UserIds.Owner, w.UserId));
    }

    [Fact]
    public async Task The_real_seed_plan_can_be_stored_and_read_back()
    {
        var seed = FindSeed();
        var plan = ExcelPlanImporter.Import(seed);
        await using var db = _db.CreateContext();
        var store = new PlanStore(db);

        Assert.True(await store.ImportAsync(plan));

        var all = await store.GetSessionsAsync(DateOnly.MinValue, DateOnly.MaxValue);
        Assert.Equal(plan.Sessions.Count, all.Count);
        Assert.Equal(plan.Workouts.Count, await db.Workouts.CountAsync());
        Assert.Equal(plan.Workouts.Values.Sum(w => w.Phases.Count), await db.Set<WorkoutPhaseEntity>().CountAsync());
    }

    [Fact]
    public void Model_has_no_pending_changes_compared_to_the_migrations()
    {
        using var db = _db.CreateContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static string FindSeed()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "seed", "Schema_Antwerp_10_Miles_2027.xlsx");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Seed workbook not found");
    }
}
