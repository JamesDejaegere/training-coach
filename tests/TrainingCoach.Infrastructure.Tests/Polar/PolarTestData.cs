using TrainingCoach.Infrastructure.Polar;

namespace TrainingCoach.Infrastructure.Tests.Polar;

internal static class PolarTestData
{
    /// <summary>Shape of GET /v3/exercises?samples=true (trimmed, hyphenated and underscored names as documented).</summary>
    public const string ExercisesJson = """
        [
          {
            "id": "AB12CD34",
            "upload_time": "2026-10-19T07:45:00.000Z",
            "polar_user": "https://www.polaraccesslink.com/v3/users/42",
            "device": "Polar Pacer Pro",
            "start_time": "2026-10-19T08:30:00",
            "start_time_utc_offset": 120,
            "duration": "PT55M30S",
            "distance": 9876.5,
            "calories": 640,
            "training_load": 88.5,
            "heart_rate": { "average": 143, "maximum": 161 },
            "sport": "RUNNING",
            "detailed_sport_info": "RUNNING",
            "has_route": true,
            "running-index": 48,
            "heart_rate_zones": [ { "index": 1, "lower-limit": 105, "upper-limit": 124, "in-zone": "PT3M" } ],
            "samples": [
              { "recording-rate": 5, "sample-type": "0", "data": "98,102,,110,121,130" },
              { "recording-rate": 1, "sample-type": "1", "data": "1.5,2.5" }
            ],
            "training_load_pro": { "date": "2026-10-19", "cardio-load": 79.2, "muscle-load": 12, "perceived-load": 30 }
          }
        ]
        """;

    public const string RechargesJson = """
        { "recharges": [
          { "polar_user": "x", "date": "2026-10-19", "heart_rate_avg": 52, "beat_to_beat_avg": 1150, "heart_rate_variability_avg": 61,
            "breathing_rate_avg": 14.1, "nightly_recharge_status": 4, "ans_charge": 3.5, "ans_charge_status": 4,
            "hrv_samples": { "00:10": 55 }, "breathing_samples": { "00:10": 14 } }
        ] }
        """;

    public const string SleepJson = """
        { "nights": [
          { "polar_user": "x", "date": "2026-10-19", "sleep_score": 82, "sleep_charge": 3, "light_sleep": 14000, "deep_sleep": 6000, "rem_sleep": 5000,
            "sleep_cycles": 5, "hypnogram": { "00:05": 2 }, "heart_rate_samples": { "00:05": 50 } }
        ] }
        """;

    public static PolarExercise Exercise(string id, DateTime localStart, string sport = "RUNNING", string duration = "PT1H", int offset = 120,
        int? avgHr = 140, string samples = "100,110,120") => new()
    {
        Id = id,
        StartTime = localStart,
        StartTimeUtcOffset = offset,
        Duration = duration,
        Distance = 10000,
        TrainingLoad = 90,
        Sport = sport,
        DetailedSportInfo = sport,
        HeartRate = avgHr is null ? null : new PolarHeartRate { Average = avgHr, Maximum = 170 },
        Samples = [new PolarSampleSet { RecordingRate = 5, SampleType = "0", Data = samples }],
        TrainingLoadPro = new PolarTrainingLoadPro { CardioLoad = 80 },
    };
}

internal sealed class FakePolarApi : IPolarApi
{
    public List<PolarExercise> Exercises { get; } = [];
    public List<PolarNightlyRecharge> Recharges { get; } = [];
    public List<PolarSleep> Nights { get; } = [];
    public List<string> Registered { get; } = [];
    public bool FailWithRateLimit { get; set; }
    public bool FailWithAuth { get; set; }
    public string? LastToken { get; private set; }

    public string BuildAuthorizeUrl(string redirectUri, string state) => $"https://example.test/polar?redirect={redirectUri}&state={state}";

    public Task<PolarTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
        Task.FromResult(new PolarTokens("polar-access-" + code, DateTimeOffset.UtcNow.AddDays(300), 4242));

    public Task RegisterUserAsync(string accessToken, string memberId, CancellationToken ct = default)
    {
        Registered.Add(memberId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolarExercise>> GetExercisesAsync(string accessToken, CancellationToken ct = default)
    {
        LastToken = accessToken;
        if (FailWithRateLimit) throw new PolarRateLimitedException("429");
        if (FailWithAuth) throw new PolarAuthException("401");
        return Task.FromResult<IReadOnlyList<PolarExercise>>(Exercises.ToList());
    }

    public Task<IReadOnlyList<PolarNightlyRecharge>> GetNightlyRechargesAsync(string accessToken, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PolarNightlyRecharge>>(Recharges.ToList());

    public Task<IReadOnlyList<PolarSleep>> GetSleepAsync(string accessToken, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PolarSleep>>(Nights.ToList());
}
