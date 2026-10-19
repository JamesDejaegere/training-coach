using Microsoft.Extensions.Time.Testing;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Strength;
using TrainingCoach.Infrastructure.Analytics;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Strength;
using TrainingCoach.Infrastructure.Tests.Persistence;

namespace TrainingCoach.Infrastructure.Tests.Strength;

public sealed class MuscleAnalyticsTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2027, 2, 10, 7, 0, 0, TimeSpan.Zero));

    public MuscleAnalyticsTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels"));

    public void Dispose() => _db.Dispose();

    private (MuscleAnalytics Muscles, StrengthStore Store, AppDbContext Db) Create()
    {
        var db = _db.CreateContext();
        var catalog = new ExerciseCatalog(db);
        var analytics = new TrainingAnalytics(db, new PlanStore(db), _time);
        return (new MuscleAnalytics(db, catalog, analytics), new StrengthStore(db, catalog, _time), db);
    }

    private static readonly DateOnly Today = new(2027, 2, 10);

    private static ActivityEntity Run(DateOnly date, double avgHr = 140, int minutes = 60) => new()
    {
        UserId = 1, Name = "run", Sport = SportFamily.Run, AverageHeartRate = avgHr,
        StartLocal = date.ToDateTime(new TimeOnly(8, 0)), StartUtc = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(7, 0)), DateTimeKind.Utc),
        MovingSeconds = minutes * 60, ElapsedSeconds = minutes * 60,
    };

    [Fact]
    public async Task Rolling_load_combines_completed_strength_sets_and_endurance_load_of_the_last_7_days()
    {
        var (muscles, store, db) = Create();
        db.Activities.Add(Run(Today.AddDays(-2)));
        db.Activities.Add(Run(Today.AddDays(-20))); // outside the window
        await db.SaveChangesAsync();

        var id = await store.StartSessionAsync(["calf-raise-two-leg"]);
        foreach (var set in (await store.GetSessionAsync(id))!.Entries[0].Sets)
            await store.UpdateSetAsync(set.Id, 15, 0, true, null);
        await store.FinishAsync(id, 6);

        var load = await muscles.RollingAsync(Today);

        var runLoad = TrainingAnalytics.LoadOf(db.Activities.First(a => a.StartLocal == Today.AddDays(-2).ToDateTime(new TimeOnly(8, 0))),
            new AthleteProfile(168, 55, null, null, 500)).Value;
        var expectedCalves = 3 + runLoad * MuscleLoad.EnduranceSetsPerLoad;
        Assert.Equal(expectedCalves, load[Muscle.Calves], precision: 6);
    }

    [Fact]
    public async Task Session_older_than_7_days_is_not_counted()
    {
        var (muscles, store, _) = Create();
        var id = await store.StartSessionAsync(["calf-raise-two-leg"]);
        foreach (var set in (await store.GetSessionAsync(id))!.Entries[0].Sets)
            await store.UpdateSetAsync(set.Id, 15, 0, true, null);
        await store.FinishAsync(id, 6);
        _time.Advance(TimeSpan.FromDays(8));

        var load = await muscles.RollingAsync(Today.AddDays(8));

        Assert.False(load.ContainsKey(Muscle.Calves));
    }

    [Fact]
    public async Task Without_any_data_the_load_is_empty()
    {
        var (muscles, _, _) = Create();

        Assert.Empty(await muscles.RollingAsync(Today));
    }
}
