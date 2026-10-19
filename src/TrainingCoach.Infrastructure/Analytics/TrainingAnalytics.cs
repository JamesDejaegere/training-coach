using Microsoft.EntityFrameworkCore;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Calculations;
using TrainingCoach.Domain.Planning;
using TrainingCoach.Domain.Warnings;
using TrainingCoach.Infrastructure.Persistence;
using TrainingCoach.Infrastructure.Polar;

namespace TrainingCoach.Infrastructure.Analytics;

public sealed record AthleteProfile(int HrMax, int HrRest, GeoPoint? Home, GeoPoint? Work, double CommuteRadiusMeters)
{
    public LoadContext LoadContext => new(HrMax, HrRest);
}

public sealed record WeekVolume(DateOnly WeekStart, double RunKm, double RideKm, double StrengthMinutes, double TotalHours, double Load);

public sealed record PlanItem(StoredSession Session, IReadOnlyList<ActivityEntity> Activities)
{
    public bool IsDone => Activities.Count > 0;
}

public sealed record PlanRangeView(
    IReadOnlyList<PlanItem> Items,
    IReadOnlyList<ActivityEntity> Extra,
    IReadOnlyList<ActivityEntity> Commutes);

/// <summary>Composes the pure domain calculations with the stored data: what the screens show (SPEC 7, 8).</summary>
public sealed class TrainingAnalytics(AppDbContext db, PlanStore plan, TimeProvider time)
{
    public const int DefaultHrMax = 168;
    public const int DefaultHrRest = 55;
    private const int RestingHrWindowDays = 30;
    private const int HrvHistoryDays = 60;
    private const int MinHrvHistory = 7;

    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    // ---- profile ----

    public async Task<AthleteProfile> ProfileAsync(int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var settings = await db.AthleteSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, ct);

        var since = Today.AddDays(-RestingHrWindowDays);
        var resting = await db.RecoveryDays.AsNoTracking()
            .Where(r => r.UserId == userId && r.Date >= since && r.Date <= Today && r.RestingHeartRate != null)
            .Select(r => r.RestingHeartRate!.Value)
            .ToListAsync(ct);

        var hrRest = settings?.RestingHrOverride ?? (resting.Count > 0 ? (int)Math.Round(Median(resting)) : DefaultHrRest);

