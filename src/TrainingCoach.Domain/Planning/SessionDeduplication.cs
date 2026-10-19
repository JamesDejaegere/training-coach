namespace TrainingCoach.Domain.Planning;

/// <summary>What identifies a session across sources.</summary>
public readonly record struct SessionKey(SportFamily Sport, DateTime StartUtc, int DurationSeconds);

/// <summary>Recognises the same session arriving from Strava and Polar (SPEC 5.5).</summary>
public static class SessionDeduplication
{
    public static readonly TimeSpan MaxStartDifference = TimeSpan.FromMinutes(5);

    /// <summary>Same sport family, starts within 5 minutes and durations within 10 % of the longer one.</summary>
    public static bool IsSameSession(SessionKey a, SessionKey b)
    {
        if (a.Sport != b.Sport) return false;
        if ((a.StartUtc - b.StartUtc).Duration() > MaxStartDifference) return false;

        var longer = Math.Max(a.DurationSeconds, b.DurationSeconds);
        var difference = Math.Abs(a.DurationSeconds - b.DurationSeconds);
        return difference * 10L <= longer;
    }
}
