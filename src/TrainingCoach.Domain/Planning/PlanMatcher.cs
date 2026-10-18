namespace TrainingCoach.Domain.Planning;

public readonly record struct PlannedSessionRef(int Id, DateOnly Date, SportFamily Sport, double? PlannedDistanceKm);

public readonly record struct ActivityRef(int Id, DateOnly Date, SportFamily Sport, double? DistanceKm, bool IsCommute);

public readonly record struct PlanMatch(int PlannedSessionId, int ActivityId);

public sealed record PlanMatchResult(
    IReadOnlyList<PlanMatch> Matches,
    IReadOnlyList<int> ExtraActivityIds,
    IReadOnlyList<int> CommuteActivityIds,
    IReadOnlyList<int> UnmatchedPlannedIds);

/// <summary>Links activities to planned sessions (SPEC 7.4).</summary>
public static class PlanMatcher
{
    public static PlanMatchResult Match(IReadOnlyCollection<PlannedSessionRef> planned, IReadOnlyCollection<ActivityRef> activities)
    {
        var commutes = activities.Where(a => a.IsCommute).Select(a => a.Id).ToList();
        var candidates = activities.Where(a => !a.IsCommute).ToList();

        // Same date + same sport; closest planned distance first (greedy over all pairs gives the best overall pairing).
        var pairs = (from p in planned
                     from a in candidates
                     where p.Date == a.Date && p.Sport == a.Sport
                     orderby Gap(p, a), p.Id, a.Id
                     select new PlanMatch(p.Id, a.Id)).ToList();

        var usedPlanned = new HashSet<int>();
        var usedActivities = new HashSet<int>();
        var matches = new List<PlanMatch>();
        foreach (var pair in pairs)
        {
            if (usedPlanned.Contains(pair.PlannedSessionId) || usedActivities.Contains(pair.ActivityId)) continue;
            usedPlanned.Add(pair.PlannedSessionId);
            usedActivities.Add(pair.ActivityId);
            matches.Add(pair);
        }

        return new PlanMatchResult(
            matches,
            candidates.Where(a => !usedActivities.Contains(a.Id)).Select(a => a.Id).ToList(),
            commutes,
            planned.Where(p => !usedPlanned.Contains(p.Id)).Select(p => p.Id).ToList());
    }

    private static double Gap(PlannedSessionRef planned, ActivityRef activity) =>
        planned.PlannedDistanceKm is { } p && activity.DistanceKm is { } a ? Math.Abs(p - a) : 0;
}
