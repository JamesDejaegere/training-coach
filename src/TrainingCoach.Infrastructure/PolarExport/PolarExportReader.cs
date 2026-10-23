using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml;

namespace TrainingCoach.Infrastructure.PolarExport;

/// <summary>One training session of the Polar Flow export (<c>training-session_*.json</c>).</summary>
public sealed record ExportSession(
    string ExerciseId,
    DateTime StartLocal,
    int? OffsetMinutes,
    int DurationSeconds,
    double DistanceMeters,
    string? SportId,
    int? HrAvg,
    int? HrMax,
    double? CardioLoad,
    IReadOnlyList<double>? HrSamples,
    int HrIntervalMillis,
    string Name);

/// <summary>One night of <c>nightly_recovery_*.json</c>.</summary>
public sealed record ExportNight(DateOnly Date, double? Rmssd, double? RestingHr, double? AnsCharge, int? AnsStatus, int? RecoveryIndicator);

/// <summary>One night of <c>sleep_result_*.json</c> / <c>sleep_score_*.json</c>.</summary>
public sealed record ExportSleep(DateOnly Date, double? Score, int? LightSeconds, int? DeepSeconds, int? RemSeconds);

public enum ExportEntryKind
{
    Ignored,
    Session,
    NightlyRecovery,
    SleepResult,
    SleepScore,
}

/// <summary>
/// Reads the Polar Flow "Download your data" export (format verified against a real export, version 2.7).
/// Entries are read straight from the zip, never extracted to disk; names and sizes are untrusted.
/// </summary>
public static class PolarExportReader
{
    /// <summary>Largest JSON entry we are willing to parse (a year of sleep data is ~13 MB).</summary>
    public const long MaxEntryBytes = 256L * 1024 * 1024;

    private const string HeartRateType = "HEART_RATE";

    public static ExportEntryKind Classify(ZipArchiveEntry entry)
    {
        var name = entry.FullName;
        // The real export is flat; anything with a path (or a traversal attempt) is not ours.
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..") || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return ExportEntryKind.Ignored;
        if (entry.Length > MaxEntryBytes) return ExportEntryKind.Ignored;

        if (name.StartsWith("training-session_", StringComparison.Ordinal)) return ExportEntryKind.Session;
        if (name.StartsWith("nightly_recovery_blob", StringComparison.Ordinal)) return ExportEntryKind.Ignored; // raw HRV samples, not needed
        if (name.StartsWith("nightly_recovery_", StringComparison.Ordinal)) return ExportEntryKind.NightlyRecovery;
        if (name.StartsWith("sleep_result_", StringComparison.Ordinal)) return ExportEntryKind.SleepResult;
        if (name.StartsWith("sleep_score_", StringComparison.Ordinal)) return ExportEntryKind.SleepScore;
        return ExportEntryKind.Ignored;
    }

    /// <summary>Throws <see cref="FormatException"/> or <see cref="JsonException"/> when the entry is not a usable session.</summary>
    public static ExportSession ParseSession(ZipArchiveEntry entry)
    {
        using var doc = Open(entry);
        var root = doc.RootElement;

        var exercise = root.TryGetProperty("exercises", out var exercises) && exercises.ValueKind == JsonValueKind.Array && exercises.GetArrayLength() > 0
            ? exercises[0]
            : throw new FormatException("Session has no exercise.");

        var start = ReadDateTime(root, "startTime") ?? throw new FormatException("Session has no start time.");
        var id = exercise.TryGetProperty("identifier", out var identifier) && identifier.TryGetProperty("id", out var idElement)
            ? idElement.ToString()
            : throw new FormatException("Session has no id.");

        var duration = (int)Math.Round(ReadDouble(root, "durationMillis") is { } ms ? ms / 1000 : (ReadDouble(exercise, "durationMillis") ?? 0) / 1000);
        var sportId = (root.TryGetProperty("sport", out var sport) ? sport : exercise.TryGetProperty("sport", out var exSport) ? exSport : default) is { ValueKind: JsonValueKind.Object } s
            && s.TryGetProperty("id", out var sid) ? sid.ToString() : null;

        var (samples, interval) = ReadHeartRate(exercise);
        double? cardio = root.TryGetProperty("trainingLoadReport", out var report) ? ReadDouble(report, "cardioLoad") : null;

        return new ExportSession(
            id,
            start,
            ReadInt(root, "timezoneOffsetMinutes") ?? ReadInt(exercise, "timezoneOffsetMinutes"),
            duration,
            ReadDouble(root, "distanceMeters") ?? ReadDouble(exercise, "distanceMeters") ?? 0,
            sportId,
            ReadInt(root, "hrAvg"),
            ReadInt(root, "hrMax"),
            cardio,
            samples,
            interval,
            root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "");
    }

