namespace TrainingCoach.Domain.Planning;

public readonly record struct PlannedSessionRef(int Id, DateOnly Date, SportFamily Sport, double? PlannedDistanceKm, bool IsCommute = false);

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
    /// <remarks>
    /// Commute activities (both legs) attach to the planned commute session of the same day; without one they are
    /// reported as commutes, never as extra. Other activities match on date + sport, closest planned distance first.
    /// A planned commute session still unmatched can be taken by a plain ride (e.g. "no commute? ride 45-60 min").
    /// </remarks>
    public static PlanMatchResult Match(IReadOnlyCollection<PlannedSessionRef> planned, IReadOnlyCollection<ActivityRef> activities)
    {
        var usedPlanned = new HashSet<int>();
        var usedActivities = new HashSet<int>();
        var matches = new List<PlanMatch>();

        // 1. Commute activities -> planned commute session of that day (many-to-one: heen + terug).
        var commutes = activities.Where(a => a.IsCommute).ToList();
        var unattachedCommutes = new List<int>();
        foreach (var commute in commutes)
        {
            PlannedSessionRef? target = planned
                .Where(p => p.IsCommute && p.Date == commute.Date && p.Sport == commute.Sport)
                .Cast<PlannedSessionRef?>()
                .FirstOrDefault();
            if (target is null)
            {
                unattachedCommutes.Add(commute.Id);
                continue;
            }
            usedPlanned.Add(target.Value.Id);
            usedActivities.Add(commute.Id);
            matches.Add(new PlanMatch(target.Value.Id, commute.Id));
        }

        // 2. Everything else: same date + same sport, closest planned distance first (greedy over all pairs).
        var candidates = activities.Where(a => !a.IsCommute).ToList();
        var pairs = (from p in planned
                     where !usedPlanned.Contains(p.Id)
                     from a in candidates
                     where p.Date == a.Date && p.Sport == a.Sport
                     orderby Gap(p, a), p.Id, a.Id
                     select new PlanMatch(p.Id, a.Id)).ToList();

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
            unattachedCommutes,
            planned.Where(p => !usedPlanned.Contains(p.Id)).Select(p => p.Id).ToList());
    }

    private static double Gap(PlannedSessionRef planned, ActivityRef activity) =>
        planned.PlannedDistanceKm is { } p && activity.DistanceKm is { } a ? Math.Abs(p - a) : 0;
}
