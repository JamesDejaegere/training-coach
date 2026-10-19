using Microsoft.Extensions.Options;
using TrainingCoach.Infrastructure.Strava;

namespace TrainingCoach.Infrastructure.Tests.Strava;

internal static class StravaTestData
{
    public static string TestKey { get; } = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    public static TokenProtector Protector() => new(Options.Create(new SecurityOptions { TokenKey = TestKey }));

    public static StravaActivity Activity(long id, DateTimeOffset startUtc, string sport = "Run", double km = 10) => new()
    {
        Id = id,
        Name = $"Activity {id}",
        SportType = sport,
        StartDate = startUtc,
        StartDateLocal = startUtc.AddHours(2),
        Distance = km * 1000,
        MovingTime = 3000,
        ElapsedTime = 3100,
        AverageSpeed = 3.3,
        MaxSpeed = 5,
    };

    /// <summary>Real response shape of GET /athlete/activities (trimmed).</summary>
    public const string ActivitiesJson = """
        [
          {
            "id": 12345678901,
            "name": "Ochtendloop",
            "sport_type": "Run",
            "start_date": "2026-10-19T06:30:00Z",
            "start_date_local": "2026-10-19T08:30:00Z",
            "distance": 10234.5,
            "moving_time": 3300,
            "elapsed_time": 3400,
            "total_elevation_gain": 41.2,
            "average_speed": 3.1,
            "max_speed": 4.9,
            "average_heartrate": 141.3,
            "max_heartrate": 158,
            "average_cadence": 82.5,
            "suffer_score": 57,
            "commute": false,
            "trainer": false,
            "start_latlng": [51.0902, 3.4529],
            "end_latlng": [51.0905, 3.4531],
            "athlete": { "id": 42, "resource_state": 1 },
            "some_unknown_field": { "x": 1 }
          },
          {
            "id": 12345678902,
            "name": "Pendel",
            "sport_type": "Ride",
            "start_date": "2026-10-20T05:00:00Z",
            "start_date_local": "2026-10-20T07:00:00Z",
            "distance": 30100,
            "moving_time": 4000,
            "elapsed_time": 4200,
            "total_elevation_gain": 60,
            "average_speed": 7.5,
            "max_speed": 11,
            "commute": true,
            "trainer": false,
            "start_latlng": [],
            "end_latlng": null
          }
        ]
        """;
}

internal sealed class FakeStravaApi : IStravaApi
{
    public List<StravaActivity> Activities { get; } = [];
    public int PageSize { get; set; } = StravaClient.PageSize;
    public int RefreshCalls { get; private set; }
    public int? FailWithRateLimitOnCall { get; set; }
    public bool RefreshFails { get; set; }
    public List<(DateTimeOffset? After, int Page)> ActivityCalls { get; } = [];
    public string? LastAccessToken { get; private set; }
    public StravaTokens RefreshResult { get; set; } = new("refreshed-access", "refreshed-refresh", DateTimeOffset.UtcNow.AddHours(6), null);

    public string BuildAuthorizeUrl(string redirectUri, string state) => $"https://example.test/authorize?redirect={redirectUri}&state={state}";

    public Task<StravaTokens> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(new StravaTokens("access-" + code, "refresh-" + code, DateTimeOffset.UtcNow.AddHours(6), 42));

    public Task<StravaTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        RefreshCalls++;
        if (RefreshFails) throw new StravaAuthException("revoked");
        return Task.FromResult(RefreshResult);
    }

    public Task<IReadOnlyList<StravaActivity>> GetActivitiesAsync(string accessToken, DateTimeOffset? after, int page, CancellationToken ct = default)
    {
        LastAccessToken = accessToken;
        ActivityCalls.Add((after, page));
        if (FailWithRateLimitOnCall == ActivityCalls.Count) throw new StravaRateLimitedException("429");

        var matching = Activities.Where(a => after is null || a.StartDate > after).OrderBy(a => a.StartDate);
        return Task.FromResult<IReadOnlyList<StravaActivity>>(matching.Skip((page - 1) * PageSize).Take(PageSize).ToList());
    }
}
