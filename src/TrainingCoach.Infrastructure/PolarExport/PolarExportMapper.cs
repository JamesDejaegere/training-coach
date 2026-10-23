using System.Globalization;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Planning;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.PolarExport;

public static class PolarExportMapper
{
    /// <summary>
    /// Polar Flow sport ids, as observed in a real export and cross-checked with the same sessions on Strava:
    /// 1 running, 2 and 5 cycling, 15 strength, 111/126/127 gym and functional workouts; 3 walking, 12 tennis,
    /// 28 ice skating, 35 golf and any unknown id count as other sports.
    /// </summary>
    public static SportFamily ToSportFamily(string? sportId) => sportId switch
    {
        "1" => SportFamily.Run,
        "2" or "5" => SportFamily.Ride,
        "15" or "111" or "126" or "127" => SportFamily.Strength,
        _ => SportFamily.Other,
    };

    private static string SportLabel(string? sportId) => sportId switch
    {
        "1" => "Hardlopen",
        "2" or "5" => "Fietsen",
        "3" => "Wandelen",
        "12" => "Tennis",
        "15" or "111" or "126" or "127" => "Workout",
        _ => "Training",
    };

    /// <summary>Polar sends local wall-clock time plus an offset; without the offset the local time zone of that date is used.</summary>
    public static DateTime StartUtc(ExportSession s, TimeZoneInfo localZone)
    {
        var offset = s.OffsetMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : localZone.GetUtcOffset(s.StartLocal);
        return DateTime.SpecifyKind(s.StartLocal - offset, DateTimeKind.Utc);
    }

    public static SessionKey KeyOf(ExportSession s, TimeZoneInfo localZone) =>
        new(ToSportFamily(s.SportId), StartUtc(s, localZone), s.DurationSeconds);

    /// <summary>
    /// Polar wins for heart rate, samples and load; a new row also gets the basic facts. A row that already
    /// exists (Strava or AccessLink) keeps its own name, sport, start, distance and ids. Polar's categorical
    /// session RPE is not copied: it does not map onto our 1-10 scale.
    /// </summary>
    public static void Apply(ExportSession s, ActivityEntity target, bool createdFromPolar, TimeZoneInfo localZone)
    {
        target.PolarId ??= s.ExerciseId;
        target.PolarCardioLoad = s.CardioLoad ?? target.PolarCardioLoad;
        target.AverageHeartRate = s.HrAvg ?? target.AverageHeartRate;
        target.MaxHeartRate = s.HrMax ?? target.MaxHeartRate;

        if (s.HrSamples is { Count: > 0 } samples)
        {
            target.HrSamples = string.Join(',', samples.Select(v => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture)));
            target.HrSampleIntervalSeconds = Math.Max(1, (int)Math.Round(s.HrIntervalMillis / 1000.0));
        }

        if (!createdFromPolar) return;

        target.Name = string.IsNullOrWhiteSpace(s.Name) ? SportLabel(s.SportId) : s.Name;
        target.Sport = ToSportFamily(s.SportId);
        target.StartUtc = StartUtc(s, localZone);
        target.StartLocal = s.StartLocal;
        target.MovingSeconds = s.DurationSeconds;
        target.ElapsedSeconds = s.DurationSeconds;
        target.DistanceMeters = s.DistanceMeters;
    }
}
