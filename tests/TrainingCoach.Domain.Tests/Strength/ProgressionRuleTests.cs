using TrainingCoach.Domain.Strength;

namespace TrainingCoach.Domain.Tests.Strength;

public class ProgressionRuleTests
{
    private static ExerciseDefinition Ex(string key) => ExerciseLibrary.ByKey[key];

    private static SetResult Reps(int reps, bool done = true, SetSide side = SetSide.None) => new(reps, 0, done, side);

    private static SetResult Hold(int seconds, bool done = true) => new(0, seconds, done, SetSide.None);

    [Fact]
    public void Top_of_rep_range_on_all_sets_at_rpe_7_suggests_the_next_step()
    {
        var calf = Ex("calf-raise-two-leg"); // 3 x 15

        var next = ProgressionRule.Suggest(calf, [Reps(15), Reps(15), Reps(15)], sessionRpe: 7);

        Assert.Equal("calf-raise-single-leg", next?.Key);
    }

    [Fact]
    public void Rpe_above_7_keeps_the_current_step()
    {
        Assert.Null(ProgressionRule.Suggest(Ex("calf-raise-two-leg"), [Reps(15), Reps(15), Reps(15)], sessionRpe: 8));
    }

    [Fact]
    public void Unknown_rpe_does_not_suggest_progression()
    {
        Assert.Null(ProgressionRule.Suggest(Ex("calf-raise-two-leg"), [Reps(15), Reps(15), Reps(15)], sessionRpe: null));
    }

    [Fact]
    public void Missing_reps_or_missing_sets_do_not_progress()
    {
        var calf = Ex("calf-raise-two-leg");

        Assert.Null(ProgressionRule.Suggest(calf, [Reps(15), Reps(15), Reps(14)], 6));
        Assert.Null(ProgressionRule.Suggest(calf, [Reps(15), Reps(15)], 6));
        Assert.Null(ProgressionRule.Suggest(calf, [Reps(15), Reps(15), Reps(15, done: false)], 6));
    }

    [Fact]
    public void Extra_sets_are_fine_but_the_prescribed_number_must_reach_the_top()
    {
        Assert.Equal("calf-raise-single-leg",
            ProgressionRule.Suggest(Ex("calf-raise-two-leg"), [Reps(15), Reps(16), Reps(15), Reps(10)], 7)?.Key);
    }

    [Fact]
    public void Both_sides_must_reach_the_top_for_unilateral_exercises()
    {
        var single = Ex("calf-raise-single-leg"); // 3 x 15 per leg
        var both = new[]
        {
            Reps(15, side: SetSide.Left), Reps(15, side: SetSide.Right),
            Reps(15, side: SetSide.Left), Reps(15, side: SetSide.Right),
            Reps(15, side: SetSide.Left), Reps(15, side: SetSide.Right),
        };
        var weakRight = both.Select(s => s.Side == SetSide.Right ? s with { Reps = 12 } : s).ToList();

        Assert.Equal("calf-raise-single-leg-slow", ProgressionRule.Suggest(single, both, 7)?.Key);
        Assert.Null(ProgressionRule.Suggest(single, weakRight, 7));
    }

    [Fact]
    public void Timed_exercises_progress_on_hold_time()
    {
        var plank = Ex("plank"); // timed

        Assert.NotNull(ProgressionRule.Suggest(plank, [Hold(plank.Target), Hold(plank.Target), Hold(plank.Target)], 6));
        Assert.Null(ProgressionRule.Suggest(plank, [Hold(plank.Target), Hold(plank.Target - 5), Hold(plank.Target)], 6));
    }

    [Fact]
    public void Top_step_and_exercises_without_ladder_have_no_next_step()
    {
        var nordic = Ex("nordic-curl");
        Assert.Null(ProgressionRule.Suggest(nordic, Enumerable.Repeat(Reps(nordic.Target), nordic.Sets).ToList(), 5));

        var superman = Ex("superman");
        Assert.Null(superman.LadderKey);
        Assert.Null(ProgressionRule.Suggest(superman, Enumerable.Repeat(Reps(99), 5).ToList(), 5));
    }

    [Fact]
    public void Next_step_lookup_follows_the_ladder()
    {
        Assert.Equal("split-squat", ProgressionRule.NextStep(Ex("bodyweight-squat"))?.Key);
        Assert.Null(ProgressionRule.NextStep(Ex("skater-squat")));
        Assert.Equal("bodyweight-squat", ProgressionRule.PreviousStep(Ex("split-squat"))?.Key);
        Assert.Null(ProgressionRule.PreviousStep(Ex("bodyweight-squat")));
    }
}
