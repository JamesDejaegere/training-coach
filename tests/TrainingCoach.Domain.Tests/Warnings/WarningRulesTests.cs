using TrainingCoach.Domain.Warnings;

namespace TrainingCoach.Domain.Tests.Warnings;

public class WarningRulesTests
{
    private static readonly DateOnly Today = new(2027, 2, 10);
    private static readonly WarningThresholds T = WarningThresholds.Default;

    // ---- Build-up ----

    [Fact]
    public void Running_volume_more_than_10_percent_above_last_week_warns()
    {
        var w = WarningRules.BuildUp(Today, thisWeekRunKm: 33.1, lastWeekRunKm: 30, acuteChronicRatio: 1.0, T);

        Assert.NotNull(w);
        Assert.Equal(WarningType.BuildUp, w.Type);
        Assert.Equal(Today, w.Date);
    }

    [Fact]
    public void Running_volume_exactly_10_percent_above_does_not_warn()
    {
        Assert.Null(WarningRules.BuildUp(Today, 33, 30, 1.0, T));
    }

    [Fact]
    public void Acute_chronic_ratio_above_1_3_warns_even_when_volume_is_stable()
    {
        Assert.NotNull(WarningRules.BuildUp(Today, 30, 30, 1.31, T));
        Assert.Null(WarningRules.BuildUp(Today, 30, 30, 1.30, T));
    }

    [Fact]
    public void No_previous_week_and_no_ratio_gives_no_warning()
    {
        Assert.Null(WarningRules.BuildUp(Today, 20, 0, null, T));
    }

    [Fact]
    public void Thresholds_are_configurable()
    {
        var strict = T with { MaxWeeklyRunVolumeIncrease = 0.05 };

        Assert.NotNull(WarningRules.BuildUp(Today, 31.6, 30, 1.0, strict));
    }

    // ---- Recovery ----

    private static readonly HrvBand Band = new(Low: 50, High: 80);

    [Fact]
    public void Three_consecutive_nights_below_hrv_band_with_hard_session_planned_warns()
    {
        var w = WarningRules.Recovery(Today, [62, 48, 47, 45], Band, restingHr: 52, baselineRestingHr: 52, hardSessionPlanned: true, T);

        Assert.NotNull(w);
        Assert.Equal(WarningType.Recovery, w.Type);
    }

    [Fact]
    public void Two_low_nights_are_not_enough()
    {
        Assert.Null(WarningRules.Recovery(Today, [62, 60, 47, 45], Band, 52, 52, true, T));
    }

    [Fact]
    public void Low_hrv_without_hard_session_planned_does_not_warn()
    {
        Assert.Null(WarningRules.Recovery(Today, [48, 47, 45], Band, 52, 52, hardSessionPlanned: false, T));
    }

    [Fact]
    public void Resting_hr_more_than_5_above_baseline_warns()
    {
        Assert.NotNull(WarningRules.Recovery(Today, [60, 60, 60], Band, restingHr: 58, baselineRestingHr: 52, true, T));
        Assert.Null(WarningRules.Recovery(Today, [60, 60, 60], Band, restingHr: 57, baselineRestingHr: 52, true, T));
    }

    [Fact]
    public void Hrv_band_is_mean_plus_minus_one_standard_deviation_of_history()
    {
        // mean 60, population sd 10
        var band = HrvBand.FromHistory([50, 70, 50, 70, 60, 60]);

        Assert.Equal(60 - Math.Sqrt(200.0 / 3), band.Low, precision: 6);
        Assert.Equal(60 + Math.Sqrt(200.0 / 3), band.High, precision: 6);
    }

    // ---- Pain ----

    [Fact]
    public void Pain_twice_at_same_location_within_7_days_warns_with_suggestion()
    {
        var reports = new[] { new PainReport(Today.AddDays(-6), "kuit"), new PainReport(Today, "kuit") };

        var w = WarningRules.Pain(Today, reports, T);

        Assert.NotNull(w);
        Assert.Equal(WarningType.Pain, w.Type);
        Assert.Contains("kuit", w.Message);
    }

    [Fact]
    public void Pain_at_different_locations_or_outside_window_does_not_warn()
    {
        Assert.Null(WarningRules.Pain(Today, [new(Today, "kuit"), new(Today, "hamstring")], T));
        Assert.Null(WarningRules.Pain(Today, [new(Today.AddDays(-7), "kuit"), new(Today, "kuit")], T));
    }

    [Fact]
    public void Pain_location_comparison_ignores_case()
    {
        Assert.NotNull(WarningRules.Pain(Today, [new(Today.AddDays(-1), "Kuit"), new(Today, "kuit")], T));
    }
}
