using TrainingCoach.Domain;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Strava;

public static class ActivityMapper
{
    public static SportFamily ToSportFamily(string stravaSportType) => stravaSportType switch
    {
        "Run" or "TrailRun" or "VirtualRun" => SportFamily.Run,
        "Ride" or "GravelRide" or "MountainBikeRide" or "EBikeRide" or "EMountainBikeRide" or "VirtualRide" or "Handcycle" or "Velomobile" => SportFamily.Ride,
        "WeightTraining" or "Workout" or "Crossfit" or "HighIntensityIntervalTraining" => SportFamily.Strength,
        "Yoga" or "Pilates" => SportFamily.Mobility,
        _ => SportFamily.Other,
    };

    /// <summary>Copies the Strava summary onto <paramref name="target"/> (insert and update share this path).</summary>
    public static void Apply(StravaActivity source, ActivityEntity target)
    {
        target.StravaId = source.Id;
        target.Name = source.Name;
        target.StravaSportType = source.SportType;
        target.Sport = ToSportFamily(source.SportType);
        target.StartUtc = source.StartDate.UtcDateTime;
        target.StartLocal = source.StartDateLocal.DateTime; // Strava labels local wall-clock time with "Z"
        target.MovingSeconds = source.MovingTime;
        target.ElapsedSeconds = source.ElapsedTime;
        target.DistanceMeters = source.Distance;
        target.ElevationGainMeters = source.TotalElevationGain;
        target.AverageSpeedMps = source.AverageSpeed;
        target.MaxSpeedMps = source.MaxSpeed;
        target.AverageHeartRate = source.AverageHeartrate;
        target.MaxHeartRate = source.MaxHeartrate;
        target.AverageCadence = source.AverageCadence;
        target.AverageWatts = source.AverageWatts;
        target.Kilojoules = source.Kilojoules;
        target.RelativeEffort = source.SufferScore;
        target.StravaCommuteFlag = source.Commute;
        target.IsTrainer = source.Trainer;
        (target.StartLatitude, target.StartLongitude) = Point(source.StartLatlng);
        (target.EndLatitude, target.EndLongitude) = Point(source.EndLatlng);
    }

    private static (double? Lat, double? Lon) Point(double[]? latlng) =>
        latlng is { Length: 2 } ? (latlng[0], latlng[1]) : (null, null);
}
