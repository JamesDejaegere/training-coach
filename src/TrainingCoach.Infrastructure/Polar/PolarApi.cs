using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TrainingCoach.Infrastructure.Polar;

/// <summary>Client id/secret come from .NET User Secrets (section "Polar"), never from Git or data/.</summary>
public sealed class PolarOptions
{
    public const string Section = "Polar";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>AccessLink tokens are long-lived and have no refresh token; <see cref="ExpiresAt"/> is informative.</summary>
public sealed record PolarTokens(string AccessToken, DateTimeOffset ExpiresAt, long PolarUserId);

public sealed record PolarHeartRate
{
    [JsonPropertyName("average")] public int? Average { get; init; }
    [JsonPropertyName("maximum")] public int? Maximum { get; init; }
}

public sealed record PolarSampleSet
{
    /// <summary>Seconds between two samples.</summary>
    [JsonPropertyName("recording-rate")] public int RecordingRate { get; init; }
    [JsonPropertyName("sample-type")] public string SampleType { get; init; } = "";
    /// <summary>Comma-separated values; an empty slot means "no value".</summary>
    [JsonPropertyName("data")] public string Data { get; init; } = "";
}

public sealed record PolarTrainingLoadPro
{
    [JsonPropertyName("cardio-load")] public double? CardioLoad { get; init; }
}

/// <summary>Subset of a Polar exercise (GET /v3/exercises?samples=true) that the app uses.</summary>
public sealed record PolarExercise
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    /// <summary>Local wall-clock start without offset, e.g. "2026-10-19T08:30:00".</summary>
    [JsonPropertyName("start_time")] public DateTime StartTime { get; init; }
    /// <summary>Minutes east of UTC.</summary>
    [JsonPropertyName("start_time_utc_offset")] public int StartTimeUtcOffset { get; init; }
    /// <summary>ISO 8601 duration, e.g. "PT1H3M5S".</summary>
    [JsonPropertyName("duration")] public string Duration { get; init; } = "PT0S";
    [JsonPropertyName("distance")] public double? Distance { get; init; }
    [JsonPropertyName("calories")] public int? Calories { get; init; }
    [JsonPropertyName("training_load")] public double? TrainingLoad { get; init; }
    [JsonPropertyName("heart_rate")] public PolarHeartRate? HeartRate { get; init; }
    [JsonPropertyName("sport")] public string Sport { get; init; } = "";
    [JsonPropertyName("detailed_sport_info")] public string DetailedSportInfo { get; init; } = "";
    [JsonPropertyName("samples")] public List<PolarSampleSet>? Samples { get; init; }
    [JsonPropertyName("training_load_pro")] public PolarTrainingLoadPro? TrainingLoadPro { get; init; }
}

/// <summary>Nightly Recharge: HRV, nightly heart rate and ANS charge.</summary>
public sealed record PolarNightlyRecharge
{
    [JsonPropertyName("date")] public DateOnly Date { get; init; }
    [JsonPropertyName("heart_rate_avg")] public double? HeartRateAvg { get; init; }
    [JsonPropertyName("heart_rate_variability_avg")] public double? HeartRateVariabilityAvg { get; init; }
    [JsonPropertyName("breathing_rate_avg")] public double? BreathingRateAvg { get; init; }
    [JsonPropertyName("ans_charge")] public double? AnsCharge { get; init; }
    [JsonPropertyName("ans_charge_status")] public int? AnsChargeStatus { get; init; }
    [JsonPropertyName("nightly_recharge_status")] public int? NightlyRechargeStatus { get; init; }
}

public sealed record PolarSleep
{
    [JsonPropertyName("date")] public DateOnly Date { get; init; }
    [JsonPropertyName("sleep_score")] public double? SleepScore { get; init; }
    /// <summary>Seconds.</summary>
    [JsonPropertyName("light_sleep")] public int? LightSleep { get; init; }
    [JsonPropertyName("deep_sleep")] public int? DeepSleep { get; init; }
    [JsonPropertyName("rem_sleep")] public int? RemSleep { get; init; }
}

public sealed class PolarRateLimitedException(string message) : Exception(message);

/// <summary>Token revoked or expired; the athlete has to connect again.</summary>
public sealed class PolarAuthException(string message) : Exception(message);

public interface IPolarApi
{
    string BuildAuthorizeUrl(string redirectUri, string state);
    Task<PolarTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);

    /// <summary>Registers the user with our client; required once before data can be read. "Already registered" is not an error.</summary>
    Task RegisterUserAsync(string accessToken, string memberId, CancellationToken ct = default);

    /// <summary>Exercises uploaded in the last 30 days, with heart-rate samples.</summary>
    Task<IReadOnlyList<PolarExercise>> GetExercisesAsync(string accessToken, CancellationToken ct = default);

    /// <summary>Last 28 nights.</summary>
    Task<IReadOnlyList<PolarNightlyRecharge>> GetNightlyRechargesAsync(string accessToken, CancellationToken ct = default);

    /// <summary>Last 28 nights.</summary>
    Task<IReadOnlyList<PolarSleep>> GetSleepAsync(string accessToken, CancellationToken ct = default);
}

public sealed class PolarClient(HttpClient http, IOptions<PolarOptions> options) : IPolarApi
{
    private const string Api = "https://www.polaraccesslink.com/v3/";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string BuildAuthorizeUrl(string redirectUri, string state) =>
        "https://flow.polar.com/oauth2/authorization" +
        $"?response_type=code&client_id={Uri.EscapeDataString(options.Value.ClientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        "&scope=accesslink.read_all" +
        $"&state={Uri.EscapeDataString(state)}";

    public async Task<PolarTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://polarremote.com/v2/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
            }),
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.ClientId}:{options.Value.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt64() : 31_535_999;
        return new PolarTokens(
            root.GetProperty("access_token").GetString()!,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            root.GetProperty("x_user_id").GetInt64());
    }

    public async Task RegisterUserAsync(string accessToken, string memberId, CancellationToken ct = default)
    {
        using var request = Authorized(HttpMethod.Post, "users", accessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { ["member-id"] = memberId }), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return; // already registered with this client
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<IReadOnlyList<PolarExercise>> GetExercisesAsync(string accessToken, CancellationToken ct = default) =>
        await GetListAsync<PolarExercise>("exercises?samples=true&zones=false&route=false", "exercises", accessToken, ct);

    public async Task<IReadOnlyList<PolarNightlyRecharge>> GetNightlyRechargesAsync(string accessToken, CancellationToken ct = default) =>
        await GetListAsync<PolarNightlyRecharge>("users/nightly-recharge", "recharges", accessToken, ct);

    public async Task<IReadOnlyList<PolarSleep>> GetSleepAsync(string accessToken, CancellationToken ct = default) =>
        await GetListAsync<PolarSleep>("users/sleep", "nights", accessToken, ct);

    /// <summary>Reads either a bare array or an object that wraps the array under <paramref name="wrapperKey"/>; 204/404 means no data.</summary>
    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, string wrapperKey, string accessToken, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Get, path, accessToken);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound) return [];
        await EnsureSuccessAsync(response, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return [];

        using var doc = JsonDocument.Parse(body);
        var array = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty(wrapperKey, out var inner) ? inner : default;
        return array.ValueKind == JsonValueKind.Array ? array.Deserialize<List<T>>(Json) ?? [] : [];
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, Api + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => new PolarRateLimitedException("Polar rate limit reached."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new PolarAuthException($"Polar rejected the credentials ({(int)response.StatusCode}): {body}"),
            _ => new HttpRequestException($"Polar returned {(int)response.StatusCode}: {body}", null, response.StatusCode),
        };
    }
}
