using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace TrainingCoach.Infrastructure.Tests.PolarExport;

/// <summary>Builds small zips with the same JSON shapes as a real "Download your data" export of Polar Flow.</summary>
internal static class PolarExportTestData
{
    public static string SessionJson(
        string exerciseId = "8300009631",
        string start = "2026-02-02T06:56:37",
        int? offsetMinutes = 60,
        int durationMillis = 2113027,
        string sportId = "1",
        double distance = 5599.7,
        int? hrAvg = 148,
        int? hrMax = 171,
        double? cardioLoad = 47.55,
        string? hrValues = "[100.0,101.0,102.0,103.0]",
        int hrIntervalMillis = 1000,
        string name = "")
    {
        var samples = hrValues is null
            ? ""
            : $$"""
              ,"samples": { "samples": [
                { "type": "DISTANCE", "intervalMillis": 1000, "values": [0.0, 2.5] },
                { "type": "HEART_RATE", "intervalMillis": {{hrIntervalMillis}}, "values": {{hrValues}} }
              ], "rrSamples": [ { "durationMillis": 800 } ] }
              """;
        return $$"""
            {
              "identifier": { "id": "session-{{exerciseId}}" },
              "startTime": "{{start}}",
              "stopTime": "2026-02-02T07:31:50",
              "name": "{{name}}",
              "durationMillis": {{durationMillis}},
              "distanceMeters": {{distance.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
              {{(hrAvg is null ? "" : $"\"hrAvg\": {hrAvg},")}}
              {{(hrMax is null ? "" : $"\"hrMax\": {hrMax},")}}
              {{(offsetMinutes is null ? "" : $"\"timezoneOffsetMinutes\": {offsetMinutes},")}}
              "sport": { "id": "{{sportId}}" },
              "exercises": [ {
                "identifier": { "id": "{{exerciseId}}" },
                "startTime": "{{start}}",
                "durationMillis": {{durationMillis}},
                "sport": { "id": "{{sportId}}" }
                {{samples}}
              } ],
              "trainingLoadReport": { {{(cardioLoad is null ? "" : $"\"cardioLoad\": {cardioLoad.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},")}} "muscleLoad": 600.0, "sessionRpe": "RPE_BRISK" }
            }
            """;
    }

    public static string NightlyRecoveryJson(params (string Night, int? Rmssd, int? Rri, double? AnsStatus, int? AnsRate, int? Indicator)[] nights) =>
        "[" + string.Join(",", nights.Select(n => $$"""
            { "night": "{{n.Night}}", "sleepTip": "-1",
              {{(n.Rri is null ? "" : $"\"meanNightlyRecoveryRri\": {n.Rri},")}}
              {{(n.Rmssd is null ? "" : $"\"meanNightlyRecoveryRmssd\": {n.Rmssd},")}}
              {{(n.AnsStatus is null ? "" : $"\"ansStatus\": {n.AnsStatus.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},")}}
              {{(n.AnsRate is null ? "" : $"\"ansRate\": {n.AnsRate},")}}
              {{(n.Indicator is null ? "" : $"\"recoveryIndicator\": {n.Indicator},")}}
              "exerciseTip": "-1" }
            """)) + "]";

    public static string SleepResultJson(params (string Night, string Light, string Deep, string Rem)[] nights) =>
        "[" + string.Join(",", nights.Select(n => $$"""
            { "night": "{{n.Night}}", "evaluation": { "asleepDuration": "PT7H",
              "phaseDurations": { "wake": "PT10M", "rem": "{{n.Rem}}", "light": "{{n.Light}}", "deep": "{{n.Deep}}", "unknown": "PT0S" } } }
            """)) + "]";

    public static string SleepScoreJson(params (string Night, double Score)[] nights) =>
        "[" + string.Join(",", nights.Select(n => $$"""
            { "night": "{{n.Night}}", "sleepScoreResult": { "sleepScore": {{n.Score.ToString(System.Globalization.CultureInfo.InvariantCulture)}} } }
            """)) + "]";

    public static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    public static string SessionFile(string id, string json) => $"training-session_2026-02-02T06-56-37_{id}-00063f8b-93dd-48e9-a5be-3c91b92ff540.json";

    public static string Compact(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
}
