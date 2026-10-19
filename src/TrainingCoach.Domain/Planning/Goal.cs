namespace TrainingCoach.Domain.Planning;

/// <summary>The current target race (SPEC 1). Becomes a setting when more goals are supported.</summary>
public static class Goal
{
    public const string RaceName = "Antwerp 10 Miles";
    public static readonly DateOnly RaceDate = new(2027, 4, 25);
    public static readonly TimeSpan TargetTime = new(1, 25, 0);
}
