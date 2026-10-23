using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TrainingCoach.Domain;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.PolarExport;
using TrainingCoach.Infrastructure.Tests.Persistence;
using static TrainingCoach.Infrastructure.Tests.PolarExport.PolarExportTestData;

namespace TrainingCoach.Infrastructure.Tests.PolarExport;

public sealed class PolarExportImporterTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2027, 2, 10, 7, 0, 0, TimeSpan.Zero));

    public PolarExportImporterTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels"));

    public void Dispose() => _db.Dispose();

    private PolarExportImporter Importer(AppDbContext db) => new(db, _time);

    private static (string Name, string Content) Session(string id, string json) =>
        (SessionFile(id, json), json);

    // ---- sport ids ----

    [Theory]
    [InlineData("1", SportFamily.Run)]
    [InlineData("2", SportFamily.Ride)]
    [InlineData("5", SportFamily.Ride)]
    [InlineData("15", SportFamily.Strength)]
    [InlineData("111", SportFamily.Strength)]
    [InlineData("126", SportFamily.Strength)]
    [InlineData("127", SportFamily.Strength)]
    [InlineData("3", SportFamily.Other)]
    [InlineData("12", SportFamily.Other)]
    [InlineData("35", SportFamily.Other)]
    [InlineData("28", SportFamily.Other)]
    [InlineData("99999", SportFamily.Other)]
    [InlineData(null, SportFamily.Other)]
    public void Maps_polar_sport_ids_to_sport_families(string? id, SportFamily expected)
    {
        Assert.Equal(expected, PolarExportMapper.ToSportFamily(id));
    }

    // ---- sessions ----

    [Fact]
    public async Task A_session_without_counterpart_becomes_a_polar_only_activity_with_hr_samples_and_load()
    {
        await using var db = _db.CreateContext();
        using var zip = Zip(Session("1", SessionJson()));

        var result = await Importer(db).ImportAsync(zip);

        Assert.Equal((1, 0, 0), (result.SessionsCreated, result.SessionsMerged, result.SessionsUpdated));
        var a = await db.Activities.AsNoTracking().SingleAsync();
        Assert.Equal(("8300009631", null, SportFamily.Run), (a.PolarId, a.StravaId, a.Sport));
        Assert.Equal(new DateTime(2026, 2, 2, 5, 56, 37, DateTimeKind.Utc), a.StartUtc); // local 06:56 minus 60 min offset
        Assert.Equal(new DateTime(2026, 2, 2, 6, 56, 37), a.StartLocal);
        Assert.Equal((2113, 2113), (a.MovingSeconds, a.ElapsedSeconds));
        Assert.Equal(5599.7, a.DistanceMeters, precision: 1);
        Assert.Equal((148, 171), (a.AverageHeartRate, a.MaxHeartRate));
        Assert.Equal(47.55, a.PolarCardioLoad);
        Assert.Equal("100,101,102,103", a.HrSamples);
        Assert.Equal(1, a.HrSampleIntervalSeconds);
        Assert.Null(a.Rpe); // Polar's categorical RPE is not mapped onto our 1-10 scale
    }

    [Fact]
    public async Task A_session_that_matches_a_strava_activity_is_merged_keeping_strava_facts()
    {
        await using var db = _db.CreateContext();
        db.Activities.Add(new ActivityEntity
        {
            UserId = 1, StravaId = 77, Name = "Ochtendloop", Sport = SportFamily.Run,
            StartUtc = new DateTime(2026, 2, 2, 5, 57, 30, DateTimeKind.Utc), StartLocal = new DateTime(2026, 2, 2, 6, 57, 30),
            ElapsedSeconds = 2150, MovingSeconds = 2100, DistanceMeters = 5620, AverageHeartRate = 140, RelativeEffort = 57,
        });
        await db.SaveChangesAsync();
        using var zip = Zip(Session("1", SessionJson()));

        var result = await Importer(db).ImportAsync(zip);

        Assert.Equal((0, 1), (result.SessionsCreated, result.SessionsMerged));
        var a = await db.Activities.AsNoTracking().SingleAsync();
        Assert.Equal((77L, "8300009631", "Ochtendloop", 57.0), (a.StravaId, a.PolarId, a.Name, a.RelativeEffort));
        Assert.Equal(148, a.AverageHeartRate); // Polar preferred for heart rate
        Assert.Equal("100,101,102,103", a.HrSamples);
        Assert.Equal(5620, a.DistanceMeters);
    }

    [Fact]
    public async Task Importing_the_same_export_twice_updates_instead_of_duplicating()
    {
        await using var db = _db.CreateContext();
        using var first = Zip(Session("1", SessionJson()));
        await Importer(db).ImportAsync(first);

        using var second = Zip(Session("1", SessionJson(cardioLoad: 50.1)));
        var result = await Importer(db).ImportAsync(second);

        Assert.Equal((0, 0, 1), (result.SessionsCreated, result.SessionsMerged, result.SessionsUpdated));
        var a = await db.Activities.AsNoTracking().SingleAsync();
        Assert.Equal(50.1, a.PolarCardioLoad);
    }

    [Fact]
    public async Task A_session_already_synced_through_accesslink_with_another_id_is_updated_not_duplicated()
    {
        await using var db = _db.CreateContext();
        db.Activities.Add(new ActivityEntity
        {
            UserId = 1, PolarId = "ACCESSLINKHASH", Name = "RUNNING", Sport = SportFamily.Run,
            StartUtc = new DateTime(2026, 2, 2, 5, 56, 37, DateTimeKind.Utc), StartLocal = new DateTime(2026, 2, 2, 6, 56, 37),
            ElapsedSeconds = 2113, MovingSeconds = 2113,
        });
        await db.SaveChangesAsync();
        using var zip = Zip(Session("1", SessionJson()));

        var result = await Importer(db).ImportAsync(zip);

        Assert.Equal(1, result.SessionsUpdated + result.SessionsMerged);
        var a = await db.Activities.AsNoTracking().SingleAsync();
        Assert.Equal("ACCESSLINKHASH", a.PolarId); // existing id is kept
        Assert.Equal("100,101,102,103", a.HrSamples);
    }

    [Fact]
    public async Task Different_sport_or_far_apart_start_stays_separate()
    {
        await using var db = _db.CreateContext();
        db.Activities.Add(new ActivityEntity
        {
            UserId = 1, StravaId = 77, Sport = SportFamily.Ride,
            StartUtc = new DateTime(2026, 2, 2, 5, 56, 37, DateTimeKind.Utc), ElapsedSeconds = 2113,
        });
        await db.SaveChangesAsync();
        using var zip = Zip(Session("1", SessionJson()));

        var result = await Importer(db).ImportAsync(zip);

        Assert.Equal(1, result.SessionsCreated);
        Assert.Equal(2, await db.Activities.CountAsync());
    }

    [Fact]
    public async Task Sessions_without_samples_or_heart_rate_still_import()
    {
        await using var db = _db.CreateContext();
        using var zip = Zip(Session("2", SessionJson("2", hrAvg: null, hrMax: null, hrValues: null, cardioLoad: null)));

        await Importer(db).ImportAsync(zip);

        var a = await db.Activities.AsNoTracking().SingleAsync();
        Assert.Null(a.AverageHeartRate);
        Assert.Null(a.HrSamples);
        Assert.Null(a.PolarCardioLoad);
    }

    [Fact]
    public async Task Missing_timezone_offset_falls_back_to_the_local_time_zone_of_that_date()
    {
        await using var db = _db.CreateContext();
        // 2026-07-01 is summer time in Brussels (UTC+2)
        using var zip = Zip(Session("3", SessionJson("3", start: "2026-07-01T10:00:00", offsetMinutes: null)));

        await Importer(db).ImportAsync(zip);

        Assert.Equal(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc), (await db.Activities.AsNoTracking().SingleAsync()).StartUtc);
    }

    [Fact]
    public async Task Heart_rate_interval_other_than_one_second_is_kept()
    {
        await using var db = _db.CreateContext();
        using var zip = Zip(Session("4", SessionJson("4", hrValues: "[90,95,100]", hrIntervalMillis: 5000)));

        await Importer(db).ImportAsync(zip);

        Assert.Equal(5, (await db.Activities.AsNoTracking().SingleAsync()).HrSampleIntervalSeconds);
    }

    // ---- nights ----

    [Fact]
    public async Task Nights_are_imported_with_hrv_resting_hr_ans_charge_and_sleep()
    {
        await using var db = _db.CreateContext();
        using var zip = Zip(
            ("nightly_recovery_60179771-aaaa.json", NightlyRecoveryJson(("2026-10-19", 66, 1193, -3.9529, 2, 4))),
            ("sleep_result_60179771-bbbb.json", SleepResultJson(("2026-10-19", "PT3H22M", "PT1H31M", "PT1H49M"))),
            ("sleep_score_60179771-cccc.json", SleepScoreJson(("2026-10-19", 76.4023))));

        var result = await Importer(db).ImportAsync(zip);

        Assert.Equal(1, result.NightsAdded);
        var n = await db.RecoveryDays.AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 19), n.Date);
        Assert.Equal(66, n.Hrv);
        Assert.Equal(50, n.RestingHeartRate); // 60000 / 1193 ms = 50.3 bpm
        Assert.Equal((-4.0, 2, 4), (n.AnsCharge, n.AnsChargeStatus, n.NightlyRechargeStatus));
        Assert.Equal(77, n.SleepScore); // rounded up, like AccessLink's score
        Assert.Equal(3 * 3600 + 22 * 60 + 91 * 60 + 109 * 60, n.SleepSeconds);
        Assert.Equal((91 * 60, 109 * 60), (n.DeepSleepSeconds, n.RemSleepSeconds));
    }

    [Fact]
    public async Task Existing_nights_keep_their_values_and_only_fill_gaps()
    {
        await using var db = _db.CreateContext();
        db.RecoveryDays.Add(new RecoveryDayEntity { UserId = 1, Date = new DateOnly(2026, 10, 19), Hrv = 61, AnsCharge = 3.5 });
        await db.SaveChangesAsync();
        using var zip = Zip(("nightly_recovery_x.json", NightlyRecoveryJson(("2026-10-19", 66, 1193, -3.9529, 2, 4))));

        var result = await Importer(db).ImportAsync(zip);

        Assert.Equal((0, 1), (result.NightsAdded, result.NightsFilled));
        var n = await db.RecoveryDays.AsNoTracking().SingleAsync();
        Assert.Equal((61.0, 3.5), (n.Hrv, n.AnsCharge)); // AccessLink values win
        Assert.Equal(50, n.RestingHeartRate);            // gap filled
    }

    [Fact]
    public async Task Missing_values_stay_null_instead_of_becoming_zero_and_empty_nights_are_not_stored()
    {
        await using var db = _db.CreateContext();
        using var zip = Zip(("nightly_recovery_x.json", NightlyRecoveryJson(
            ("2025-01-01", null, 1200, null, null, null),    // only the heart rate is known
            ("2025-01-02", null, null, null, null, null)))); // nothing known: no row

        var result = await Importer(db).ImportAsync(zip);

        Assert.Equal(1, result.NightsAdded);
        var n = await db.RecoveryDays.AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2025, 1, 1), n.Date);
        Assert.Null(n.Hrv);
        Assert.Equal(50, n.RestingHeartRate);
        Assert.Null(n.AnsCharge);
    }

    [Fact]
    public async Task Sleep_only_nights_are_imported_too_and_reimport_is_idempotent()
    {
        await using var db = _db.CreateContext();
        var entries = new[] { ("sleep_score_x.json", SleepScoreJson(("2024-05-05", 70.0))) };

        using (var zip = Zip(entries)) await Importer(db).ImportAsync(zip);
        using (var zip = Zip(entries)) await Importer(db).ImportAsync(zip);

        Assert.Equal(70, (await db.RecoveryDays.AsNoTracking().SingleAsync()).SleepScore);
    }

    // ---- robustness ----

    [Fact]
    public async Task Unknown_entries_malformed_json_and_unsafe_names_are_ignored_without_failing()
    {
        await using var db = _db.CreateContext();
        using var zip = Zip(
            ("account-data-123.json", """{ "firstName": "x" }"""),
            ("247ohr_2026_09-abc.json", """{ "deviceDays": [] }"""),
            ("../training-session_evil.json", SessionJson("evil")),
            ("training-session_broken.json", "{ this is not json"),
            ("training-session_no-start.json", """{ "identifier": { "id": "x" }, "exercises": [] }"""),
            Session("ok", SessionJson("ok")));

        var result = await Importer(db).ImportAsync(zip);

        Assert.Equal(1, result.SessionsCreated);
        Assert.Equal(2, result.FilesSkipped); // broken json + session without start; unsafe and unknown names are ignored silently
        Assert.Equal(1, await db.Activities.CountAsync());
    }

    [Fact]
    public async Task An_empty_or_unrelated_zip_imports_nothing()
    {
        await using var db = _db.CreateContext();
        using var zip = Zip(("readme.txt", "hello"));

        var result = await Importer(db).ImportAsync(zip);

        Assert.Equal(new PolarExportResult(0, 0, 0, 0, 0, 0), result);
    }

    [Fact]
    public async Task Progress_is_reported_per_batch()
    {
        await using var db = _db.CreateContext();
        var messages = new List<string>();
        using var zip = Zip(Session("1", SessionJson("1")), Session("2", SessionJson("2", start: "2026-02-03T06:56:37")));

        await Importer(db).ImportAsync(zip, new Progress<string>(messages.Add));
        await Task.Delay(50); // Progress<T> posts to the thread pool

        Assert.NotEmpty(messages);
    }
}
