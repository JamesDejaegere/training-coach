using TrainingCoach.Domain.Planning;

namespace TrainingCoach.Domain.Tests.Planning;

public class CommuteDetectorTests
{
    // Aalter and Ghent centre, roughly 30 km apart.
    private static readonly GeoPoint Home = new(51.0902, 3.4529);
    private static readonly GeoPoint Work = new(51.0543, 3.7174);

    [Fact]
    public void Distance_between_aalter_and_ghent_is_about_18_km()
    {
        var km = GeoPoint.DistanceMeters(Home, Work) / 1000;

        Assert.InRange(km, 17, 20);
    }

    [Fact]
    public void Distance_to_self_is_zero()
    {
        Assert.Equal(0, GeoPoint.DistanceMeters(Home, Home), precision: 6);
    }

    [Fact]
    public void Home_to_work_is_a_commute()
    {
        Assert.True(CommuteDetector.IsCommute(Offset(Home, 100), Offset(Work, 200), Home, Work));
    }

    [Fact]
    public void Work_to_home_is_a_commute()
    {
        Assert.True(CommuteDetector.IsCommute(Work, Home, Home, Work));
    }

    [Fact]
    public void Ride_ending_far_from_work_is_not_a_commute()
    {
        Assert.False(CommuteDetector.IsCommute(Home, Offset(Work, 2000), Home, Work));
    }

    [Fact]
    public void Round_trip_from_home_is_not_a_commute()
    {
        Assert.False(CommuteDetector.IsCommute(Home, Home, Home, Work));
    }

    [Fact]
    public void Radius_is_500_m_by_default_and_configurable()
    {
        var start = Offset(Home, 600);
        Assert.False(CommuteDetector.IsCommute(start, Work, Home, Work));
        Assert.True(CommuteDetector.IsCommute(start, Work, Home, Work, radiusMeters: 700));
    }

    /// <summary>Moves a point roughly <paramref name="meters"/> north.</summary>
    private static GeoPoint Offset(GeoPoint p, double meters) => new(p.Latitude + meters / 111_320.0, p.Longitude);
}
