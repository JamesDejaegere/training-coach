using System.Globalization;
using TrainingCoach.Domain;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Web.Components;

/// <summary>Dutch display formatting shared by the pages.</summary>
public static class Format
{
    public static readonly CultureInfo Culture = new("nl-BE");

    public static string Sport(SportFamily sport) => sport switch
    {
        SportFamily.Run => "Lopen",
        SportFamily.Ride => "Fietsen",
        SportFamily.Strength => "Kracht",
        SportFamily.Mobility => "Mobiliteit",
        _ => "Andere",
    };

    public static string SportBadge(SportFamily sport) => sport switch
    {
        SportFamily.Run => "text-bg-primary",
        SportFamily.Ride => "text-bg-success",
        SportFamily.Strength => "text-bg-warning",
        SportFamily.Mobility => "text-bg-info",
        _ => "text-bg-secondary",
    };

    public static string Date(DateOnly date) => date.ToString("dddd d MMMM yyyy", Culture);

    public static string ShortDate(DateOnly date) => date.ToString("ddd d/M", Culture);

    public static string Meters(double meters) => (meters / 1000).ToString("0.0", Culture) + " km";

    public static string Km(double? km) => km is null ? "–" : km.Value.ToString("0.#", Culture) + " km";

    public static string Duration(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>Running pace (min/km) or cycling speed (km/h), whichever suits the sport.</summary>
    public static string PaceOrSpeed(ActivityEntity a)
    {
        if (a.DistanceMeters <= 0 || a.MovingSeconds <= 0) return "–";

        if (a.Sport == SportFamily.Run)
        {
            var secondsPerKm = a.MovingSeconds / (a.DistanceMeters / 1000);
            return $"{(int)(secondsPerKm / 60)}:{(int)(secondsPerKm % 60):00} /km";
        }
        return (a.DistanceMeters / 1000 / (a.MovingSeconds / 3600.0)).ToString("0.0", Culture) + " km/u";
    }

    public static string Source(ActivityEntity a) => (a.StravaId, a.PolarId) switch
    {
        (not null, not null) => "Strava + Polar",
        (not null, null) => "Strava",
        (null, not null) => "Polar",
        _ => "Handmatig",
    };

    public static string Number(double value, string format = "0") => value.ToString(format, Culture);
}