        GeoPoint? home = settings is { HomeLatitude: { } hLat, HomeLongitude: { } hLon } ? new GeoPoint(hLat, hLon) : null;
        GeoPoint? work = settings is { WorkLatitude: { } wLat, WorkLongitude: { } wLon } ? new GeoPoint(wLat, wLon) : null;
        return new AthleteProfile(settings?.HrMax ?? DefaultHrMax, hrRest, home, work, settings?.CommuteRadiusMeters ?? CommuteDetector.DefaultRadiusMeters);
    }

    // ---- per-activity rules ----

    /// <summary>Own detection from home/work locations when known (SPEC 7.4); otherwise Strava's commute flag. Rides only.</summary>
    public static bool IsCommute(ActivityEntity a, AthleteProfile profile)
    {
        if (a.Sport != SportFamily.Ride) return false;

        if (profile.Home is { } home && profile.Work is { } work &&
            a is { StartLatitude: { } sLat, StartLongitude: { } sLon, EndLatitude: { } eLat, EndLongitude: { } eLon })
        {
            return CommuteDetector.IsCommute(new GeoPoint(sLat, sLon), new GeoPoint(eLat, eLon), home, work, profile.CommuteRadiusMeters);
        }

        return a.StravaCommuteFlag;
    }

    /// <summary>Best available load source for one activity (SPEC 7.1).</summary>
    public static LoadResult LoadOf(ActivityEntity a, AthleteProfile profile)
    {
        var seconds = a.MovingSeconds > 0 ? a.MovingSeconds : a.ElapsedSeconds;
        var samples = PolarMapper.ParseSamples(a.HrSamples);
        return LoadEstimator.Estimate(
            profile.LoadContext, a.Sport, seconds / 60.0,
            samples.Count > 0 ? samples : null, a.HrSampleIntervalSeconds ?? 1, a.AverageHeartRate, a.Rpe);
    }

    // ---- load, fitness, volume ----

    public async Task<IReadOnlyDictionary<DateOnly, double>> DailyLoadsAsync(DateOnly from, DateOnly to, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var profile = await ProfileAsync(userId, ct);
        var activities = await ActivitiesAsync(from, to, userId, ct);
        return activities
            .GroupBy(a => DateOnly.FromDateTime(a.StartLocal))
            .ToDictionary(g => g.Key, g => g.Sum(a => LoadOf(a, profile).Value));
    }

    /// <summary>Fitness / fatigue / form for the range; history before the range is included in the weighting.</summary>
    public async Task<IReadOnlyList<DailyFitness>> FitnessAsync(DateOnly from, DateOnly to, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var earliest = await db.Activities.Where(a => a.UserId == userId).MinAsync(a => (DateTime?)a.StartLocal, ct);
        var start = earliest is { } e && DateOnly.FromDateTime(e) < from ? DateOnly.FromDateTime(e) : from;

        var loads = await DailyLoadsAsync(start, to, userId, ct);
        return FitnessModel.Compute(loads, start, to).Where(d => d.Date >= from).ToList();
    }

    public async Task<IReadOnlyList<WeekVolume>> WeeklyVolumesAsync(DateOnly from, DateOnly to, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var firstWeek = FitnessModel.WeekStart(from);
        var lastWeek = FitnessModel.WeekStart(to);
        var profile = await ProfileAsync(userId, ct);
        var activities = await ActivitiesAsync(firstWeek, lastWeek.AddDays(6), userId, ct);

        var weeks = new List<WeekVolume>();
        for (var week = firstWeek; week <= lastWeek; week = week.AddDays(7))
        {
            var inWeek = activities.Where(a => FitnessModel.WeekStart(DateOnly.FromDateTime(a.StartLocal)) == week).ToList();
            weeks.Add(new WeekVolume(
                week,
                inWeek.Where(a => a.Sport == SportFamily.Run).Sum(a => a.DistanceMeters) / 1000,
                inWeek.Where(a => a.Sport == SportFamily.Ride).Sum(a => a.DistanceMeters) / 1000,
                inWeek.Where(a => a.Sport == SportFamily.Strength).Sum(a => a.MovingSeconds) / 60.0,
                inWeek.Sum(a => a.MovingSeconds) / 3600.0,
                inWeek.Sum(a => LoadOf(a, profile).Value)));
        }
        return weeks;
    }

    // ---- plan vs actual ----

    public async Task<PlanRangeView> PlanViewAsync(DateOnly from, DateOnly to, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var sessions = await plan.GetSessionsAsync(from, to, userId, ct);
        var activities = await ActivitiesAsync(from, to, userId, ct);
        var profile = await ProfileAsync(userId, ct);

        var plannedRefs = sessions
            .Where(s => s.Status != PlannedStatus.Skipped)
            .Select(s => new PlannedSessionRef(s.Id, s.Session.Date, s.Session.Sport, s.Session.PlannedDistanceKm, s.Session.IsCommute))
            .ToList();
        var activityRefs = activities
            .Select(a => new ActivityRef(a.Id, DateOnly.FromDateTime(a.StartLocal), a.Sport, a.DistanceMeters > 0 ? a.DistanceMeters / 1000 : null, IsCommute(a, profile)))
            .ToList();

        var result = PlanMatcher.Match(plannedRefs, activityRefs);
        var byId = activities.ToDictionary(a => a.Id);

        var items = sessions
            .Select(s => new PlanItem(
                s,
                result.Matches.Where(m => m.PlannedSessionId == s.Id).Select(m => byId[m.ActivityId]).OrderBy(a => a.StartUtc).ToList()))
            .ToList();

        return new PlanRangeView(
            items,
            result.ExtraActivityIds.Select(id => byId[id]).OrderBy(a => a.StartUtc).ToList(),
            result.CommuteActivityIds.Select(id => byId[id]).OrderBy(a => a.StartUtc).ToList());
    }

    // ---- warnings ----

    public async Task<IReadOnlyList<TrainingWarning>> WarningsAsync(DateOnly date, int userId = UserIds.Owner, WarningThresholds? thresholds = null, CancellationToken ct = default)
    {
        thresholds ??= WarningThresholds.Default;
        var warnings = new List<TrainingWarning?>();

        // Build-up: running volume this week to date vs last full week, and acute:chronic ratio.
        var weekStart = FitnessModel.WeekStart(date);
        var runKmThisWeek = await RunKmAsync(weekStart, date, userId, ct);
        var runKmLastWeek = await RunKmAsync(weekStart.AddDays(-7), weekStart.AddDays(-1), userId, ct);
        var loads = await DailyLoadsAsync(date.AddDays(-27), date, userId, ct);
        warnings.Add(WarningRules.BuildUp(date, runKmThisWeek, runKmLastWeek, FitnessModel.AcuteChronicRatio(loads, date), thresholds));

        warnings.Add(await RecoveryWarningAsync(date, userId, thresholds, ct));

        var painSince = date.AddDays(-thresholds.PainWindowDays);
        var pain = await db.PainReports.AsNoTracking()
            .Where(p => p.UserId == userId && p.Date >= painSince && p.Date <= date)
            .ToListAsync(ct);
        warnings.Add(WarningRules.Pain(date, pain.Select(p => new PainReport(p.Date, p.Location)), thresholds));

        return warnings.OfType<TrainingWarning>().ToList();
    }

    private async Task<TrainingWarning?> RecoveryWarningAsync(DateOnly date, int userId, WarningThresholds thresholds, CancellationToken ct)
    {
        var since = date.AddDays(-HrvHistoryDays - thresholds.LowHrvConsecutiveNights);
        var nights = await db.RecoveryDays.AsNoTracking()
            .Where(r => r.UserId == userId && r.Date >= since && r.Date <= date)
            .OrderBy(r => r.Date)
            .ToListAsync(ct);

        var recent = nights.Where(n => n.Hrv != null).TakeLast(thresholds.LowHrvConsecutiveNights).ToList();
        var history = nights.Where(n => n.Hrv != null && !recent.Contains(n)).Select(n => n.Hrv!.Value).ToList();
        if (recent.Count < thresholds.LowHrvConsecutiveNights || history.Count < MinHrvHistory) return null;

        var restingHistory = nights.Where(n => n.RestingHeartRate != null && !recent.Contains(n)).Select(n => n.RestingHeartRate!.Value).ToList();
        var latestResting = nights.LastOrDefault(n => n.RestingHeartRate != null)?.RestingHeartRate;
        var baseline = restingHistory.Count > 0 ? Median(restingHistory) : double.NaN;
        var restingHr = latestResting ?? double.NaN;
        var restingKnown = !double.IsNaN(baseline) && !double.IsNaN(restingHr);

        var hardPlanned = await HardSessionPlannedAsync(date, userId, ct);

        return WarningRules.Recovery(
            date,
            recent.Select(n => n.Hrv!.Value).ToList(),
            HrvBand.FromHistory(history),
            restingKnown ? restingHr : 0,
            restingKnown ? baseline : 0,
            hardPlanned,
            thresholds);
    }

    /// <summary>A quality session (tempo, intervals, race pace) planned today or tomorrow.</summary>
    private async Task<bool> HardSessionPlannedAsync(DateOnly date, int userId, CancellationToken ct)
    {
        var sessions = await plan.GetSessionsAsync(date, date.AddDays(1), userId, ct);
        foreach (var s in sessions.Where(s => s.Status != PlannedStatus.Skipped && s.Session.WorkoutName is not null))
        {
            var workout = await plan.GetWorkoutAsync(s.Session.WorkoutName!, userId, ct);
            if (workout is { IsQuality: true }) return true;
        }
        return false;
    }

    // ---- helpers ----

    private async Task<double> RunKmAsync(DateOnly from, DateOnly to, int userId, CancellationToken ct)
    {
        var runs = await ActivitiesAsync(from, to, userId, ct);
        return runs.Where(a => a.Sport == SportFamily.Run).Sum(a => a.DistanceMeters) / 1000;
    }

    private async Task<List<ActivityEntity>> ActivitiesAsync(DateOnly from, DateOnly to, int userId, CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return await db.Activities.AsNoTracking()
            .Where(a => a.UserId == userId && a.StartLocal >= start && a.StartLocal < end)
            .OrderBy(a => a.StartUtc)
            .ToListAsync(ct);
    }

    private static double Median(IReadOnlyCollection<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
