using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TrainingCoach.Infrastructure.Polar;

namespace TrainingCoach.Infrastructure.Tests.Polar;

public class PolarClientTests
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

    private static (PolarClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new PolarClient(new HttpClient(handler), Options.Create(new PolarOptions { ClientId = "cid", ClientSecret = "sec" })), handler);
    }

    [Fact]
    public void Authorize_url_points_to_polar_flow_with_read_scope_state_and_redirect()
    {
        var (client, _) = Create(_ => Json("{}"));

        var url = client.BuildAuthorizeUrl("https://localhost:7001/callback/polar", "st@te");

        Assert.StartsWith("https://flow.polar.com/oauth2/authorization?", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=cid", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Flocalhost%3A7001%2Fcallback%2Fpolar", url);
        Assert.Contains("scope=accesslink.read_all", url);
        Assert.Contains("state=st%40te", url);
        Assert.DoesNotContain("secret", url); // the secret never goes into a URL
    }

    [Fact]
    public async Task Token_exchange_uses_basic_auth_and_parses_user_id()
    {
        var (client, handler) = Create(_ => Json("""{"access_token":"tok","token_type":"bearer","expires_in":31535999,"x_user_id":987654}"""));

        var tokens = await client.ExchangeCodeAsync("the-code", "https://localhost:7001/callback/polar");

        Assert.Equal(("tok", 987654L), (tokens.AccessToken, tokens.PolarUserId));
        Assert.True(tokens.ExpiresAt > DateTimeOffset.UtcNow.AddDays(360));

        var (request, body) = handler.Requests.Single();
        Assert.Equal("https://polarremote.com/v2/oauth2/token", request.RequestUri!.ToString());
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("cid:sec", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=the-code", body);
        Assert.Contains("redirect_uri=https%3A%2F%2Flocalhost%3A7001%2Fcallback%2Fpolar", body);
        Assert.DoesNotContain("client_secret", body); // credentials travel in the Authorization header only
    }

    [Fact]
    public async Task Registering_posts_member_id_and_treats_409_as_already_registered()
    {
        var (client, handler) = Create(_ => Json("{}", HttpStatusCode.Conflict));

        await client.RegisterUserAsync("tok", "training-coach-1");

        var (request, body) = handler.Requests.Single();
        Assert.Equal("https://www.polaraccesslink.com/v3/users", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Contains("\"member-id\":\"training-coach-1\"", body);
    }

    [Fact]
    public async Task Registering_without_consent_is_an_auth_exception()
    {
        var (client, _) = Create(_ => Json("{}", HttpStatusCode.Forbidden));

        await Assert.ThrowsAsync<PolarAuthException>(() => client.RegisterUserAsync("tok", "m"));
    }

    [Fact]
    public async Task Exercises_are_requested_with_samples_and_parsed_including_hyphenated_fields()
    {
        var (client, handler) = Create(_ => Json(PolarTestData.ExercisesJson));

        var exercises = await client.GetExercisesAsync("tok");

        var uri = handler.Requests.Single().Request.RequestUri!;
        Assert.Equal("/v3/exercises", uri.AbsolutePath);
        Assert.Contains("samples=true", uri.Query);

        var e = Assert.Single(exercises);
        Assert.Equal("AB12CD34", e.Id);
        Assert.Equal(new DateTime(2026, 10, 19, 8, 30, 0), e.StartTime);
        Assert.Equal(120, e.StartTimeUtcOffset);
        Assert.Equal("PT55M30S", e.Duration);
        Assert.Equal(88.5, e.TrainingLoad);
        Assert.Equal((143, 161), (e.HeartRate!.Average, e.HeartRate.Maximum));
        Assert.Equal(79.2, e.TrainingLoadPro!.CardioLoad);
        Assert.Equal(2, e.Samples!.Count);
        Assert.Equal((5, "0", "98,102,,110,121,130"), (e.Samples[0].RecordingRate, e.Samples[0].SampleType, e.Samples[0].Data));
    }

    [Fact]
    public async Task Exercises_list_may_also_be_wrapped_in_an_object()
    {
        var (client, _) = Create(_ => Json("{\"exercises\":" + PolarTestData.ExercisesJson + "}"));

        Assert.Single(await client.GetExercisesAsync("tok"));
    }

    [Fact]
    public async Task No_content_means_an_empty_list()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        Assert.Empty(await client.GetExercisesAsync("tok"));
        Assert.Empty(await client.GetNightlyRechargesAsync("tok"));
        Assert.Empty(await client.GetSleepAsync("tok"));
    }

    [Fact]
    public async Task Nightly_recharge_and_sleep_are_read_from_their_wrapper_objects()
    {
        var (client, handler) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("sleep") ? Json(PolarTestData.SleepJson) : Json(PolarTestData.RechargesJson));

        var recharge = Assert.Single(await client.GetNightlyRechargesAsync("tok"));
        var sleep = Assert.Single(await client.GetSleepAsync("tok"));

        Assert.Equal(new DateOnly(2026, 10, 19), recharge.Date);
        Assert.Equal((61.0, 52.0, 3.5), (recharge.HeartRateVariabilityAvg, recharge.HeartRateAvg, recharge.AnsCharge));
        Assert.Equal(4, recharge.AnsChargeStatus);
        Assert.Equal((82.0, 14000, 6000, 5000), (sleep.SleepScore, sleep.LightSleep, sleep.DeepSleep, sleep.RemSleep));
        Assert.Contains(handler.Requests, r => r.Request.RequestUri!.AbsolutePath == "/v3/users/nightly-recharge");
        Assert.Contains(handler.Requests, r => r.Request.RequestUri!.AbsolutePath == "/v3/users/sleep");
    }

    [Fact]
    public async Task Status_429_is_rate_limited_and_401_is_auth()
    {
        var (limited, _) = Create(_ => Json("{}", HttpStatusCode.TooManyRequests));
        var (unauthorized, _) = Create(_ => Json("{}", HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<PolarRateLimitedException>(() => limited.GetExercisesAsync("tok"));
        await Assert.ThrowsAsync<PolarAuthException>(() => unauthorized.GetExercisesAsync("tok"));
    }

    [Fact]
    public async Task Other_failures_surface_as_http_request_exception()
    {
        var (client, _) = Create(_ => Json("{}", HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetExercisesAsync("tok"));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }
}
