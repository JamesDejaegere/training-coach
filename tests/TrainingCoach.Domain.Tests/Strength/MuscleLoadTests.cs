using TrainingCoach.Domain.Strength;

namespace TrainingCoach.Domain.Tests.Strength;

public class MuscleLoadTests
{
    private static ExerciseDefinition Ex(string key) => ExerciseLibrary.ByKey[key];

    private static SetResult Done(int reps = 10, SetSide side = SetSide.None) => new(reps, 0, true, side);

    [Fact]
    public void Completed_sets_count_one_for_primary_and_half_for_secondary_muscles()
    {
        var squat = Ex("bodyweight-squat");
        var load = MuscleLoad.FromSets(Enumerable.Repeat((squat, Done()), 3));

        foreach (var m in squat.Primary) Assert.Equal(3.0, load[m]);
        foreach (var m in squat.Secondary) Assert.Equal(1.5, load[m]);
    }

    [Fact]
    public void Incomplete_sets_do_not_count()
    {
        var squat = Ex("bodyweight-squat");

        var load = MuscleLoad.FromSets([(squat, Done()), (squat, new SetResult(10, 0, false, SetSide.None))]);

        Assert.Equal(1.0, load[squat.Primary[0]]);
    }

    [Fact]
    public void Every_side_of_a_unilateral_set_counts()
    {
        var calf = Ex("calf-raise-single-leg");

        var load = MuscleLoad.FromSets([(calf, Done(15, SetSide.Left)), (calf, Done(15, SetSide.Right))]);

        Assert.Equal(2.0, load[Muscle.Calves]);
    }

    [Fact]
    public void Muscles_in_both_primary_and_secondary_of_different_exercises_add_up()
    {
        var load = MuscleLoad.FromSets([(Ex("glute-bridge"), Done()), (Ex("bodyweight-squat"), Done())]);

        var expected = (Ex("glute-bridge").Primary.Contains(Muscle.Glutes) ? 1.0 : 0.5) + (Ex("bodyweight-squat").Primary.Contains(Muscle.Glutes) ? 1.0 : 0.5);
        Assert.Equal(expected, load[Muscle.Glutes]);
    }

    [Fact]
    public void Running_loads_calves_hamstrings_quads_glutes_fully_and_hip_flexors_half()
    {
        var load = MuscleLoad.FromEndurance(SportFamily.Run, trainingLoad: 100);

        var unit = 100 * MuscleLoad.EnduranceSetsPerLoad;
        Assert.Equal(unit, load[Muscle.Calves]);
        Assert.Equal(unit, load[Muscle.Hamstrings]);
        Assert.Equal(unit, load[Muscle.Quadriceps]);
        Assert.Equal(unit, load[Muscle.Glutes]);
        Assert.Equal(unit * 0.5, load[Muscle.HipFlexors]);
        Assert.False(load.ContainsKey(Muscle.Chest));
    }

    [Fact]
    public void Endurance_load_is_proportional_to_training_load()
    {
        var small = MuscleLoad.FromEndurance(SportFamily.Run, 50);
        var big = MuscleLoad.FromEndurance(SportFamily.Run, 150);

        Assert.Equal(small[Muscle.Calves] * 3, big[Muscle.Calves], precision: 9);
    }

    [Fact]
    public void Cycling_loads_the_legs_but_not_hip_flexors_like_running()
    {
        var load = MuscleLoad.FromEndurance(SportFamily.Ride, 100);

        Assert.True(load[Muscle.Quadriceps] > load[Muscle.Calves]);
        Assert.True(load[Muscle.Glutes] > 0);
        Assert.False(load.ContainsKey(Muscle.HipFlexors));
    }

    [Theory]
    [InlineData(SportFamily.Strength)]
    [InlineData(SportFamily.Mobility)]
    [InlineData(SportFamily.Other)]
    public void Other_sports_add_no_endurance_muscle_load(SportFamily sport)
    {
        Assert.Empty(MuscleLoad.FromEndurance(sport, 100));
    }

    [Fact]
    public void Merge_adds_loads_per_muscle()
    {
        var a = new Dictionary<Muscle, double> { [Muscle.Calves] = 2, [Muscle.Glutes] = 1 };
        var b = new Dictionary<Muscle, double> { [Muscle.Calves] = 3, [Muscle.Chest] = 1 };

        var merged = MuscleLoad.Merge(a, b);

        Assert.Equal(5, merged[Muscle.Calves]);
        Assert.Equal(1, merged[Muscle.Glutes]);
        Assert.Equal(1, merged[Muscle.Chest]);
    }

    [Fact]
    public void Intensity_is_relative_to_a_reference_and_clamped_to_one()
    {
        var load = new Dictionary<Muscle, double> { [Muscle.Calves] = 6, [Muscle.Glutes] = 30 };

        var intensity = MuscleLoad.Intensity(load, reference: 12);

        Assert.Equal(0.5, intensity[Muscle.Calves]);
        Assert.Equal(1.0, intensity[Muscle.Glutes]);
        Assert.Equal(0.0, intensity[Muscle.Chest]);
        Assert.Equal(Enum.GetValues<Muscle>().Length, intensity.Count);
    }
}
