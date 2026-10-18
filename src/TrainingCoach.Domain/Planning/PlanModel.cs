namespace TrainingCoach.Domain.Planning;

public enum PhaseKind
{
    Distance,
    Time,
    /// <summary>Repeats the phases <see cref="WorkoutPhase.RepeatFromPhase"/>..<see cref="WorkoutPhase.RepeatToPhase"/>.</summary>
    Repeat,
}

/// <summary>One step of a structured workout (SPEC 6, "Planned session").</summary>
public sealed record WorkoutPhase(
    int Number,
    string Name,
    PhaseKind Kind,
    double? DistanceKm,
    TimeSpan? Duration,
    int? Repeats,
    int? RepeatFromPhase,
    int? RepeatToPhase,
    string Target,
    string Notes);

/// <summary>Named, reusable workout (a "Polar favourite" in the source plan).</summary>
public sealed record WorkoutTemplate(string Name, double TotalKm, IReadOnlyList<WorkoutPhase> Phases)
{
    /// <summary>Quality session: any phase at tempo/threshold/interval or race pace.</summary>
    public bool IsQuality => Phases.Any(p =>
        p.Target.StartsWith("Tempozone", StringComparison.OrdinalIgnoreCase) ||
        p.Target.StartsWith("Race Pace", StringComparison.OrdinalIgnoreCase));
}

public sealed record PlannedSession(
    DateOnly Date,
    SportFamily Sport,
    string Title,
    string Description,
    string? WorkoutName,
    double? PlannedDistanceKm,
    int Week,
    string Phase,
    bool IsCommute);

public sealed record TrainingPlan(
    IReadOnlyList<PlannedSession> Sessions,
    IReadOnlyDictionary<string, WorkoutTemplate> Workouts);
