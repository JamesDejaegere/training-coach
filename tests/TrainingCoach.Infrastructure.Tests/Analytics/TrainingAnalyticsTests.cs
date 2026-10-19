using Microsoft.Extensions.Time.Testing;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Calculations;
using TrainingCoach.Domain.Planning;
using TrainingCoach.Domain.Warnings;
using TrainingCoach.Infrastructure.Analytics;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Tests.Persistence;

namespace TrainingCoach.Infrastructure.Tests.Analytics;

public sealed class TrainingAnalyticsTests : IDisposable
{
    // Wednesday
    private static readonly DateOnly Today = new(2027, 2, 10);
    private static readonly DateOnly Monday = new(2027, 2, 8);

    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2027, 2, 10, 9, 0, 0, TimeSpan.FromHours(1)));

    public void Dispose() => _db.Dispose();

    private (TrainingAnalytics Analytics, AppDbContext Db) Create()
    {
        var db = _db.CreateContext();
        return (new TrainingAnalytics(db, new PlanStore(db), _time), db);
    }

    private static ActivityEntity Activity(DateOnly date, SportFamily sport, double km = 0, int minutes = 60, double? avgHr = null,
        string? samples = null, int? rpe = null, bool stravaCommute = false, (double, double)? from = null, (double, double)? to = null) => new()
    {
        UserId = 1,
        Name = "a",
        Sport = sport,
        StartLocal = date.ToDateTime(new TimeOnly(8, 0)),
        StartUtc = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(7, 0)), DateTimeKind.Utc),
        MovingSeconds = minutes * 60,
        ElapsedSeconds = minutes * 60,
        DistanceMeters = km * 1000,
        AverageHeartRate = avgHr,
        HrSamples = samples,
        HrSampleIntervalSeconds = samples is null ? null : 60,
        Rpe = rpe,
        StravaCommuteFlag = stravaCommute,
        StartLatitude = from?.Item1, StartLongitude = from?.Item2,
        EndLatitude = to?.Item1, EndLongitude = to?.Item2,
    };

    // ---- profile ----

    [Fact]
    public async Task Profile_defaults_to_hrmax_168_and_resting_55()
    {
        var (analytics, _) = Create();

        var profile = await analytics.ProfileAsync();

        Assert.Equal((168, 55), (profile.HrMax, profile.HrRest));
        Assert.Null(profile.Home);
    }

    [Fact]
    public async Task Resting_hr_is_the_median_of_recent_nightly_recharge_values_unless_overridden()
    {
        var (analytics, db) = Create();
        foreach (var (offset, hr) in new[] { (1, 50.0), (2, 52.0), (3, 60.0), (60, 80.0) }) // the 60-day-old night is outside the window
            db.RecoveryDays.Add(new RecoveryDayEntity { UserId = 1, Date = Today.AddDays(-offset), RestingHeartRate = hr });
        await db.SaveChangesAsync();

        Assert.Equal(52, (await analytics.ProfileAsync()).HrRest);

        db.AthleteSettings.Add(new AthleteSettingsEntity { UserId = 1, HrMax = 170, RestingHrOverride = 48 });
        await db.SaveChangesAsync();
        var overridden = await analytics.ProfileAsync();
        Assert.Equal((170, 48), (overridden.HrMax, overridden.HrRest));
    }

    // ---- commute detection ----

    private static readonly AthleteProfile WithLocations = new(168, 55, new GeoPoint(51.0902, 3.4529), new GeoPoint(51.0543, 3.7174), 500);

    [Fact]
    public void Ride_between_home_and_work_is_a_commute_even_without_strava_flag()
    {
        var ride = Activity(Today, SportFamily.Ride, 30, from: (51.0903, 3.4530), to: (51.0544, 3.7175));

        Assert.True(TrainingAnalytics.IsCommute(ride, WithLocations));
    }

    [Fact]
    public void Ride_elsewhere_is_not_a_commute_even_if_strava_flagged_when_locations_are_known()
    {
        var ride = Activity(Today, SportFamily.Ride, 30, stravaCommute: true, from: (51.2, 3.0), to: (51.3, 3.1));

        Assert.False(TrainingAnalytics.IsCommute(ride, WithLocations));
    }

    [Fact]
    public void Without_locations_the_strava_flag_decides_and_only_rides_can_be_commutes()
    {
        var noLocations = new AthleteProfile(168, 55, null, null, 500);

        Assert.True(TrainingAnalytics.IsCommute(Activity(Today, SportFamily.Ride, stravaCommute: true), noLocations));
        Assert.False(TrainingAnalytics.IsCommute(Activity(Today, SportFamily.Run, stravaCommute: true), noLocations));
    }

    // ---- load ----

    [Fact]
    public void Load_uses_samples_then_average_hr_then_rpe_for_strength_then_duration()
    {
        var profile = new AthleteProfile(168, 55, null, null, 500);

        Assert.Equal(LoadMethod.TrimpSamples, TrainingAnalytics.LoadOf(Activity(Today, SportFamily.Run, avgHr: 140, samples: "130,135,140"), profile).Method);
        Assert.Equal(LoadMethod.TrimpAverageHr, TrainingAnalytics.LoadOf(Activity(Today, SportFamily.Run, avgHr: 140), profile).Method);
        var strength = TrainingAnalytics.LoadOf(Activity(Today, SportFamily.Strength, minutes: 30, rpe: 6), profile);
        Assert.Equal((LoadMethod.SessionRpe, LoadEstimator.FromSessionRpe(6, 30)), (strength.Method, strength.Value));
        Assert.Equal(LoadMethod.DurationEstimate, TrainingAnalytics.LoadOf(Activity(Today, SportFamily.Ride), profile).Method);
    }

    [Fact]
    public void Sample_trimp_ignores_empty_slots_and_uses_the_recording_interval()
    {
        var profile = new AthleteProfile(168, 55, null, null, 500);
        var withGap = Activity(Today, SportFamily.Run, samples: "140,,140");

        var load = TrainingAnalytics.LoadOf(withGap, profile);

        Assert.Equal(Trimp.ForInterval(2, 140, 168, 55), load.Value, precision: 6); // 2 valid samples x 60 s
    }

    [Fact]
    public async Task Daily_load_sums_all_sports_per_local_day()
    {
        var (analytics, db) = Create();
        db.Activities.AddRange(
            Activity(Today, SportFamily.Run, avgHr: 140),
            Activity(Today, SportFamily.Strength, minutes: 30, rpe: 6),
            Activity(Today.AddDays(-1), SportFamily.Ride));
        await db.SaveChangesAsync();

        var loads = await analytics.DailyLoadsAsync(Today.AddDays(-1), Today);

        var expectedToday = Trimp.ForInterval(60, 140, 168, 55) + LoadEstimator.FromSessionRpe(6, 30);
        Assert.Equal(expectedToday, loads[Today], precision: 6);
        Assert.Equal(LoadEstimator.FromDuration(SportFamily.Ride, 60), loads[Today.AddDays(-1)], precision: 6);
    }

    [Fact]
    public async Task Fitness_is_computed_from_the_first_activity_so_history_before_the_range_counts()
    {
        var (analytics, db) = Create();
        db.Activities.Add(Activity(Today.AddDays(-30), SportFamily.Run, avgHr: 150));
        await db.SaveChangesAsync();

        var days = await analytics.FitnessAsync(Today.AddDays(-2), Today);

        Assert.Equal(3, days.Count);
        Assert.True(days[0].Fitness > 0); // the run 28 days earlier still counts
        Assert.True(days[0].Fatigue < days[0].Fitness);
    }

    [Fact]
    public async Task Fitness_without_any_activity_is_zero()
    {
        var (analytics, _) = Create();

        var days = await analytics.FitnessAsync(Today.AddDays(-1), Today);

        Assert.All(days, d => Assert.Equal(0, d.Fitness));
    }

    // ---- weekly volume ----

    [Fact]
    public async Task Weekly_volume_groups_monday_to_sunday_per_sport()
    {
        var (analytics, db) = Create();
        db.Activities.AddRange(
            Activity(Monday, SportFamily.Run, km: 5, minutes: 30),
            Activity(Monday.AddDays(6), SportFamily.Run, km: 10, minutes: 60), // Sunday
            Activity(Monday.AddDays(7), SportFamily.Run, km: 7, minutes: 40),  // next Monday
            Activity(Monday.AddDays(1), SportFamily.Ride, km: 60, minutes: 120),
            Activity(Monday.AddDays(3), SportFamily.Strength, minutes: 25, rpe: 5));
        await db.SaveChangesAsync();

        var weeks = await analytics.WeeklyVolumesAsync(Monday, Monday.AddDays(7));

        var first = weeks[0];
        Assert.Equal(Monday, first.WeekStart);
        Assert.Equal((15.0, 60.0, 25), (first.RunKm, first.RideKm, first.StrengthMinutes));
        Assert.Equal((30 + 60 + 120 + 25) / 60.0, first.TotalHours, precision: 6);
        Assert.Equal(7, weeks[1].RunKm);
    }

    // ---- plan view ----

    private static TrainingPlan Plan(params PlannedSession[] sessions) => new(sessions, new Dictionary<string, WorkoutTemplate>());

    private static PlannedSession Planned(DateOnly date, SportFamily sport, double? km = null, bool commute = false, string? workout = null) =>
        new(date, sport, "t", "d", workout, km, 1, "1 · Basis", commute);

    [Fact]
    public async Task Plan_view_links_activities_to_sessions_and_reports_extra_and_commutes()
    {
        var (analytics, db) = Create();
        await new PlanStore(db).ImportAsync(Plan(
            Planned(Monday, SportFamily.Run, 5),
            Planned(Monday.AddDays(1), SportFamily.Ride, 60, commute: true),
            Planned(Monday.AddDays(2), SportFamily.Run, 8)));
        db.AthleteSettings.Add(new AthleteSettingsEntity { UserId = 1, HomeLatitude = 51.0902, HomeLongitude = 3.4529, WorkLatitude = 51.0543, WorkLongitude = 3.7174 });
        db.Activities.AddRange(
            Activity(Monday, SportFamily.Run, 5.2),
            Activity(Monday.AddDays(1), SportFamily.Ride, 30, from: (51.0902, 3.4529), to: (51.0543, 3.7174)),
            Activity(Monday.AddDays(1), SportFamily.Ride, 30, from: (51.0543, 3.7174), to: (51.0902, 3.4529)),
            Activity(Monday.AddDays(4), SportFamily.Run, 4)); // not planned
        await db.SaveChangesAsync();

        var view = await analytics.PlanViewAsync(Monday, Monday.AddDays(6));

        Assert.Equal(3, view.Items.Count);
        Assert.True(view.Items[0].IsDone);
        Assert.Equal(2, view.Items[1].Activities.Count); // both commute legs on the planned commute
        Assert.False(view.Items[2].IsDone);
        Assert.Single(view.Extra);
        Assert.Empty(view.Commutes);
    }

    [Fact]
    public async Task Skipped_sessions_are_not_matched()
    {
        var (analytics, db) = Create();
        var store = new PlanStore(db);
        await store.ImportAsync(Plan(Planned(Monday, SportFamily.Run, 5)));
        var id = (await store.GetSessionsAsync(Monday, Monday))[0].Id;
        await store.SetStatusAsync(id, PlannedStatus.Skipped);
        db.Activities.Add(Activity(Monday, SportFamily.Run, 5));
        await db.SaveChangesAsync();

        var view = await analytics.PlanViewAsync(Monday, Monday);

        Assert.False(view.Items.Single().IsDone);
        Assert.Single(view.Extra);
    }

    // ---- warnings ----

    [Fact]
    public async Task Build_up_warning_when_this_weeks_running_exceeds_last_week_by_more_than_10_percent()
    {
        var (analytics, db) = Create();
        db.Activities.AddRange(
            Activity(Monday.AddDays(-5), SportFamily.Run, km: 30),
            Activity(Monday, SportFamily.Run, km: 20),
            Activity(Today, SportFamily.Run, km: 14)); // 34 > 33
        await db.SaveChangesAsync();

        var warnings = await analytics.WarningsAsync(Today);

        Assert.Contains(warnings, w => w.Type == WarningType.BuildUp);
    }

    [Fact]
    public async Task Pain_warning_after_two_reports_at_the_same_location_within_7_days()
    {
        var (analytics, db) = Create();
        db.PainReports.AddRange(
            new PainReportEntity { UserId = 1, Date = Today.AddDays(-4), Location = "kuit", Severity = 4 },
            new PainReportEntity { UserId = 1, Date = Today, Location = "kuit", Severity = 5 });
        await db.SaveChangesAsync();

        var warnings = await analytics.WarningsAsync(Today);

        Assert.Single(warnings, w => w.Type == WarningType.Pain);
    }

    [Fact]
    public async Task Recovery_warning_needs_low_hrv_for_3_nights_and_a_quality_session_planned_today_or_tomorrow()
    {
        var (analytics, db) = Create();
        var workouts = new Dictionary<string, WorkoutTemplate>
        {
            ["Fartlek"] = new("Fartlek", 6, [new WorkoutPhase(1, "Stevig", PhaseKind.Time, null, TimeSpan.FromMinutes(1), null, null, null, "Tempozone 4", "")]),
        };
        await new PlanStore(db).ImportAsync(new TrainingPlan([Planned(Today.AddDays(1), SportFamily.Run, 6, workout: "Fartlek")], workouts));

        // 40 normal nights around 60, then three clearly low ones
        for (var i = 3; i < 43; i++)
            db.RecoveryDays.Add(new RecoveryDayEntity { UserId = 1, Date = Today.AddDays(-i), Hrv = 58 + (i % 5), RestingHeartRate = 52 });
        for (var i = 0; i < 3; i++)
            db.RecoveryDays.Add(new RecoveryDayEntity { UserId = 1, Date = Today.AddDays(-i), Hrv = 35, RestingHeartRate = 52 });
        await db.SaveChangesAsync();

        var warnings = await analytics.WarningsAsync(Today);

        Assert.Contains(warnings, w => w.Type == WarningType.Recovery);
    }

    [Fact]
    public async Task Recovery_warning_is_not_raised_when_no_quality_session_is_planned()
    {
        var (analytics, db) = Create();
        for (var i = 3; i < 43; i++)
            db.RecoveryDays.Add(new RecoveryDayEntity { UserId = 1, Date = Today.AddDays(-i), Hrv = 58 + (i % 5), RestingHeartRate = 52 });
        for (var i = 0; i < 3; i++)
            db.RecoveryDays.Add(new RecoveryDayEntity { UserId = 1, Date = Today.AddDays(-i), Hrv = 35, RestingHeartRate = 52 });
        await db.SaveChangesAsync();

        Assert.DoesNotContain(await analytics.WarningsAsync(Today), w => w.Type == WarningType.Recovery);
    }
}
