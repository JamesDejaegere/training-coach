namespace TrainingCoach.Infrastructure.Persistence;

/// <summary>An exercise added by the athlete; the curated library itself lives in code (<c>ExerciseLibrary</c>).</summary>
public class CustomExerciseEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string NameNl { get; set; } = "";
    public string Pattern { get; set; } = "";
    /// <summary>Comma-separated <c>Muscle</c> names.</summary>
    public string PrimaryMuscles { get; set; } = "";
    public string SecondaryMuscles { get; set; } = "";
    /// <summary>Comma-separated <c>Equipment</c> names.</summary>
    public string Equipment { get; set; } = "";
    public bool Unilateral { get; set; }
    public bool Timed { get; set; }
    public int Sets { get; set; } = 3;
    public int Target { get; set; } = 10;
    public string Instructions { get; set; } = "";
}

public enum StrengthSessionStatus
{
    InProgress,
    Finished,
}

public class StrengthSessionEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public StrengthSessionStatus Status { get; set; } = StrengthSessionStatus.InProgress;
    /// <summary>Session RPE 1–10, entered at the end (SPEC 9).</summary>
    public int? Rpe { get; set; }
    /// <summary>The strength activity created when the session is finished, so load, plan matching and pain warnings work.</summary>
    public int? ActivityId { get; set; }
    public List<StrengthEntryEntity> Entries { get; set; } = [];
}

public class StrengthEntryEntity
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    /// <summary>Key of a library exercise, or "custom-{id}".</summary>
    public string ExerciseKey { get; set; } = "";
    public int Order { get; set; }
    public List<StrengthSetEntity> Sets { get; set; } = [];
}

public class StrengthSetEntity
{
    public int Id { get; set; }
    public int EntryId { get; set; }
    public int Number { get; set; }
    public int Reps { get; set; }
    public int DurationSeconds { get; set; }
    /// <summary>"None", "Left" or "Right".</summary>
    public string Side { get; set; } = "None";
    public double? AddedLoadKg { get; set; }
    public bool Completed { get; set; }
}
