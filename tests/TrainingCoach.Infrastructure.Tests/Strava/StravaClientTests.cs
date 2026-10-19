using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TrainingCoach.Infrastructure.Strava;

namespace TrainingCoach.Infrastructure.Tests.Strava;

public class StravaClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (StravaClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var options = Options.Create(new StravaOptions { ClientId = "1234", ClientSecret = "shh" });
        return (new StravaClient(new HttpClient(handler), options), handler);
    }

    [Fact]
    public void Authorize_url_requests_activity_read_all_and_carries_state_and_redirect()
    {
        var (client, _) = Create(_ => Json("{}"));

        var url = client.BuildAuthorizeUrl("https://localhost:7001/callback/strava", "abc 123");

        Assert.StartsWith("https://www.strava.com/oauth/authorize?", url);
        Assert.Contains("client_id=1234", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Flocalhost%3A7001%2Fcallback%2Fstrava", url);
        Assert.Contains("scope=read,activity:read_all", url);
        Assert.Contains("state=abc%20123", url);
        Assert.DoesNotContain("shh", url); // the secret never goes into a URL
    }

    [Fact]
    public async Task Exchange_code_posts_form_and_parses_tokens_and_athlete()
    {
        var (client, handler) = Create(_ => Json("""
            {"token_type":"Bearer","expires_at":1792000000,"refresh_token":"R","access_token":"A","athlete":{"id":42}}
            """));

        var tokens = await client.ExchangeCodeAsync("the-code");

        Assert.Equal(("A", "R", 42L), (tokens.AccessToken, tokens.RefreshToken, tokens.AthleteId));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792000000), tokens.ExpiresAt);
        var (request, body) = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://www.strava.com/oauth/token", request.RequestUri!.ToString());
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=the-code", body);
        Assert.Contains("client_secret=shh", body);
    }

    [Fact]
    public async Task Refresh_response_without_athlete_gives_null_athlete_id()
    {
        var (client, handler) = Create(_ => Json("""{"expires_at":1792000000,"refresh_token":"R2","access_token":"A2"}"""));

        var tokens = await client.RefreshAsync("old-refresh");

        Assert.Null(tokens.AthleteId);
        Assert.Equal("R2", tokens.RefreshToken);
        Assert.Contains("grant_type=refresh_token", handler.Requests.Single().Body);
        Assert.Contains("refresh_token=old-refresh", handler.Requests.Single().Body);
    }

    [Fact]
    public async Task Activities_are_requested_with_bearer_token_paging_and_after_and_parsed()
    {
        var (client, handler) = Create(_ => Json(StravaTestData.ActivitiesJson));

        var activities = await client.GetActivitiesAsync("tok", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), page: 3);

        var request = handler.Requests.Single().Request;
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("tok", request.Headers.Authorization.Parameter);
        Assert.Contains("per_page=200", request.RequestUri!.Query);
        Assert.Contains("page=3", request.RequestUri.Query);
        Assert.Contains($"after={new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()}", request.RequestUri.Query);

        Assert.Equal(2, activities.Count);
        var run = activities[0];
        Assert.Equal(12345678901L, run.Id);
        Assert.Equal("Run", run.SportType);
        Assert.Equal(141.3, run.AverageHeartrate);
        Assert.Equal(57, run.SufferScore);
        Assert.Equal([51.0902, 3.4529], run.StartLatlng!);
        Assert.True(activities[1].Commute);
        Assert.Null(activities[1].AverageHeartrate);
    }

    [Fact]
    public async Task First_page_without_after_has_no_after_parameter()
    {
        var (client, handler) = Create(_ => Json("[]"));

        await client.GetActivitiesAsync("tok", null, 1);

        Assert.DoesNotContain("after=", handler.Requests.Single().Request.RequestUri!.Query);
    }

    [Fact]
    public async Task Status_429_becomes_rate_limited_exception()
    {
        var (client, _) = Create(_ => Json("{}", HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<StravaRateLimitedException>(() => client.GetActivitiesAsync("tok", null, 1));
    }

    [Fact]
    public async Task Status_401_becomes_auth_exception()
    {
        var (client, _) = Create(_ => Json("""{"message":"Authorization Error"}""", HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<StravaAuthException>(() => client.GetActivitiesAsync("tok", null, 1));
    }

    [Fact]
    public async Task Revoked_refresh_token_is_an_auth_exception()
    {
        var (client, _) = Create(_ => Json("""{"message":"Bad Request","errors":[{"resource":"RefreshToken","field":"refresh_token","code":"invalid"}]}""", HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<StravaAuthException>(() => client.RefreshAsync("revoked"));
    }

    [Fact]
    public async Task Other_failures_surface_as_http_request_exception()
    {
        var (client, _) = Create(_ => Json("{}", HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetActivitiesAsync("tok", null, 1));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }
}
