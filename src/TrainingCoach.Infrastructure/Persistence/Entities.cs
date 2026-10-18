using TrainingCoach.Domain;
using TrainingCoach.Domain.Planning;

namespace TrainingCoach.Infrastructure.Persistence;

/// <summary>Single-user for now (D1), but every aggregate carries its owner so multi-user stays possible.</summary>
public static class UserIds
{
    public const int Owner = 1;
}

public class PlannedSessionEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>Date in the original plan; set when the session is moved.</summary>
    public DateOnly? OriginalDate { get; set; }
    public SportFamily Sport { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? WorkoutName { get; set; }
    public double? PlannedDistanceKm { get; set; }
    public int Week { get; set; }
    public string Phase { get; set; } = "";
    public bool IsCommute { get; set; }
    public PlannedStatus Status { get; set; } = PlannedStatus.Planned;
}

public class WorkoutTemplateEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public double TotalKm { get; set; }
    public List<WorkoutPhaseEntity> Phases { get; set; } = [];
}

public class WorkoutPhaseEntity
{
    public int Id { get; set; }
    public int WorkoutTemplateEntityId { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public PhaseKind Kind { get; set; }
    public double? DistanceKm { get; set; }
    public TimeSpan? Duration { get; set; }
    public int? Repeats { get; set; }
    public int? RepeatFromPhase { get; set; }
    public int? RepeatToPhase { get; set; }
    public string Target { get; set; } = "";
    public string Notes { get; set; } = "";
}
