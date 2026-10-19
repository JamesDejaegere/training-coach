namespace TrainingCoach.Domain.Strength;

/// <summary>Suggests the next harder variant on a ladder (SPEC 9: top of the rep range at RPE ≤ 7).</summary>
public static class ProgressionRule
{
    public const int MaxRpeForProgression = 7;

    public static ExerciseDefinition? NextStep(ExerciseDefinition exercise) => Step(exercise, +1);

    public static ExerciseDefinition? PreviousStep(ExerciseDefinition exercise) => Step(exercise, -1);

    private static ExerciseDefinition? Step(ExerciseDefinition exercise, int delta)
    {
        if (exercise.LadderKey is null || exercise.LadderStep is not { } step) return null;

        var steps = ExerciseLibrary.Ladder(exercise.LadderKey).Steps;
        var target = step + delta;
        return target >= 0 && target < steps.Count ? ExerciseLibrary.ByKey[steps[target]] : null;
    }

    /// <summary>
    /// The next ladder step when the prescribed number of sets all reached the target (reps, or seconds for timed
    /// exercises; per side for one-sided exercises) and the session felt easy enough; otherwise null.
    /// </summary>
    public static ExerciseDefinition? Suggest(ExerciseDefinition current, IReadOnlyList<SetResult> sets, int? sessionRpe)
    {
        if (sessionRpe is not { } rpe || rpe > MaxRpeForProgression) return null;
        if (NextStep(current) is not { } next) return null;

        bool Reached(SetResult s) => s.Completed && (current.Timed ? s.DurationSeconds : s.Reps) >= current.Target;

        var reached = current.Unilateral
            ? new[] { SetSide.Left, SetSide.Right }.All(side => sets.Count(s => s.Side == side && Reached(s)) >= current.Sets)
            : sets.Count(Reached) >= current.Sets;

        return reached ? next : null;
    }
}
