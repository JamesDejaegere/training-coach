using TrainingCoach.Domain;
using TrainingCoach.Domain.Planning;

namespace TrainingCoach.Domain.Tests.Planning;

public class PlanMatcherTests
{
    private static readonly DateOnly Day = new(2027, 3, 2);

    private static PlannedSessionRef Plan(int id, SportFamily sport, double? km = null, DateOnly? date = null) =>
        new(id, date ?? Day, sport, km);

    private static ActivityRef Act(int id, SportFamily sport, double? km = null, bool commute = false, DateOnly? date = null) =>
        new(id, date ?? Day, sport, km, commute);

    [Fact]
    public void Same_date_and_sport_matches()
    {
        var result = PlanMatcher.Match([Plan(1, SportFamily.Run, 10)], [Act(100, SportFamily.Run, 9.8)]);

        Assert.Equal(new[] { new PlanMatch(1, 100) }, result.Matches);
        Assert.Empty(result.ExtraActivityIds);
        Assert.Empty(result.UnmatchedPlannedIds);
    }

    [Fact]
    public void Different_date_or_sport_does_not_match()
    {
        var result = PlanMatcher.Match(
            [Plan(1, SportFamily.Run, 10)],
            [Act(100, SportFamily.Ride, 10), Act(101, SportFamily.Run, 10, date: Day.AddDays(1))]);

        Assert.Empty(result.Matches);
        Assert.Equal(new[] { 100, 101 }, result.ExtraActivityIds.Order());
        Assert.Equal(new[] { 1 }, result.UnmatchedPlannedIds);
    }

    [Fact]
    public void Multiple_candidates_pick_the_closest_planned_distance()
    {
        var result = PlanMatcher.Match(
            [Plan(1, SportFamily.Run, 10)],
            [Act(100, SportFamily.Run, 5), Act(101, SportFamily.Run, 9.5), Act(102, SportFamily.Run, 14)]);

        Assert.Equal(new[] { new PlanMatch(1, 101) }, result.Matches);
        Assert.Equal(new[] { 100, 102 }, result.ExtraActivityIds.Order());
    }

    [Fact]
    public void Each_activity_is_used_at_most_once_and_best_overall_pairing_wins()
    {
        // Plans 5 km and 10 km, activities 5.1 km and 9.8 km: must pair 5<->5.1 and 10<->9.8.
        var result = PlanMatcher.Match(
            [Plan(1, SportFamily.Run, 10), Plan(2, SportFamily.Run, 5)],
            [Act(100, SportFamily.Run, 5.1), Act(101, SportFamily.Run, 9.8)]);

        Assert.Contains(new PlanMatch(1, 101), result.Matches);
        Assert.Contains(new PlanMatch(2, 100), result.Matches);
    }

    [Fact]
    public void Commutes_are_never_matched_and_are_not_reported_as_extra()
    {
        var result = PlanMatcher.Match([Plan(1, SportFamily.Ride, 60)], [Act(100, SportFamily.Ride, 30, commute: true)]);

        Assert.Empty(result.Matches);
        Assert.Empty(result.ExtraActivityIds);
        Assert.Equal(new[] { 100 }, result.CommuteActivityIds);
        Assert.Equal(new[] { 1 }, result.UnmatchedPlannedIds);
    }

    [Fact]
    public void Missing_distances_still_match_on_date_and_sport()
    {
        var result = PlanMatcher.Match([Plan(1, SportFamily.Strength)], [Act(100, SportFamily.Strength)]);

        Assert.Equal(new[] { new PlanMatch(1, 100) }, result.Matches);
    }
}
