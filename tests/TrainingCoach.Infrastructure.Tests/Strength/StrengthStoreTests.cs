using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Strength;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Strength;
using TrainingCoach.Infrastructure.Tests.Persistence;

namespace TrainingCoach.Infrastructure.Tests.Strength;

public sealed class StrengthStoreTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2027, 2, 10, 7, 0, 0, TimeSpan.Zero));

    public StrengthStoreTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels"));

    public void Dispose() => _db.Dispose();

    private (StrengthStore Store, ExerciseCatalog Catalog, AppDbContext Db) Create()
    {
        var db = _db.CreateContext();
        var catalog = new ExerciseCatalog(db);
        return (new StrengthStore(db, catalog, _time), catalog, db);
    }

    private static async Task CompleteAllAsync(StrengthStore store, StrengthSessionView view, int reps)
    {
        foreach (var set in view.Entries.SelectMany(e => e.Sets))
            await store.UpdateSetAsync(set.Id, reps, 0, completed: true, addedLoadKg: null);
    }

    // ---- sessions and sets ----

    [Fact]
    public async Task Starting_a_session_creates_the_prescribed_sets_prefilled_with_the_target_and_per_side_for_unilateral_exercises()
    {
        var (store, _, _) = Create();

        var id = await store.StartSessionAsync(["bodyweight-squat", "calf-raise-single-leg", "plank"]);
        var view = (await store.GetSessionAsync(id))!;

        Assert.Equal(["bodyweight-squat", "calf-raise-single-leg", "plank"], view.Entries.Select(e => e.Exercise.Key));

        var squat = view.Entries[0].Sets;
        Assert.Equal(3, squat.Count);
        Assert.All(squat, s => Assert.Equal((15, "None", false), (s.Reps, s.Side, s.Completed)));

        var calf = view.Entries[1].Sets;
        Assert.Equal(6, calf.Count); // 3 sets x 2 legs
        Assert.Equal(["Left", "Right", "Left", "Right", "Left", "Right"], calf.Select(s => s.Side));
        Assert.Equal([1, 1, 2, 2, 3, 3], calf.Select(s => s.Number));

        var plank = view.Entries[2].Sets;
        Assert.All(plank, s => Assert.Equal((0, 45), (s.Reps, s.DurationSeconds))); // timed: seconds prefilled
        Assert.Equal(StrengthSessionStatus.InProgress, view.Session.Status);
    }

    [Fact]
    public async Task Unknown_exercise_keys_are_rejected()
    {
        var (store, _, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => store.StartSessionAsync(["does-not-exist"]));
    }

    [Fact]
    public async Task Sets_can_be_updated_added_and_removed()
    {
        var (store, _, _) = Create();
        var id = await store.StartSessionAsync(["bodyweight-squat"]);
        var view = (await store.GetSessionAsync(id))!;
        var first = view.Entries[0].Sets[0];

        await store.UpdateSetAsync(first.Id, 12, 0, completed: true, addedLoadKg: 5);
        await store.AddSetAsync(view.Entries[0].Entry.Id);
        await store.RemoveSetAsync(view.Entries[0].Sets[1].Id);

        var after = (await store.GetSessionAsync(id))!.Entries[0].Sets;
        Assert.Equal(3, after.Count); // 3 + 1 added - 1 removed
        Assert.Equal((12, true, 5.0), (after[0].Reps, after[0].Completed, after[0].AddedLoadKg));
        Assert.Equal(after.Select(s => s.Number).Order(), after.Select(s => s.Number)); // numbering stays ordered
    }

    [Fact]
    public async Task Added_set_copies_the_values_of_the_last_set()
    {
        var (store, _, _) = Create();
        var id = await store.StartSessionAsync(["bodyweight-squat"]);
        var entry = (await store.GetSessionAsync(id))!.Entries[0];
        await store.UpdateSetAsync(entry.Sets[^1].Id, 9, 0, false, null);

        await store.AddSetAsync(entry.Entry.Id);

        var sets = (await store.GetSessionAsync(id))!.Entries[0].Sets;
        Assert.Equal((4, 9), (sets[^1].Number, sets[^1].Reps));
    }

    [Fact]
    public async Task Exercises_can_be_added_and_removed_during_the_session()
    {
        var (store, _, _) = Create();
        var id = await store.StartSessionAsync(["plank"]);

        await store.AddExerciseAsync(id, "glute-bridge");
        var view = (await store.GetSessionAsync(id))!;
        Assert.Equal(["plank", "glute-bridge"], view.Entries.Select(e => e.Exercise.Key));

        await store.RemoveExerciseAsync(view.Entries[0].Entry.Id);
        view = (await store.GetSessionAsync(id))!;
        Assert.Equal(["glute-bridge"], view.Entries.Select(e => e.Exercise.Key));
    }

    // ---- finishing ----

    [Fact]
    public async Task Finishing_creates_a_strength_activity_with_duration_rpe_and_local_start()
    {
        var (store, _, db) = Create();
        var id = await store.StartSessionAsync(["bodyweight-squat"]);
        _time.Advance(TimeSpan.FromMinutes(27));

        var activityId = await store.FinishAsync(id, rpe: 6);

        var activity = await db.Activities.SingleAsync(a => a.Id == activityId);
        Assert.Equal((SportFamily.Strength, 6, 27 * 60), (activity.Sport, activity.Rpe, activity.MovingSeconds));
        Assert.Equal(new DateTime(2027, 2, 10, 7, 0, 0, DateTimeKind.Utc), activity.StartUtc);
        Assert.Equal(new DateTime(2027, 2, 10, 8, 0, 0), activity.StartLocal); // Brussels is UTC+1 in February
        Assert.Null(activity.StravaId);

        var session = (await store.GetSessionAsync(id))!.Session;
        Assert.Equal((StrengthSessionStatus.Finished, activityId, 6), (session.Status, session.ActivityId, session.Rpe));
    }

    [Fact]
    public async Task Finishing_validates_rpe_and_cannot_be_done_twice()
    {
        var (store, _, _) = Create();
        var id = await store.StartSessionAsync(["plank"]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.FinishAsync(id, 11));
        await store.FinishAsync(id, 5);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FinishAsync(id, 5));
    }

    [Fact]
    public async Task Finished_strength_activity_counts_for_load_via_session_rpe()
    {
        var (store, _, db) = Create();
        var id = await store.StartSessionAsync(["plank"]);
        _time.Advance(TimeSpan.FromMinutes(20));
        await store.FinishAsync(id, 7);

        var activity = await db.Activities.AsNoTracking().SingleAsync();

        var load = TrainingCoach.Infrastructure.Analytics.TrainingAnalytics.LoadOf(activity,
            new TrainingCoach.Infrastructure.Analytics.AthleteProfile(168, 55, null, null, 500));
        Assert.Equal(TrainingCoach.Domain.Calculations.LoadMethod.SessionRpe, load.Method);
    }

    [Fact]
    public async Task Discarding_removes_the_session_with_its_entries_and_sets()
    {
        var (store, _, db) = Create();
        var id = await store.StartSessionAsync(["plank", "glute-bridge"]);

        await store.DiscardAsync(id);

        Assert.Null(await store.GetSessionAsync(id));
        Assert.Empty(await db.StrengthEntries.ToListAsync());
        Assert.Empty(await db.StrengthSets.ToListAsync());
    }

    // ---- previous performance and progression ----

    [Fact]
    public async Task Previous_performance_comes_from_the_last_finished_session_and_prefills_the_next_one()
    {
        var (store, _, _) = Create();
        var first = await store.StartSessionAsync(["bodyweight-squat"]);
        var view = (await store.GetSessionAsync(first))!;
        var sets = view.Entries[0].Sets;
        await store.UpdateSetAsync(sets[0].Id, 14, 0, true, null);
        await store.UpdateSetAsync(sets[1].Id, 13, 0, true, null);
        await store.UpdateSetAsync(sets[2].Id, 12, 0, true, null);
        await store.FinishAsync(first, 6);
        _time.Advance(TimeSpan.FromDays(3));

        var second = await store.StartSessionAsync(["bodyweight-squat"]);
        var entry = (await store.GetSessionAsync(second))!.Entries[0];

        Assert.NotNull(entry.Previous);
        Assert.Equal(6, entry.Previous.Rpe);
        Assert.Equal([14, 13, 12], entry.Previous.Sets.Select(s => s.Reps));
        Assert.Equal([14, 13, 12], entry.Sets.Select(s => s.Reps)); // prefilled from last time
        Assert.All(entry.Sets, s => Assert.False(s.Completed));
    }

    [Fact]
    public async Task Prefill_works_when_previous_sets_are_identical_and_for_both_sides_of_one_sided_exercises()
    {
        var (store, _, _) = Create();
        var first = await store.StartSessionAsync(["bodyweight-squat", "calf-raise-single-leg"]);
        foreach (var set in (await store.GetSessionAsync(first))!.Entries[0].Sets)
            await store.UpdateSetAsync(set.Id, 11, 0, true, null);
        var calfSets = (await store.GetSessionAsync(first))!.Entries[1].Sets; // L,R x 3
        for (var i = 0; i < calfSets.Count; i++)
            await store.UpdateSetAsync(calfSets[i].Id, calfSets[i].Side == "Left" ? 14 : 9, 0, true, null);
        await store.FinishAsync(first, 6);

        var second = await store.StartSessionAsync(["bodyweight-squat", "calf-raise-single-leg"]);
        var view = (await store.GetSessionAsync(second))!;

        Assert.Equal([11, 11, 11], view.Entries[0].Sets.Select(s => s.Reps));
        Assert.Equal([14, 9, 14, 9, 14, 9], view.Entries[1].Sets.Select(s => s.Reps)); // left and right keep their own numbers
    }

    [Fact]
    public async Task Unfinished_sessions_are_not_used_as_previous_performance()
    {
        var (store, _, _) = Create();
        var first = await store.StartSessionAsync(["plank"]);
        await CompleteAllAsync(store, (await store.GetSessionAsync(first))!, 0);

        var second = await store.StartSessionAsync(["plank"]);

        Assert.Null((await store.GetSessionAsync(second))!.Entries[0].Previous);
    }

    [Fact]
    public async Task Ladder_status_suggests_the_next_step_after_a_successful_easy_session()
    {
        var (store, _, _) = Create();
        var id = await store.StartSessionAsync(["calf-raise-two-leg"]);
        await CompleteAllAsync(store, (await store.GetSessionAsync(id))!, 15);
        await store.FinishAsync(id, 6);

        var status = await store.LadderStatusAsync();

        var calf = status.Single(s => s.Ladder.Key == "calf");
        Assert.Equal("calf-raise-two-leg", calf.Current?.Key);
        Assert.Equal("calf-raise-single-leg", calf.Suggestion?.Key);
        Assert.Null(status.Single(s => s.Ladder.Key == "squat").Current); // no history yet
    }

    [Fact]
    public async Task Ladder_status_does_not_suggest_when_the_session_felt_hard()
    {
        var (store, _, _) = Create();
        var id = await store.StartSessionAsync(["calf-raise-two-leg"]);
        await CompleteAllAsync(store, (await store.GetSessionAsync(id))!, 15);
        await store.FinishAsync(id, 9);

        Assert.Null((await store.LadderStatusAsync()).Single(s => s.Ladder.Key == "calf").Suggestion);
    }

    // ---- muscle load ----

    [Fact]
    public async Task Session_muscle_load_counts_completed_sets_only()
    {
        var (store, _, _) = Create();
        var id = await store.StartSessionAsync(["calf-raise-two-leg"]);
        var sets = (await store.GetSessionAsync(id))!.Entries[0].Sets;
        await store.UpdateSetAsync(sets[0].Id, 15, 0, true, null);
        await store.UpdateSetAsync(sets[1].Id, 15, 0, true, null);

        var load = StrengthStore.MuscleLoadOf((await store.GetSessionAsync(id))!);

        Assert.Equal(2.0, load[Muscle.Calves]);
    }

    // ---- list ----

    [Fact]
    public async Task List_is_newest_first_with_set_counts()
    {
        var (store, _, _) = Create();
        var a = await store.StartSessionAsync(["plank"]);
        await CompleteAllAsync(store, (await store.GetSessionAsync(a))!, 0);
        await store.FinishAsync(a, 5);
        _time.Advance(TimeSpan.FromDays(2));
        var b = await store.StartSessionAsync(["plank", "glute-bridge"]);

        var list = await store.ListAsync();

        Assert.Equal([b, a], list.Select(s => s.Id));
        Assert.Equal((2, 6, 0), (list[0].ExerciseCount, list[0].SetCount, list[0].CompletedSetCount));
        Assert.Equal(3, list[1].CompletedSetCount);
    }

    // ---- catalog ----

    [Fact]
    public async Task Catalog_returns_the_library_plus_custom_exercises_and_custom_ones_can_be_trained()
    {
        var (store, catalog, _) = Create();
        var custom = await catalog.AddCustomAsync(new CustomExerciseInput(
            "Enkelmobiliteit tegen de muur", MovementPattern.Calf, [Muscle.Calves], [], [Equipment.Wall], false, false, 2, 12, "Duw de knie naar de muur."));

        var all = await catalog.AllAsync();

        Assert.Equal(ExerciseLibrary.All.Count + 1, all.Count);
        Assert.StartsWith("custom-", custom.Key);
        Assert.Equal(custom.Key, (await catalog.FindAsync(custom.Key))!.Key);

        var id = await store.StartSessionAsync([custom.Key]);
        Assert.Equal(2, (await store.GetSessionAsync(id))!.Entries[0].Sets.Count);
    }

    [Fact]
    public async Task Custom_exercise_needs_a_name_and_a_primary_muscle()
    {
        var (_, catalog, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => catalog.AddCustomAsync(
            new CustomExerciseInput(" ", MovementPattern.Core, [Muscle.Abdominals], [], [Equipment.Bodyweight], false, false, 3, 10, "")));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.AddCustomAsync(
            new CustomExerciseInput("Naam", MovementPattern.Core, [], [], [Equipment.Bodyweight], false, false, 3, 10, "")));
    }

    [Fact]
    public async Task Library_exercises_are_found_by_key()
    {
        var (_, catalog, _) = Create();

        Assert.Equal("Squat", (await catalog.FindAsync("bodyweight-squat"))!.NameNl);
        Assert.Null(await catalog.FindAsync("nope"));
    }
}
