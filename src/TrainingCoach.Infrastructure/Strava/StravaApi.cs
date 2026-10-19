using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TrainingCoach.Infrastructure.Strava;

/// <summary>Client id/secret come from .NET User Secrets (section "Strava"), never from Git or data/.</summary>
public sealed class StravaOptions
{
    public const string Section = "Strava";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed record StravaTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, long? AthleteId);

/// <summary>Subset of the Strava v3 activity summary that the app uses.</summary>
public sealed record StravaActivity
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string SportType { get; init; } = "";
    public DateTimeOffset StartDate { get; init; }
    /// <summary>Local wall-clock time, serialised by Strava with a "Z" suffix.</summary>
    public DateTimeOffset StartDateLocal { get; init; }
    public double Distance { get; init; }
    public int MovingTime { get; init; }
    public int ElapsedTime { get; init; }
    public double TotalElevationGain { get; init; }
    public double AverageSpeed { get; init; }
    public double MaxSpeed { get; init; }
    public double? AverageHeartrate { get; init; }
    public double? MaxHeartrate { get; init; }
    public double? AverageCadence { get; init; }
    public double? AverageWatts { get; init; }
    public double? Kilojoules { get; init; }
    /// <summary>Strava "Relative Effort".</summary>
    public double? SufferScore { get; init; }
    public bool Commute { get; init; }
    public bool Trainer { get; init; }
    public double[]? StartLatlng { get; init; }
    public double[]? EndLatlng { get; init; }
}

public sealed class StravaRateLimitedException(string message) : Exception(message);

/// <summary>Token is invalid or revoked; the user has to connect again.</summary>
public sealed class StravaAuthException(string message) : Exception(message);

public interface IStravaApi
{
    string BuildAuthorizeUrl(string redirectUri, string state);
    Task<StravaTokens> ExchangeCodeAsync(string code, CancellationToken ct = default);
    Task<StravaTokens> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>One page (up to 200) of activities started after <paramref name="after"/>.</summary>
    Task<IReadOnlyList<StravaActivity>> GetActivitiesAsync(string accessToken, DateTimeOffset? after, int page, CancellationToken ct = default);
}

public sealed class StravaClient(HttpClient http, IOptions<StravaOptions> options) : IStravaApi
{
    public const int PageSize = 200;
    public const string Scope = "read,activity:read_all";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string BuildAuthorizeUrl(string redirectUri, string state) =>
        "https://www.strava.com/oauth/authorize" +
        $"?client_id={Uri.EscapeDataString(options.Value.ClientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        "&response_type=code&approval_prompt=auto" +
        $"&scope={Scope}" +
        $"&state={Uri.EscapeDataString(state)}";

    public Task<StravaTokens> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
        PostTokenAsync(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code }, ct);

    public Task<StravaTokens> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostTokenAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, ct);

    private async Task<StravaTokens> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = options.Value.ClientId;
        form["client_secret"] = options.Value.ClientSecret;

        using var response = await http.PostAsync("https://www.strava.com/oauth/token", new FormUrlEncodedContent(form), ct);
        await EnsureSuccessAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        long? athleteId = root.TryGetProperty("athlete", out var athlete) && athlete.TryGetProperty("id", out var id) ? id.GetInt64() : null;
        return new StravaTokens(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("expires_at").GetInt64()),
            athleteId);
    }

    public async Task<IReadOnlyList<StravaActivity>> GetActivitiesAsync(string accessToken, DateTimeOffset? after, int page, CancellationToken ct = default)
    {
        var url = $"https://www.strava.com/api/v3/athlete/activities?per_page={PageSize}&page={page}";
        if (after is { } a) url += $"&after={a.ToUnixTimeSeconds()}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<List<StravaActivity>>(Json, ct) ?? [];
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => new StravaRateLimitedException("Strava rate limit reached (100 requests / 15 min, 1000 / day)."),
            HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest when body.Contains("invalid", StringComparison.OrdinalIgnoreCase) || response.StatusCode == HttpStatusCode.Unauthorized
                => new StravaAuthException($"Strava rejected the credentials ({(int)response.StatusCode}): {body}"),
            _ => new HttpRequestException($"Strava returned {(int)response.StatusCode}: {body}", null, response.StatusCode),
        };
    }
}
