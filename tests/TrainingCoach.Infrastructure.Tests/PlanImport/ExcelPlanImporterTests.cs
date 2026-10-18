using TrainingCoach.Domain;
using TrainingCoach.Domain.Planning;
using TrainingCoach.Infrastructure.PlanImport;

namespace TrainingCoach.Infrastructure.Tests.PlanImport;

/// <summary>Runs the importer against the real seed workbook (seed/Schema_Antwerp_10_Miles_2027.xlsx).</summary>
public class ExcelPlanImporterTests
{
    private static readonly Lazy<TrainingPlan> Plan = new(() => ExcelPlanImporter.Import(SeedPath()));

    private static string SeedPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "seed", "Schema_Antwerp_10_Miles_2027.xlsx");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Seed workbook not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Plan_covers_19_october_2026_to_25_april_2027_over_27_weeks()
    {
        var sessions = Plan.Value.Sessions;

        Assert.Equal(new DateOnly(2026, 10, 19), sessions.Min(s => s.Date));
        Assert.Equal(new DateOnly(2027, 4, 25), sessions.Max(s => s.Date));
        Assert.Equal(27, sessions.Select(s => s.Week).Distinct().Count());
    }

    [Fact]
    public void Race_day_is_the_antwerp_10_miles_run_of_16_1_km()
    {
        var race = Plan.Value.Sessions.Single(s => s.Date == new DateOnly(2027, 4, 25));

        Assert.Equal(SportFamily.Run, race.Sport);
        Assert.Equal("Antwerp 10 Miles", race.WorkoutName);
        Assert.Equal(16.1, race.PlannedDistanceKm);
    }

    [Fact]
    public void First_week_has_run_ride_strength_sessions_with_commutes_on_tuesday_and_friday()
    {
        var week1 = Plan.Value.Sessions.Where(s => s.Week == 1).OrderBy(s => s.Date).ToList();

        Assert.Equal(new DateOnly(2026, 10, 19), week1[0].Date); // Monday
        Assert.Equal(SportFamily.Run, week1[0].Sport);
        Assert.Equal(4, week1[0].PlannedDistanceKm);
        Assert.Equal("Rustig 4 km", week1[0].WorkoutName);

        var commutes = week1.Where(s => s.IsCommute).ToList();
        Assert.Equal([DayOfWeek.Tuesday, DayOfWeek.Friday], commutes.Select(c => c.Date.DayOfWeek));
        Assert.All(commutes, c => { Assert.Equal(SportFamily.Ride, c.Sport); Assert.Equal(60, c.PlannedDistanceKm); });

        Assert.Equal(2, week1.Count(s => s.Sport == SportFamily.Strength)); // Thursday + Saturday
    }

    [Fact]
    public void Run_plus_strength_day_becomes_two_sessions()
    {
        var thursday = Plan.Value.Sessions.Where(s => s.Date == new DateOnly(2027, 4, 15)).ToList(); // wk 26 Thursday

        Assert.Contains(thursday, s => s.Sport == SportFamily.Run && s.PlannedDistanceKm == 6.3);
        Assert.Contains(thursday, s => s.Sport == SportFamily.Strength);
    }

    [Fact]
    public void Rest_days_produce_no_session()
    {
        // Thursday and Friday of race week (week 27) are rest days.
        Assert.DoesNotContain(Plan.Value.Sessions, s => s.Date == new DateOnly(2027, 4, 22));
        Assert.DoesNotContain(Plan.Value.Sessions, s => s.Date == new DateOnly(2027, 4, 23));
    }

    [Fact]
    public void Every_run_workout_name_has_a_template()
    {
        var plan = Plan.Value;
        var names = plan.Sessions.Where(s => s.WorkoutName is not null).Select(s => s.WorkoutName!).Distinct();

        Assert.All(names, n => Assert.True(plan.Workouts.ContainsKey(n), $"Missing template '{n}'"));
        Assert.Equal(39, plan.Workouts.Count);
    }

    [Fact]
    public void Strides_workout_has_nested_repeat_over_phases_4_to_5()
    {
        var w = Plan.Value.Workouts["Rustig 5 km + strides"];

        var warmup = w.Phases[0];
        Assert.Equal((PhaseKind.Distance, 1.0, "HR-zone 1–2"), (warmup.Kind, warmup.DistanceKm, warmup.Target));

        var repeat = w.Phases.Single(p => p.Kind == PhaseKind.Repeat);
        Assert.Equal((6, 4, 5), (repeat.Repeats, repeat.RepeatFromPhase, repeat.RepeatToPhase));

        var stride = w.Phases.Single(p => p.Name == "Stride");
        Assert.Equal(TimeSpan.FromSeconds(20), stride.Duration);
        Assert.Equal(5.3, w.TotalKm);
    }

    [Fact]
    public void Quality_flag_is_true_for_tempo_intervals_and_false_for_easy_runs()
    {
        var w = Plan.Value.Workouts;

        Assert.False(w["Rustig 4 km"].IsQuality);
        Assert.False(w["Lange duurloop 7 km"].IsQuality);
        Assert.True(w["Fartlek 8×1' (6 km)"].IsQuality);
        Assert.True(w["Antwerp 10 Miles"].IsQuality);
    }

    [Fact]
    public void All_repeat_phases_reference_existing_earlier_phase_numbers()
    {
        foreach (var w in Plan.Value.Workouts.Values)
        foreach (var r in w.Phases.Where(p => p.Kind == PhaseKind.Repeat))
        {
            Assert.True(r.Repeats >= 2, w.Name);
            Assert.True(r.RepeatFromPhase > r.Number, w.Name);
            Assert.True(r.RepeatToPhase >= r.RepeatFromPhase, w.Name);
            Assert.Contains(w.Phases, p => p.Number == r.RepeatToPhase);
        }
    }
}
