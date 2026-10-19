namespace TrainingCoach.Infrastructure.Persistence;

/// <summary>Athlete settings (SPEC 6). Zones are effective-dated and arrive with a later iteration.</summary>
public class AthleteSettingsEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public int HrMax { get; set; } = 168;
    /// <summary>Overrides the resting HR derived from Nightly Recharge.</summary>
    public int? RestingHrOverride { get; set; }
    public double? BodyWeightKg { get; set; }

    public double? HomeLatitude { get; set; }
    public double? HomeLongitude { get; set; }
    public double? WorkLatitude { get; set; }
    public double? WorkLongitude { get; set; }
    public double CommuteRadiusMeters { get; set; } = 500;
}

/// <summary>Pain after a session (D14); feeds the pain warning (SPEC 8).</summary>
public class PainReportEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string Location { get; set; } = "";
    /// <summary>1 (mild) – 10 (severe).</summary>
    public int Severity { get; set; }
    public int? ActivityId { get; set; }
    public string Note { get; set; } = "";
}
