using TrainingCoach.Domain;
using TrainingCoach.Domain.Planning;

namespace TrainingCoach.Domain.Tests.Planning;

public class SessionDeduplicationTests
{
    private static readonly DateTime Start = new(2027, 2, 3, 6, 30, 0, DateTimeKind.Utc);

    private static SessionKey Key(SportFamily sport = SportFamily.Run, int startOffsetMinutes = 0, int seconds = 3600) =>
        new(sport, Start.AddMinutes(startOffsetMinutes), seconds);

    [Fact]
    public void Same_sport_start_and_duration_is_the_same_session()
    {
        Assert.True(SessionDeduplication.IsSameSession(Key(), Key()));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    [InlineData(1)]
    public void Start_times_within_5_minutes_match(int offset)
    {
        Assert.True(SessionDeduplication.IsSameSession(Key(), Key(startOffsetMinutes: offset)));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(-30)]
    public void Start_times_more_than_5_minutes_apart_do_not_match(int offset)
    {
        Assert.False(SessionDeduplication.IsSameSession(Key(), Key(startOffsetMinutes: offset)));
    }

    [Fact]
    public void Different_sport_family_does_not_match()
    {
        Assert.False(SessionDeduplication.IsSameSession(Key(SportFamily.Run), Key(SportFamily.Ride)));
    }

    [Theory]
    [InlineData(3600, 3960, true)]
    [InlineData(3600, 4000, true)]  // difference is exactly 10 % of the longer one
    [InlineData(3600, 3300, true)]
    [InlineData(3600, 4100, false)]
    [InlineData(3600, 1800, false)]
    public void Durations_must_be_within_10_percent(int a, int b, bool expected)
    {
        Assert.Equal(expected, SessionDeduplication.IsSameSession(Key(seconds: a), Key(seconds: b)));
    }

    [Fact]
    public void Zero_durations_match_only_each_other()
    {
        Assert.True(SessionDeduplication.IsSameSession(Key(seconds: 0), Key(seconds: 0)));
        Assert.False(SessionDeduplication.IsSameSession(Key(seconds: 0), Key(seconds: 60)));
    }

    [Fact]
    public void Matching_is_symmetric()
    {
        var a = Key(startOffsetMinutes: 3, seconds: 3500);
        var b = Key(seconds: 3800);

        Assert.Equal(SessionDeduplication.IsSameSession(a, b), SessionDeduplication.IsSameSession(b, a));
    }
}
