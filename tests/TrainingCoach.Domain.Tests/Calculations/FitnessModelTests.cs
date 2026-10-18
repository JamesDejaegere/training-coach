using TrainingCoach.Domain.Calculations;

namespace TrainingCoach.Domain.Tests.Calculations;

public class FitnessModelTests
{
    private static readonly DateOnly Start = new(2027, 1, 1);

    [Fact]
    public void Constant_daily_load_converges_to_that_load_and_form_to_zero()
    {
        var loads = Enumerable.Range(0, 400).ToDictionary(i => Start.AddDays(i), _ => 100.0);

        var last = FitnessModel.Compute(loads, Start, Start.AddDays(399)).Last();

        Assert.Equal(100, last.Fitness, precision: 1);
        Assert.Equal(100, last.Fatigue, precision: 1);
        Assert.Equal(0, last.Form, precision: 1);
    }

    [Fact]
    public void Single_impulse_raises_fatigue_faster_and_decays_faster_than_fitness()
    {
        var loads = new Dictionary<DateOnly, double> { [Start] = 100 };

        var days = FitnessModel.Compute(loads, Start, Start.AddDays(20));

        Assert.True(days[0].Fatigue > days[0].Fitness);
        Assert.True(days[0].Form < 0);
        // after 20 days fatigue has decayed below fitness: form positive
        Assert.True(days[20].Fatigue < days[20].Fitness);
        Assert.True(days[20].Form > 0);
    }

    [Fact]
    public void First_day_follows_exponential_weighting_from_zero()
    {
        var days = FitnessModel.Compute(new Dictionary<DateOnly, double> { [Start] = 100 }, Start, Start);

        Assert.Equal(100 * (1 - Math.Exp(-1.0 / 42)), days[0].Fitness, precision: 6);
        Assert.Equal(100 * (1 - Math.Exp(-1.0 / 7)), days[0].Fatigue, precision: 6);
    }

    [Fact]
    public void Missing_days_count_as_zero_load_and_every_day_in_range_is_returned()
    {
        var days = FitnessModel.Compute(new Dictionary<DateOnly, double> { [Start] = 50 }, Start, Start.AddDays(4));

        Assert.Equal(5, days.Count);
        Assert.Equal(0, days[3].Load);
        Assert.Equal(Start.AddDays(4), days[4].Date);
    }

    [Fact]
    public void Form_is_fitness_minus_fatigue()
    {
        var loads = new Dictionary<DateOnly, double> { [Start] = 80, [Start.AddDays(2)] = 120 };

        foreach (var d in FitnessModel.Compute(loads, Start, Start.AddDays(6)))
            Assert.Equal(d.Fitness - d.Fatigue, d.Form, precision: 9);
    }

    [Fact]
    public void Acute_chronic_ratio_is_seven_day_sum_over_four_week_average_week()
    {
        // 28 days: first 21 days 100/day, last 7 days 200/day
        var loads = Enumerable.Range(0, 28).ToDictionary(i => Start.AddDays(i), i => i < 21 ? 100.0 : 200.0);
        var date = Start.AddDays(27);

        var acwr = FitnessModel.AcuteChronicRatio(loads, date);

        // acute = 1400; chronic weekly average = (3*700 + 1400) / 4 = 875
        Assert.Equal(1400.0 / 875.0, acwr!.Value, precision: 6);
    }

    [Fact]
    public void Acute_chronic_ratio_is_null_without_chronic_load()
    {
        Assert.Null(FitnessModel.AcuteChronicRatio(new Dictionary<DateOnly, double>(), Start));
    }

    [Fact]
    public void Weekly_sum_covers_monday_to_sunday()
    {
        // Wed 2027-01-06 belongs to the week of Monday 2027-01-04
        var loads = new Dictionary<DateOnly, double>
        {
            [new(2027, 1, 3)] = 10,  // Sunday of previous week
            [new(2027, 1, 4)] = 20,
            [new(2027, 1, 6)] = 30,
            [new(2027, 1, 10)] = 40, // Sunday
            [new(2027, 1, 11)] = 50, // next Monday
        };

        Assert.Equal(90, FitnessModel.WeekSum(loads, new DateOnly(2027, 1, 6)));
        Assert.Equal(new DateOnly(2027, 1, 4), FitnessModel.WeekStart(new DateOnly(2027, 1, 10)));
    }
}
