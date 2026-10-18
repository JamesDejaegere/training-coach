namespace TrainingCoach.Domain.Calculations;

public readonly record struct DailyFitness(DateOnly Date, double Load, double Fitness, double Fatigue)
{
    public double Form => Fitness - Fatigue;
}

/// <summary>Fitness / fatigue / form and acute:chronic ratio (SPEC 7.2).</summary>
public static class FitnessModel
{
    public const int FitnessDays = 42;
    public const int FatigueDays = 7;

    /// <summary>Exponentially weighted averages starting from zero; days without load count as 0.</summary>
    public static IReadOnlyList<DailyFitness> Compute(
        IReadOnlyDictionary<DateOnly, double> dailyLoad,
        DateOnly from,
        DateOnly to,
        int fitnessDays = FitnessDays,
        int fatigueDays = FatigueDays)
    {
        var fitnessAlpha = 1 - Math.Exp(-1.0 / fitnessDays);
        var fatigueAlpha = 1 - Math.Exp(-1.0 / fatigueDays);

        var result = new List<DailyFitness>();
        double fitness = 0, fatigue = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var load = dailyLoad.GetValueOrDefault(date);
            fitness += (load - fitness) * fitnessAlpha;
            fatigue += (load - fatigue) * fatigueAlpha;
            result.Add(new DailyFitness(date, load, fitness, fatigue));
        }
        return result;
    }

    /// <summary>7-day load divided by the average weekly load of the last 28 days; null when there is no chronic load.</summary>
    public static double? AcuteChronicRatio(IReadOnlyDictionary<DateOnly, double> dailyLoad, DateOnly date)
    {
        var acute = SumRange(dailyLoad, date.AddDays(-6), date);
        var chronicWeekly = SumRange(dailyLoad, date.AddDays(-27), date) / 4.0;
        return chronicWeekly > 0 ? acute / chronicWeekly : null;
    }

    public static DateOnly WeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Sum over the Monday–Sunday week containing <paramref name="date"/>.</summary>
    public static double WeekSum(IReadOnlyDictionary<DateOnly, double> values, DateOnly date)
    {
        var start = WeekStart(date);
        return SumRange(values, start, start.AddDays(6));
    }

    public static double SumRange(IReadOnlyDictionary<DateOnly, double> values, DateOnly from, DateOnly to)
    {
        double sum = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            sum += values.GetValueOrDefault(d);
        return sum;
    }
}
