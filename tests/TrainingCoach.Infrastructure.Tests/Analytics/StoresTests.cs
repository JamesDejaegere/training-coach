using TrainingCoach.Domain;
using TrainingCoach.Infrastructure.Analytics;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Tests.Persistence;

namespace TrainingCoach.Infrastructure.Tests.Analytics;

public sealed class StoresTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedActivityAsync(AppDbContext db, SportFamily sport = SportFamily.Run, int daysAgo = 0)
    {
        var activity = new ActivityEntity
        {
            UserId = 1, Name = "x", Sport = sport,
            StartLocal = new DateTime(2027, 2, 10, 8, 0, 0).AddDays(-daysAgo),
            StartUtc = new DateTime(2027, 2, 10, 7, 0, 0, DateTimeKind.Utc).AddDays(-daysAgo),
        };
        db.Activities.Add(activity);
        await db.SaveChangesAsync();
        return activity.Id;
    }

    [Fact]
    public async Task Settings_default_then_save_and_reload()
    {
        await using var db = _db.CreateContext();
        var store = new AthleteSettingsStore(db);
        var settings = await store.GetAsync();
        Assert.Equal((0, 168), (settings.Id, settings.HrMax));

        settings.HomeLatitude = 51.09;
        settings.HomeLongitude = 3.45;
        settings.RestingHrOverride = 50;
        await store.SaveAsync(settings);

        await using var fresh = _db.CreateContext();
        var loaded = await new AthleteSettingsStore(fresh).GetAsync();
        Assert.Equal((51.09, 50), (loaded.HomeLatitude, loaded.RestingHrOverride));
        Assert.Equal(1, fresh.AthleteSettings.Count());
    }

    [Theory]
    [InlineData(90, null)]
    [InlineData(168, 20)]
    [InlineData(168, 170)]
    public async Task Implausible_heart_rates_are_rejected(int hrMax, int? resting)
    {
        await using var db = _db.CreateContext();
        var store = new AthleteSettingsStore(db);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.SaveAsync(new AthleteSettingsEntity { UserId = 1, HrMax = hrMax, RestingHrOverride = resting }));
    }

    [Fact]
    public async Task List_is_newest_first_and_can_filter_by_sport()
    {
        await using var db = _db.CreateContext();
        await SeedActivityAsync(db, SportFamily.Run, daysAgo: 2);
        await SeedActivityAsync(db, SportFamily.Ride, daysAgo: 0);
        await SeedActivityAsync(db, SportFamily.Run, daysAgo: 1);
        var store = new ActivityStore(db);

        var all = await store.ListAsync();
        var runs = await store.ListAsync(SportFamily.Run);

        Assert.Equal([SportFamily.Ride, SportFamily.Run, SportFamily.Run], all.Select(a => a.Sport));
        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].StartUtc > runs[1].StartUtc);
    }

    [Fact]
    public async Task Rpe_is_saved_cleared_and_validated()
    {
        await using var db = _db.CreateContext();
        var id = await SeedActivityAsync(db);
        var store = new ActivityStore(db);

        await store.SetRpeAsync(id, 7);
        Assert.Equal(7, (await store.GetAsync(id))!.Rpe);

        await store.SetRpeAsync(id, null);
        Assert.Null((await store.GetAsync(id))!.Rpe);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SetRpeAsync(id, 11));
    }

    [Fact]
    public async Task Pain_is_stored_on_the_training_day_with_normalised_location_and_can_be_deleted()
    {
        await using var db = _db.CreateContext();
        var id = await SeedActivityAsync(db, daysAgo: 3);
        var store = new ActivityStore(db);

        var report = await store.AddPainAsync(id, "  Kuit ", 4, "links");

        Assert.Equal((new DateOnly(2027, 2, 7), "kuit", 4), (report.Date, report.Location, report.Severity));
        Assert.Single(await store.PainForActivityAsync(id));

        await store.DeletePainAsync(report.Id);
        Assert.Empty(await store.PainForActivityAsync(id));
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("kuit", 0)]
    [InlineData("kuit", 11)]
    public async Task Invalid_pain_input_is_rejected(string location, int severity)
    {
        await using var db = _db.CreateContext();
        var id = await SeedActivityAsync(db);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => new ActivityStore(db).AddPainAsync(id, location, severity));
    }
}
