namespace TrainingCoach.Domain.Strength;

/// <summary>Muscle load in "set equivalents" (SPEC 7.3): one completed set = 1.0 on primary and 0.5 on secondary muscles.</summary>
public static class MuscleLoad
{
    public const double PrimaryWeight = 1.0;
    public const double SecondaryWeight = 0.5;

    /// <summary>Set equivalents per unit of training load: a run of load 100 counts like 4 sets on the leg muscles.</summary>
    public const double EnduranceSetsPerLoad = 0.04;

    /// <summary>Set equivalents that colour a muscle fully on the map.</summary>
    public const double DefaultReference = 12;

    /// <summary>Every completed set counts (each side of a one-sided exercise separately).</summary>
    public static Dictionary<Muscle, double> FromSets(IEnumerable<(ExerciseDefinition Exercise, SetResult Set)> sets)
    {
        var load = new Dictionary<Muscle, double>();
        foreach (var (exercise, set) in sets)
        {
            if (!set.Completed) continue;
            foreach (var m in exercise.Primary) Add(load, m, PrimaryWeight);
            foreach (var m in exercise.Secondary) Add(load, m, SecondaryWeight);
        }
        return load;
    }

    /// <summary>Running and cycling load the leg muscles in proportion to their training load, so leg fatigue after a long run is visible.</summary>
    public static Dictionary<Muscle, double> FromEndurance(SportFamily sport, double trainingLoad)
    {
        var weights = sport switch
        {
            SportFamily.Run => new Dictionary<Muscle, double>
            {
                [Muscle.Calves] = 1.0, [Muscle.Hamstrings] = 1.0, [Muscle.Quadriceps] = 1.0, [Muscle.Glutes] = 1.0, [Muscle.HipFlexors] = 0.5,
            },
            SportFamily.Ride => new Dictionary<Muscle, double>
            {
                [Muscle.Quadriceps] = 1.0, [Muscle.Glutes] = 0.8, [Muscle.Hamstrings] = 0.5, [Muscle.Calves] = 0.5,
            },
            _ => [],
        };

        return weights.ToDictionary(w => w.Key, w => w.Value * trainingLoad * EnduranceSetsPerLoad);
    }

    public static Dictionary<Muscle, double> Merge(params IReadOnlyDictionary<Muscle, double>[] loads)
    {
        var merged = new Dictionary<Muscle, double>();
        foreach (var load in loads)
            foreach (var (muscle, value) in load)
                Add(merged, muscle, value);
        return merged;
    }

    /// <summary>0..1 per muscle (all muscles present) relative to <paramref name="reference"/>, for colouring the map.</summary>
    public static Dictionary<Muscle, double> Intensity(IReadOnlyDictionary<Muscle, double> load, double reference = DefaultReference) =>
        Enum.GetValues<Muscle>().ToDictionary(m => m, m => load.TryGetValue(m, out var v) ? Math.Clamp(v / reference, 0, 1) : 0.0);

    private static void Add(Dictionary<Muscle, double> load, Muscle muscle, double value) =>
        load[muscle] = load.GetValueOrDefault(muscle) + value;
}
