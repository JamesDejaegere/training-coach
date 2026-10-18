namespace TrainingCoach.Domain.Warnings;

public enum WarningType
{
    BuildUp,
    Recovery,
    Pain,
}

/// <summary>Suggestion shown on "Vandaag"; never changes the plan by itself (SPEC 8, D19).</summary>
public sealed record TrainingWarning(WarningType Type, DateOnly Date, string Message, string SuggestedAction);

/// <summary>All thresholds are configurable (SPEC 8).</summary>
public sealed record WarningThresholds
{
    public static WarningThresholds Default { get; } = new();

    public double MaxWeeklyRunVolumeIncrease { get; init; } = 0.10;
    public double MaxAcuteChronicRatio { get; init; } = 1.3;
    public int LowHrvConsecutiveNights { get; init; } = 3;
    public double RestingHrElevationBpm { get; init; } = 5;
    public int PainReportCount { get; init; } = 2;
    public int PainWindowDays { get; init; } = 7;
}

/// <summary>Normal range of nightly HRV; below <see cref="Low"/> counts as a low night.</summary>
public readonly record struct HrvBand(double Low, double High)
{
    /// <summary>Mean ± <paramref name="sdMultiplier"/> population standard deviations of the history.</summary>
    public static HrvBand FromHistory(IReadOnlyCollection<double> nightlyHrv, double sdMultiplier = 1.0)
    {
        if (nightlyHrv.Count == 0) throw new ArgumentException("HRV history is empty.", nameof(nightlyHrv));

        var mean = nightlyHrv.Average();
        var sd = Math.Sqrt(nightlyHrv.Average(v => (v - mean) * (v - mean)));
        return new HrvBand(mean - sdMultiplier * sd, mean + sdMultiplier * sd);
    }
}

public readonly record struct PainReport(DateOnly Date, string Location);

public static class WarningRules
{
    /// <summary>Running volume this week &gt; +10 % vs last week, or acute:chronic ratio &gt; 1.3.</summary>
    public static TrainingWarning? BuildUp(
        DateOnly date, double thisWeekRunKm, double lastWeekRunKm, double? acuteChronicRatio, WarningThresholds thresholds)
    {
        var volumeUp = lastWeekRunKm > 0 && thisWeekRunKm > lastWeekRunKm * (1 + thresholds.MaxWeeklyRunVolumeIncrease);
        var ratioHigh = acuteChronicRatio > thresholds.MaxAcuteChronicRatio;
        if (!volumeUp && !ratioHigh) return null;

        var reasons = new List<string>();
        if (volumeUp)
            reasons.Add($"loopvolume deze week ({thisWeekRunKm:0.#} km) is meer dan {thresholds.MaxWeeklyRunVolumeIncrease:P0} hoger dan vorige week ({lastWeekRunKm:0.#} km)");
        if (ratioHigh)
            reasons.Add($"acute:chronische belasting is {acuteChronicRatio:0.00} (grens {thresholds.MaxAcuteChronicRatio:0.0})");

        return new TrainingWarning(
            WarningType.BuildUp, date,
            "Opbouw te snel: " + string.Join(" en ", reasons) + ".",
            "Overweeg deze week minder te lopen of een rustige sessie te kiezen.");
    }

    /// <summary>HRV below the band for N consecutive nights, or resting HR too high – while a hard session is planned.</summary>
    /// <param name="nightlyHrv">Most recent nights, oldest first.</param>
    public static TrainingWarning? Recovery(
        DateOnly date, IReadOnlyList<double> nightlyHrv, HrvBand band, double restingHr, double baselineRestingHr,
        bool hardSessionPlanned, WarningThresholds thresholds)
    {
        if (!hardSessionPlanned) return null;

        var n = thresholds.LowHrvConsecutiveNights;
        var hrvLow = nightlyHrv.Count >= n && nightlyHrv.TakeLast(n).All(v => v < band.Low);
        var restingHigh = restingHr - baselineRestingHr > thresholds.RestingHrElevationBpm;
        if (!hrvLow && !restingHigh) return null;

        var reasons = new List<string>();
        if (hrvLow) reasons.Add($"HRV lag {n} nachten na elkaar onder je normale bereik");
        if (restingHigh) reasons.Add($"rusthartslag is {restingHr - baselineRestingHr:0} slagen boven je basiswaarde");

        return new TrainingWarning(
            WarningType.Recovery, date,
            "Onvoldoende hersteld: " + string.Join(" en ", reasons) + ".",
            "Vervang de zware sessie door een rustige training of rust.");
    }

    /// <summary>Pain reported at least N times at the same location within the window ending on <paramref name="date"/>.</summary>
    public static TrainingWarning? Pain(DateOnly date, IEnumerable<PainReport> reports, WarningThresholds thresholds)
    {
        var repeated = reports
            .Where(r => r.Date <= date && date.DayNumber - r.Date.DayNumber < thresholds.PainWindowDays)
            .GroupBy(r => r.Location.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() >= thresholds.PainReportCount);
        if (repeated is null) return null;

        return new TrainingWarning(
            WarningType.Pain, date,
            $"Pijn ({repeated.Key}) {repeated.Count()}x gemeld in de afgelopen {thresholds.PainWindowDays} dagen.",
            "Vervang de volgende loopsessie door fietsen of rust.");
    }
}
