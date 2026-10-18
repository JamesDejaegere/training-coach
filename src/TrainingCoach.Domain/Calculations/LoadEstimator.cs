namespace TrainingCoach.Domain.Calculations;

public enum LoadMethod
{
    TrimpSamples,
    TrimpAverageHr,
    SessionRpe,
    DurationEstimate,
}

public readonly record struct LoadContext(int HrMax, int HrRest);

public readonly record struct LoadResult(double Value, LoadMethod Method);

/// <summary>Picks the best available load source for an activity (SPEC 7.1, D12).</summary>
public static class LoadEstimator
{
    /// <summary>
    /// Scales RPE x minutes into the TRIMP range so all sports add up in one total.
    /// Calibrated so RPE 4 for 60 min (easy run) is close to the TRIMP of an hour at 135 bpm.
    /// </summary>
    public const double SessionRpeScale = 0.44;

    public static double FromSessionRpe(int rpe, double minutes)
    {
        if (rpe is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(rpe), "RPE must be between 1 and 10.");
        return rpe * minutes * SessionRpeScale;
    }

    /// <summary>Fallback when no heart rate exists: TRIMP per minute typical for the sport.</summary>
    public static double FromDuration(SportFamily sport, double minutes) => minutes * PerMinute(sport);

    private static double PerMinute(SportFamily sport) => sport switch
    {
        SportFamily.Run => 1.7,
        SportFamily.Ride => 1.2,
        SportFamily.Strength => 0.9,
        SportFamily.Mobility => 0.3,
        _ => 0.8,
    };

    public static LoadResult Estimate(
        LoadContext athlete,
        SportFamily sport,
        double minutes,
        IEnumerable<double>? hrSamples,
        double sampleIntervalSeconds,
        double? averageHr,
        int? rpe)
    {
        if (sport is SportFamily.Strength or SportFamily.Mobility && rpe is { } sessionRpe)
            return new(FromSessionRpe(sessionRpe, minutes), LoadMethod.SessionRpe);

        if (hrSamples is not null)
        {
            var samples = hrSamples.ToList();
            if (samples.Count > 0)
                return new(Trimp.FromSamples(samples, sampleIntervalSeconds, athlete.HrMax, athlete.HrRest), LoadMethod.TrimpSamples);
        }

        if (averageHr is { } hr)
            return new(Trimp.ForInterval(minutes, hr, athlete.HrMax, athlete.HrRest), LoadMethod.TrimpAverageHr);

        return new(FromDuration(sport, minutes), LoadMethod.DurationEstimate);
    }
}
