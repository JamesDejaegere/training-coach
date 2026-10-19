using TrainingCoach.Domain;

namespace TrainingCoach.Infrastructure.Persistence;

/// <summary>A recorded session from Strava and/or Polar (SPEC 6, "Activity").</summary>
public class ActivityEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public long? StravaId { get; set; }
    public string? PolarId { get; set; }

    public string Name { get; set; } = "";
    public SportFamily Sport { get; set; }
    public string StravaSportType { get; set; } = "";

    public DateTime StartUtc { get; set; }
    /// <summary>Local wall-clock start; its date is the training day used for plan matching.</summary>
    public DateTime StartLocal { get; set; }
    public int MovingSeconds { get; set; }
    public int ElapsedSeconds { get; set; }
    public double DistanceMeters { get; set; }
    public double ElevationGainMeters { get; set; }
    public double AverageSpeedMps { get; set; }
    public double MaxSpeedMps { get; set; }

    public double? AverageHeartRate { get; set; }
    public double? MaxHeartRate { get; set; }
    public double? AverageCadence { get; set; }
    public double? AverageWatts { get; set; }
    public double? Kilojoules { get; set; }

    /// <summary>Strava "Relative Effort"; shown for comparison only (SPEC 7.1).</summary>
    public double? RelativeEffort { get; set; }

    /// <summary>Polar training load / cardio load; shown for comparison only (SPEC 7.1).</summary>
    public double? PolarTrainingLoad { get; set; }
    public double? PolarCardioLoad { get; set; }

    /// <summary>Heart-rate samples (comma-separated bpm, empty slot = no value) for TRIMP (SPEC 7.1).</summary>
    public string? HrSamples { get; set; }
    public int? HrSampleIntervalSeconds { get; set; }

    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public double? EndLatitude { get; set; }
    public double? EndLongitude { get; set; }

    public bool StravaCommuteFlag { get; set; }
    public bool IsTrainer { get; set; }
    /// <summary>Commute according to our own home/work detection (SPEC 7.4).</summary>
    public bool IsCommute { get; set; }

    /// <summary>Session RPE 1–10, entered by the athlete after the session.</summary>
    public int? Rpe { get; set; }
}

/// <summary>One night from Polar: Nightly Recharge and sleep (SPEC 6, "Recovery day").</summary>
public class RecoveryDayEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    /// <summary>The morning the night ends on.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Average nightly HRV (RMSSD, ms).</summary>
    public double? Hrv { get; set; }
    /// <summary>Average heart rate during the night; used as resting heart rate (SPEC 6).</summary>
    public double? RestingHeartRate { get; set; }
    public double? BreathingRate { get; set; }
    public double? AnsCharge { get; set; }
    public int? AnsChargeStatus { get; set; }
    public int? NightlyRechargeStatus { get; set; }

    public double? SleepScore { get; set; }
    public int? SleepSeconds { get; set; }
    public int? DeepSleepSeconds { get; set; }
    public int? RemSleepSeconds { get; set; }
}

/// <summary>OAuth connection to an external provider; tokens are stored encrypted.</summary>
public class ProviderConnectionEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = "";
    public long? ExternalAthleteId { get; set; }

    public string AccessTokenProtected { get; set; } = "";
    public string RefreshTokenProtected { get; set; } = "";
    public DateTime AccessTokenExpiresUtc { get; set; }

    public DateTime ConnectedUtc { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    /// <summary>Start of the newest activity seen in the last complete sync; basis for incremental sync.</summary>
    public DateTime? SyncCursorUtc { get; set; }
}

public static class Providers
{
    public const string Strava = "strava";
    public const string Polar = "polar";
}