    public static IReadOnlyList<ExportNight> ParseNightlyRecovery(ZipArchiveEntry entry)
    {
        using var doc = Open(entry);
        var nights = new List<ExportNight>();
        foreach (var n in Array(doc))
        {
            if (ReadDate(n, "night") is not { } date) continue;

            var rri = ReadDouble(n, "meanNightlyRecoveryRri");
            nights.Add(new ExportNight(
                date,
                ReadDouble(n, "meanNightlyRecoveryRmssd"),
                rri is > 0 ? Math.Round(60000.0 / rri.Value) : null,
                ReadDouble(n, "ansStatus") is { } charge ? Math.Round(charge, 1) : null, // the -10..10 ANS charge (AccessLink: ans_charge)
                ReadInt(n, "ansRate"),                                                     // the 1-5 status (AccessLink: ans_charge_status)
                ReadInt(n, "recoveryIndicator")));                                         // AccessLink: nightly_recharge_status
        }
        return nights;
    }

    public static IReadOnlyList<ExportSleep> ParseSleepResult(ZipArchiveEntry entry)
    {
        using var doc = Open(entry);
        var nights = new List<ExportSleep>();
        foreach (var n in Array(doc))
        {
            if (ReadDate(n, "night") is not { } date) continue;
            if (!n.TryGetProperty("evaluation", out var evaluation) || !evaluation.TryGetProperty("phaseDurations", out var phases)) continue;

            nights.Add(new ExportSleep(date, null, ReadIsoSeconds(phases, "light"), ReadIsoSeconds(phases, "deep"), ReadIsoSeconds(phases, "rem")));
        }
        return nights;
    }

    public static IReadOnlyList<ExportSleep> ParseSleepScore(ZipArchiveEntry entry)
    {
        using var doc = Open(entry);
        var nights = new List<ExportSleep>();
        foreach (var n in Array(doc))
        {
            if (ReadDate(n, "night") is not { } date) continue;
            var score = n.TryGetProperty("sleepScoreResult", out var result) ? ReadDouble(result, "sleepScore") : null;
            nights.Add(new ExportSleep(date, score, null, null, null));
        }
        return nights;
    }

    // ---- helpers ----

    private static JsonDocument Open(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static IEnumerable<JsonElement> Array(JsonDocument doc) =>
        doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [];

    private static (IReadOnlyList<double>? Samples, int IntervalMillis) ReadHeartRate(JsonElement exercise)
    {
        if (!exercise.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Object ||
            !samples.TryGetProperty("samples", out var list) || list.ValueKind != JsonValueKind.Array)
            return (null, 1000);

        foreach (var s in list.EnumerateArray())
        {
            if (!s.TryGetProperty("type", out var type) || type.GetString() != HeartRateType) continue;
            if (!s.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) continue;

            var parsed = values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDouble()).ToList();
            var interval = ReadInt(s, "intervalMillis") is > 0 and var i ? i : 1000;
            return (parsed.Count > 0 ? parsed : null, interval);
        }
        return (null, 1000);
    }

    private static DateTime? ReadDateTime(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(p.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified)
            : null;

    private static DateOnly? ReadDate(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String &&
        DateOnly.TryParseExact(p.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    private static double? ReadDouble(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    private static int? ReadInt(JsonElement e, string name) => ReadDouble(e, name) is { } d ? (int)Math.Round(d) : null;

    private static int? ReadIsoSeconds(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return null;
        try { return (int)Math.Round(XmlConvert.ToTimeSpan(p.GetString()!).TotalSeconds); }
        catch (FormatException) { return null; }
    }
}
