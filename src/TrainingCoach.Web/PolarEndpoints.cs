using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TrainingCoach.Infrastructure.Polar;

namespace TrainingCoach.Web;

/// <summary>OAuth flow for Polar AccessLink: /connect/polar redirects to Polar Flow, /callback/polar receives the code.</summary>
public static class PolarEndpoints
{
    private const string StateCookie = "polar_oauth_state";

    public static WebApplication MapPolarEndpoints(this WebApplication app)
    {
        app.MapGet("/connect/polar", (HttpContext http, IPolarApi api, IOptions<PolarOptions> options) =>
        {
            if (!options.Value.IsConfigured)
                return Results.Redirect("/instellingen?fout=polar-niet-geconfigureerd");

            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            http.Response.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true,
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Lax, // must survive the top-level redirect back from Polar
                MaxAge = TimeSpan.FromMinutes(10),
            });

            return Results.Redirect(api.BuildAuthorizeUrl(CallbackUrl(http.Request), state));
        });

        app.MapGet("/callback/polar", async (
            HttpContext http, string? code, string? state, string? error,
            IPolarApi api, PolarSyncService sync, IServiceScopeFactory scopes, ILoggerFactory logs) =>
        {
            var expected = http.Request.Cookies[StateCookie];
            http.Response.Cookies.Delete(StateCookie);

            if (!StateMatches(expected, state))
                return Results.Redirect("/instellingen?fout=ongeldige-state");
            if (error is not null || string.IsNullOrEmpty(code))
                return Results.Redirect("/instellingen?fout=polar-geweigerd");

            var tokens = await api.ExchangeCodeAsync(code, CallbackUrl(http.Request), http.RequestAborted);
            await sync.ConnectAsync(tokens, ct: http.RequestAborted);

            var logger = logs.CreateLogger("PolarConnect");
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scoped = scopes.CreateAsyncScope();
                    var result = await scoped.ServiceProvider.GetRequiredService<PolarSyncService>().SyncAsync();
                    logger.LogInformation("Initial Polar sync: {Status}, {Added} added, {Recovery} recovery days", result.Status, result.Added, result.RecoveryDays);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Initial Polar sync failed");
                }
            });

            return Results.Redirect("/instellingen?polar-verbonden=1");
        });

        return app;
    }

    private static string CallbackUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}/callback/polar";

    private static bool StateMatches(string? expected, string? actual) =>
        !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
