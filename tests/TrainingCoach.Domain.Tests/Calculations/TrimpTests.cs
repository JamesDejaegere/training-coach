using TrainingCoach.Domain.Calculations;

namespace TrainingCoach.Domain.Tests.Calculations;

public class TrimpTests
{
    private const int HrMax = 168;
    private const int HrRest = 55;

    [Fact]
    public void Banister_formula_matches_hand_calculation()
    {
        // HRr = (140 - 55) / (168 - 55) = 0.75221; 60 min * 0.75221 * 0.64 * e^(1.92 * 0.75221) = 122.4
        var trimp = Trimp.ForInterval(minutes: 60, averageHr: 140, HrMax, HrRest);

        Assert.Equal(122.4, trimp, precision: 1);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(55)]
    public void Heart_rate_at_or_below_resting_gives_zero(double hr)
    {
        Assert.Equal(0, Trimp.ForInterval(30, hr, HrMax, HrRest));
    }

    [Fact]
    public void Heart_rate_above_max_is_capped_at_max()
    {
        var atMax = Trimp.ForInterval(10, HrMax, HrMax, HrRest);
        var above = Trimp.ForInterval(10, HrMax + 20, HrMax, HrRest);

        Assert.Equal(atMax, above);
    }

    [Fact]
    public void Higher_intensity_gives_more_load_for_same_duration()
    {
        Assert.True(Trimp.ForInterval(30, 160, HrMax, HrRest) > Trimp.ForInterval(30, 130, HrMax, HrRest));
    }

    [Fact]
    public void Samples_with_constant_heart_rate_equal_the_interval_formula()
    {
        var samples = Enumerable.Repeat(140.0, 3600); // 1 Hz for one hour

        var fromSamples = Trimp.FromSamples(samples, sampleIntervalSeconds: 1, HrMax, HrRest);

        Assert.Equal(Trimp.ForInterval(60, 140, HrMax, HrRest), fromSamples, precision: 6);
    }

    [Fact]
    public void Samples_are_summed_per_sample_not_averaged()
    {
        // 30 min at 120 + 30 min at 160 must differ from 60 min at the average (140), because the curve is convex.
        var samples = Enumerable.Repeat(120.0, 1800).Concat(Enumerable.Repeat(160.0, 1800));

        var fromSamples = Trimp.FromSamples(samples, 1, HrMax, HrRest);

        Assert.True(fromSamples > Trimp.ForInterval(60, 140, HrMax, HrRest));
    }

    [Fact]
    public void Invalid_max_hr_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Trimp.ForInterval(10, 140, hrMax: 55, hrRest: 55));
    }
}
