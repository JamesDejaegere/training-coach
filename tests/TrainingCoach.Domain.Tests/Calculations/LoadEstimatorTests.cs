using TrainingCoach.Domain;
using TrainingCoach.Domain.Calculations;

namespace TrainingCoach.Domain.Tests.Calculations;

public class LoadEstimatorTests
{
    [Fact]
    public void Session_rpe_is_rpe_times_minutes_scaled_to_trimp_range()
    {
        var load = LoadEstimator.FromSessionRpe(rpe: 6, minutes: 30);

        Assert.Equal(6 * 30 * LoadEstimator.SessionRpeScale, load, precision: 6);
    }

    [Fact]
    public void Easy_run_rpe_lands_in_same_range_as_trimp_of_an_easy_run()
    {
        // Easy 60 min run: avg HR 135 (Z2) -> TRIMP; RPE 4 session load must be within 25% of it.
        var trimp = Trimp.ForInterval(60, 135, 168, 55);
        var srpe = LoadEstimator.FromSessionRpe(4, 60);

        Assert.InRange(srpe / trimp, 0.75, 1.25);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Rpe_outside_1_to_10_throws(int rpe)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LoadEstimator.FromSessionRpe(rpe, 30));
    }

    [Fact]
    public void Duration_estimate_scales_linearly_with_minutes_and_differs_per_sport()
    {
        var run30 = LoadEstimator.FromDuration(SportFamily.Run, 30);
        var run60 = LoadEstimator.FromDuration(SportFamily.Run, 60);
        var other60 = LoadEstimator.FromDuration(SportFamily.Other, 60);

        Assert.Equal(run30 * 2, run60, precision: 6);
        Assert.True(run60 > other60);
    }

    [Fact]
    public void Best_source_prefers_hr_samples_then_average_hr_then_rpe_for_strength_then_duration()
    {
        var athlete = new LoadContext(HrMax: 168, HrRest: 55);

        var withSamples = LoadEstimator.Estimate(athlete, SportFamily.Run, minutes: 60,
            hrSamples: Enumerable.Repeat(140.0, 3600), sampleIntervalSeconds: 1, averageHr: 100, rpe: 5);
        var withAvg = LoadEstimator.Estimate(athlete, SportFamily.Run, 60, null, 1, averageHr: 140, rpe: 5);
        var strength = LoadEstimator.Estimate(athlete, SportFamily.Strength, 30, null, 1, averageHr: 120, rpe: 7);
        var noData = LoadEstimator.Estimate(athlete, SportFamily.Ride, 60, null, 1, null, null);

        Assert.Equal(LoadMethod.TrimpSamples, withSamples.Method);
        Assert.Equal(122.4, withSamples.Value, precision: 1);
        Assert.Equal(LoadMethod.TrimpAverageHr, withAvg.Method);
        Assert.Equal(LoadMethod.SessionRpe, strength.Method);
        Assert.Equal(LoadEstimator.FromSessionRpe(7, 30), strength.Value, precision: 6);
        Assert.Equal(LoadMethod.DurationEstimate, noData.Method);
    }
}
