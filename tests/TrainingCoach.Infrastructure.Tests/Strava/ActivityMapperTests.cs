using TrainingCoach.Domain;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Strava;

namespace TrainingCoach.Infrastructure.Tests.Strava;

public class ActivityMapperTests
{
    [Theory]
    [InlineData("Run", SportFamily.Run)]
    [InlineData("TrailRun", SportFamily.Run)]
    [InlineData("Ride", SportFamily.Ride)]
    [InlineData("GravelRide", SportFamily.Ride)]
    [InlineData("VirtualRide", SportFamily.Ride)]
    [InlineData("WeightTraining", SportFamily.Strength)]
    [InlineData("Workout", SportFamily.Strength)]
    [InlineData("Yoga", SportFamily.Mobility)]
    [InlineData("Tennis", SportFamily.Other)]
    [InlineData("Walk", SportFamily.Other)]
    [InlineData("Golf", SportFamily.Other)]
    [InlineData("SomethingNew", SportFamily.Other)]
    public void Maps_strava_sport_type_to_sport_family(string stravaType, SportFamily expected)
    {
        Assert.Equal(expected, ActivityMapper.ToSportFamily(stravaType));
    }

    [Fact]
    public void Copies_summary_fields_and_keeps_local_wall_clock_time()
    {
        var source = new StravaActivity
        {
            Id = 99,
            Name = "Loop",
            SportType = "Run",
            StartDate = new DateTimeOffset(2026, 10, 19, 6, 30, 0, TimeSpan.Zero),
            StartDateLocal = new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.Zero), // Strava: local time labelled "Z"
            Distance = 10234.5,
            MovingTime = 3300,
            ElapsedTime = 3400,
            TotalElevationGain = 41.2,
            AverageHeartrate = 141.3,
            MaxHeartrate = 158,
            SufferScore = 57,
            Commute = true,
            StartLatlng = [51.09, 3.45],
            EndLatlng = [51.05, 3.71],
        };
        var target = new ActivityEntity();

        ActivityMapper.Apply(source, target);

        Assert.Equal(99, target.StravaId);
        Assert.Equal(SportFamily.Run, target.Sport);
        Assert.Equal(new DateTime(2026, 10, 19, 6, 30, 0, DateTimeKind.Utc), target.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 19, 8, 30, 0), target.StartLocal);
        Assert.Equal(DateOnly.FromDateTime(target.StartLocal), new DateOnly(2026, 10, 19));
        Assert.Equal(10234.5, target.DistanceMeters);
        Assert.Equal(141.3, target.AverageHeartRate);
        Assert.Equal(57, target.RelativeEffort);
        Assert.True(target.StravaCommuteFlag);
        Assert.Equal((51.09, 3.45), (target.StartLatitude!.Value, target.StartLongitude!.Value));
        Assert.Equal((51.05, 3.71), (target.EndLatitude!.Value, target.EndLongitude!.Value));
    }

    [Fact]
    public void Missing_or_empty_coordinates_and_heart_rate_stay_null()
    {
        var source = new StravaActivity { Id = 1, SportType = "Ride", StartLatlng = [], EndLatlng = null };
        var target = new ActivityEntity();

        ActivityMapper.Apply(source, target);

        Assert.Null(target.StartLatitude);
        Assert.Null(target.EndLongitude);
        Assert.Null(target.AverageHeartRate);
    }
}
