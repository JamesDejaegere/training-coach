using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TrainingCoach.Infrastructure.Strava;

namespace TrainingCoach.Web;

/// <summary>OAuth flow for Strava: /connect/strava redirects to Strava, /callback/strava receives the code.</summary>
public static class StravaEndpoints
{
    private const string StateCookie = "strava_oauth_state";
    private const string RequiredScope = "activity:read_all";

    public static WebApplication MapStravaEndpoints(this WebApplication app)
    {
        app.MapGet("/connect/strava", (HttpContext http, IStravaApi api, IOptions<StravaOptions> options) =>
        {
            if (!options.Value.IsConfigured)
                return Results.Redirect("/instellingen?fout=niet-geconfigureerd");

            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            http.Response.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true,
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Lax, // must survive the top-level redirect back from strava.com
                MaxAge = TimeSpan.FromMinutes(10),
            });

            return Results.Redirect(api.BuildAuthorizeUrl(CallbackUrl(http.Request), state));
        });

        app.MapGet("/callback/strava", async (
            HttpContext http, string? code, string? state, string? error, string? scope,
            IStravaApi api, StravaSyncService sync, IServiceScopeFactory scopes, ILoggerFactory logs) =>
        {
            var expected = http.Request.Cookies[StateCookie];
            http.Response.Cookies.Delete(StateCookie);

            if (!StateMatches(expected, state))
                return Results.Redirect("/instellingen?fout=ongeldige-state");
            if (error is not null || string.IsNullOrEmpty(code))
                return Results.Redirect("/instellingen?fout=geweigerd");
            if (scope is null || !scope.Split(',').Contains(RequiredScope))
                return Results.Redirect("/instellingen?fout=onvoldoende-rechten");

            var tokens = await api.ExchangeCodeAsync(code, http.RequestAborted);
            await sync.ConnectAsync(tokens, ct: http.RequestAborted);

            // The first sync pulls the whole history and can take a while: run it in the background.
            var logger = logs.CreateLogger("StravaConnect");
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scoped = scopes.CreateAsyncScope();
                    var result = await scoped.ServiceProvider.GetRequiredService<StravaSyncService>().SyncAsync();
                    logger.LogInformation("Initial Strava sync: {Status}, {Added} added", result.Status, result.Added);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Initial Strava sync failed");
                }
            });

            return Results.Redirect("/instellingen?verbonden=1");
        });

        return app;
    }

    private static string CallbackUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}/callback/strava";

    private static bool StateMatches(string? expected, string? actual) =>
        !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
