namespace TrainingCoach.Domain.Calculations;

/// <summary>Banister training impulse (SPEC 7.1).</summary>
public static class Trimp
{
    /// <summary>Sex-specific exponent of the Banister formula; 1.92 (male) is used by default.</summary>
    public const double DefaultExponent = 1.92;

    /// <summary>TRIMP for a stretch of <paramref name="minutes"/> at a constant <paramref name="averageHr"/>.</summary>
    public static double ForInterval(double minutes, double averageHr, int hrMax, int hrRest, double exponent = DefaultExponent)
    {
        if (hrMax <= hrRest)
            throw new ArgumentOutOfRangeException(nameof(hrMax), "Max HR must be higher than resting HR.");

        var reserve = (averageHr - hrRest) / (hrMax - hrRest);
        if (reserve <= 0) return 0;
        reserve = Math.Min(reserve, 1);

        return minutes * reserve * 0.64 * Math.Exp(exponent * reserve);
    }

    /// <summary>TRIMP summed over equally spaced heart-rate samples.</summary>
    public static double FromSamples(IEnumerable<double> heartRates, double sampleIntervalSeconds, int hrMax, int hrRest, double exponent = DefaultExponent)
    {
        var minutesPerSample = sampleIntervalSeconds / 60.0;
        return heartRates.Sum(hr => ForInterval(minutesPerSample, hr, hrMax, hrRest, exponent));
    }
}
