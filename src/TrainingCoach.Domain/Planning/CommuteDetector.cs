namespace TrainingCoach.Domain.Planning;

public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusMeters = 6_371_000;

    /// <summary>Great-circle distance (haversine).</summary>
    public static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        static double Rad(double deg) => deg * Math.PI / 180;

        var dLat = Rad(b.Latitude - a.Latitude);
        var dLon = Rad(b.Longitude - a.Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(Rad(a.Latitude)) * Math.Cos(Rad(b.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}

/// <summary>Recognises commute rides: home to work or work to home (SPEC 7.4).</summary>
public static class CommuteDetector
{
    public const double DefaultRadiusMeters = 500;

    public static bool IsCommute(GeoPoint start, GeoPoint end, GeoPoint home, GeoPoint work, double radiusMeters = DefaultRadiusMeters)
    {
        bool Near(GeoPoint a, GeoPoint b) => GeoPoint.DistanceMeters(a, b) <= radiusMeters;

        return (Near(start, home) && Near(end, work)) || (Near(start, work) && Near(end, home));
    }
}
