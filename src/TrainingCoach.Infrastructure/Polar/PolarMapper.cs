using System.Xml;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Planning;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Polar;

public static class PolarMapper
{
    /// <summary>AccessLink sample type of the heart-rate series.</summary>
    public const string HeartRateSampleType = "0";

    public static SportFamily ToSportFamily(string sport, string detailedSportInfo)
    {
        var s = $"{detailedSportInfo} {sport}".ToUpperInvariant();
        if (s.Contains("RUN") || s.Contains("JOG")) return SportFamily.Run;
        if (s.Contains("CYCL") || s.Contains("BIK")) return SportFamily.Ride;
        if (s.Contains("STRENGTH") || s.Contains("CIRCUIT") || s.Contains("CROSSFIT") || s.Contains("FUNCTIONAL")) return SportFamily.Strength;
        if (s.Contains("YOGA") || s.Contains("PILATES") || s.Contains("STRETCH") || s.Contains("MOBILITY")) return SportFamily.Mobility;
        return SportFamily.Other;
    }

    /// <summary>Polar sends local wall-clock time plus the UTC offset in minutes.</summary>
    public static DateTime StartUtc(PolarExercise e) =>
        DateTime.SpecifyKind(e.StartTime.AddMinutes(-e.StartTimeUtcOffset), DateTimeKind.Utc);

    public static int DurationSeconds(PolarExercise e) => (int)Math.Round(XmlConvert.ToTimeSpan(e.Duration).TotalSeconds);

    public static SessionKey KeyOf(PolarExercise e) =>
        new(ToSportFamily(e.Sport, e.DetailedSportInfo), StartUtc(e), DurationSeconds(e));

    /// <summary>
    /// Polar data wins for heart rate and load. A new row also takes the basic facts from Polar;
    /// a row that already came from Strava keeps its Strava name, sport, start, distance and Relative Effort.
    /// </summary>
    public static void Apply(PolarExercise source, ActivityEntity target, bool createdFromPolar)
    {
        target.PolarId = source.Id;
        target.PolarTrainingLoad = source.TrainingLoad;
        target.PolarCardioLoad = source.TrainingLoadPro?.CardioLoad;
        target.AverageHeartRate = source.HeartRate?.Average ?? target.AverageHeartRate;
        target.MaxHeartRate = source.HeartRate?.Maximum ?? target.MaxHeartRate;

        var hr = source.Samples?.FirstOrDefault(s => s.SampleType == HeartRateSampleType && !string.IsNullOrWhiteSpace(s.Data));
        if (hr is not null)
        {
            target.HrSamples = hr.Data;
            target.HrSampleIntervalSeconds = hr.RecordingRate;
        }

        if (!createdFromPolar) return;

        var duration = DurationSeconds(source);
        target.Name = $"{(string.IsNullOrWhiteSpace(source.DetailedSportInfo) ? source.Sport : source.DetailedSportInfo)}".Replace('_', ' ');
        target.Sport = ToSportFamily(source.Sport, source.DetailedSportInfo);
        target.StartUtc = StartUtc(source);
        target.StartLocal = source.StartTime;
        target.MovingSeconds = duration;
        target.ElapsedSeconds = duration;
        target.DistanceMeters = source.Distance ?? 0;
    }

    /// <summary>Parses the stored comma-separated samples; empty slots are skipped.</summary>
    public static IReadOnlyList<double> ParseSamples(string? data) =>
        string.IsNullOrWhiteSpace(data)
            ? []
            : data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN)
                .Where(d => !double.IsNaN(d) && d > 0)
                .ToList();
}
