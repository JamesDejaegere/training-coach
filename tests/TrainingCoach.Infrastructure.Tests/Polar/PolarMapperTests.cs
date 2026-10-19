using TrainingCoach.Domain;
using TrainingCoach.Domain.Planning;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Polar;

namespace TrainingCoach.Infrastructure.Tests.Polar;

public class PolarMapperTests
{
    [Theory]
    [InlineData("RUNNING", "RUNNING", SportFamily.Run)]
    [InlineData("RUNNING", "TRAIL_RUNNING", SportFamily.Run)]
    [InlineData("OTHER", "TREADMILL_RUNNING", SportFamily.Run)]
    [InlineData("CYCLING", "ROAD_BIKING", SportFamily.Ride)]
    [InlineData("CYCLING", "INDOOR_CYCLING", SportFamily.Ride)]
    [InlineData("STRENGTH_TRAINING", "STRENGTH_TRAINING", SportFamily.Strength)]
    [InlineData("OTHER", "YOGA", SportFamily.Mobility)]
    [InlineData("OTHER", "TENNIS", SportFamily.Other)]
    [InlineData("OTHER", "", SportFamily.Other)]
    public void Maps_polar_sports_to_sport_families(string sport, string detailed, SportFamily expected)
    {
        Assert.Equal(expected, PolarMapper.ToSportFamily(sport, detailed));
    }

    [Fact]
    public void Start_is_local_wall_clock_minus_offset_in_utc()
    {
        var e = PolarTestData.Exercise("x", new DateTime(2026, 10, 19, 8, 30, 0), offset: 120);

        Assert.Equal(new DateTime(2026, 10, 19, 6, 30, 0, DateTimeKind.Utc), PolarMapper.StartUtc(e));
    }

    [Theory]
    [InlineData("PT55M30S", 3330)]
    [InlineData("PT2H44M", 9840)]
    [InlineData("PT1H", 3600)]
    [InlineData("PT45.6S", 46)]
    public void Parses_iso_8601_durations(string iso, int seconds)
    {
        var e = PolarTestData.Exercise("x", DateTime.Today, duration: iso);

        Assert.Equal(seconds, PolarMapper.DurationSeconds(e));
    }

    [Fact]
    public void New_polar_row_takes_basic_facts_heart_rate_samples_and_loads()
    {
        var e = PolarTestData.Exercise("AB12", new DateTime(2026, 10, 19, 8, 30, 0), duration: "PT55M30S", samples: "98,102,,110");
        var row = new ActivityEntity();

        PolarMapper.Apply(e, row, createdFromPolar: true);

        Assert.Equal("AB12", row.PolarId);
        Assert.Equal(SportFamily.Run, row.Sport);
        Assert.Equal(new DateTime(2026, 10, 19, 6, 30, 0, DateTimeKind.Utc), row.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 19, 8, 30, 0), row.StartLocal);
        Assert.Equal(3330, row.ElapsedSeconds);
        Assert.Equal(10000, row.DistanceMeters);
        Assert.Equal((140, 170), (row.AverageHeartRate, row.MaxHeartRate));
        Assert.Equal((90, 80), (row.PolarTrainingLoad, row.PolarCardioLoad));
        Assert.Equal("98,102,,110", row.HrSamples);
        Assert.Equal(5, row.HrSampleIntervalSeconds);
    }

    [Fact]
    public void Merging_into_a_strava_row_keeps_strava_facts_but_takes_polar_heart_rate()
    {
        var row = new ActivityEntity
        {
            StravaId = 1,
            Name = "Ochtendloop",
            Sport = SportFamily.Run,
            StartUtc = new DateTime(2026, 10, 19, 6, 31, 0, DateTimeKind.Utc),
            DistanceMeters = 10050,
            AverageHeartRate = 133,
            RelativeEffort = 57,
        };
        var e = PolarTestData.Exercise("AB12", new DateTime(2026, 10, 19, 8, 30, 0));

        PolarMapper.Apply(e, row, createdFromPolar: false);

        Assert.Equal(("Ochtendloop", 1L, 10050.0, 57.0), (row.Name, row.StravaId, row.DistanceMeters, row.RelativeEffort));
        Assert.Equal(new DateTime(2026, 10, 19, 6, 31, 0, DateTimeKind.Utc), row.StartUtc);
        Assert.Equal("AB12", row.PolarId);
        Assert.Equal(140, row.AverageHeartRate);
        Assert.NotNull(row.HrSamples);
    }

    [Fact]
    public void Only_the_heart_rate_series_is_stored()
    {
        var e = PolarTestData.Exercise("x", DateTime.Today) with
        {
            Samples = [new PolarSampleSet { RecordingRate = 1, SampleType = "1", Data = "9,9" }],
        };
        var row = new ActivityEntity();

        PolarMapper.Apply(e, row, createdFromPolar: true);

        Assert.Null(row.HrSamples);
    }

    [Fact]
    public void Samples_are_parsed_skipping_empty_slots()
    {
        Assert.Equal([98.0, 102.0, 110.0], PolarMapper.ParseSamples("98,102,,110"));
        Assert.Empty(PolarMapper.ParseSamples(null));
        Assert.Empty(PolarMapper.ParseSamples(""));
    }

    [Fact]
    public void Session_key_uses_family_utc_start_and_duration()
    {
        var key = PolarMapper.KeyOf(PolarTestData.Exercise("x", new DateTime(2026, 10, 19, 8, 30, 0), "CYCLING", "PT1H"));

        Assert.Equal(new SessionKey(SportFamily.Ride, new DateTime(2026, 10, 19, 6, 30, 0, DateTimeKind.Utc), 3600), key);
    }
}
