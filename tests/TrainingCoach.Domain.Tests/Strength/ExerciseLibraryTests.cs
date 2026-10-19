using TrainingCoach.Domain.Strength;

namespace TrainingCoach.Domain.Tests.Strength;

public class ExerciseLibraryTests
{
    [Fact]
    public void Keys_are_unique_and_every_exercise_is_fully_described_in_dutch_and_english()
    {
        var all = ExerciseLibrary.All;

        Assert.Equal(all.Count, all.Select(e => e.Key).Distinct().Count());
        Assert.All(all, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.NameNl), e.Key);
            Assert.False(string.IsNullOrWhiteSpace(e.NameEn), e.Key);
            Assert.True(e.InstructionsNl.Length > 40, $"{e.Key}: instructions too short");
            Assert.NotEmpty(e.Primary);
            Assert.DoesNotContain(e.Primary, m => e.Secondary.Contains(m));
            Assert.True(e.Sets >= 1 && e.Target >= 1, e.Key);
        });
    }

    [Fact]
    public void Every_exercise_can_be_done_with_the_home_equipment_only()
    {
        Assert.All(ExerciseLibrary.All, e => Assert.All(e.Equipment, q => Assert.Contains(q, ExerciseLibrary.HomeEquipment)));
    }

    [Fact]
    public void Library_covers_calf_hamstring_hip_and_core_every_week_requirement()
    {
        var patterns = ExerciseLibrary.All.Select(e => e.Pattern).ToHashSet();

        Assert.Contains(MovementPattern.Calf, patterns);
        Assert.Contains(MovementPattern.Hamstring, patterns);
        Assert.Contains(MovementPattern.Hip, patterns);
        Assert.Contains(MovementPattern.Core, patterns);
        Assert.Contains(MovementPattern.Squat, patterns);
        Assert.True(ExerciseLibrary.All.Count >= 28);
    }

    [Fact]
    public void Spec_ladders_exist_in_the_documented_order()
    {
        Assert.Equal(
            ["calf-raise-two-leg", "calf-raise-single-leg", "calf-raise-single-leg-slow", "calf-raise-single-leg-pause"],
            ExerciseLibrary.Ladder("calf").Steps);
        Assert.Equal(
            ["glute-bridge", "single-leg-bridge", "towel-hamstring-curl", "nordic-curl"],
            ExerciseLibrary.Ladder("hamstring").Steps);
        Assert.Equal(
            ["bodyweight-squat", "split-squat", "bulgarian-split-squat", "skater-squat"],
            ExerciseLibrary.Ladder("squat").Steps);
    }

    [Fact]
    public void Ladder_members_exist_and_know_their_position()
    {
        foreach (var ladder in ExerciseLibrary.Ladders)
        {
            for (var i = 0; i < ladder.Steps.Count; i++)
            {
                var exercise = ExerciseLibrary.ByKey[ladder.Steps[i]];
                Assert.Equal((ladder.Key, i), (exercise.LadderKey, exercise.LadderStep));
            }
        }
        Assert.All(ExerciseLibrary.All.Where(e => e.LadderKey is not null), e => Assert.Contains(e.Key, ExerciseLibrary.Ladder(e.LadderKey!).Steps));
    }

    [Fact]
    public void Routine_from_the_plan_has_six_known_exercises_covering_calf_hamstring_hip_and_core()
    {
        var routine = ExerciseLibrary.Routine.Select(k => ExerciseLibrary.ByKey[k]).ToList();

        Assert.Equal(6, routine.Count);
        var patterns = routine.Select(e => e.Pattern).ToHashSet();
        Assert.Superset(new HashSet<MovementPattern> { MovementPattern.Calf, MovementPattern.Hamstring, MovementPattern.Hip, MovementPattern.Core }, patterns);
    }

    [Fact]
    public void Unilateral_exercises_are_marked_so_sets_are_logged_per_side()
    {
        Assert.True(ExerciseLibrary.ByKey["calf-raise-single-leg"].Unilateral);
        Assert.True(ExerciseLibrary.ByKey["bulgarian-split-squat"].Unilateral);
        Assert.False(ExerciseLibrary.ByKey["bodyweight-squat"].Unilateral);
    }

    [Fact]
    public void Muscles_have_dutch_names()
    {
        Assert.All(Enum.GetValues<Muscle>(), m => Assert.False(string.IsNullOrWhiteSpace(MuscleNames.Dutch(m)), m.ToString()));
        Assert.Equal("Kuiten", MuscleNames.Dutch(Muscle.Calves));
    }
}
