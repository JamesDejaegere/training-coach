using System.Globalization;
using System.Text.RegularExpressions;
using TrainingCoach.Domain;

namespace TrainingCoach.Infrastructure.PlanImport;

/// <summary>Parses the Dutch text conventions of the training-plan workbook.</summary>
public static partial class PlanTextParser
{
    /// <summary>"1,0 km" → 1.0</summary>
    public static double ParseKilometres(string text)
    {
        var number = text.Trim();
        if (number.EndsWith("km", StringComparison.OrdinalIgnoreCase)) number = number[..^2].Trim();

        return double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var km)
            ? km
            : throw new FormatException($"Not a distance: '{text}'");
    }

    /// <summary>"m:ss" → duration ("0:20" = 20 seconds, "20:00" = 20 minutes).</summary>
    public static TimeSpan ParseDuration(string text)
    {
        var m = DurationPattern().Match(text.Trim());
        return m.Success
            ? new TimeSpan(0, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))
            : throw new FormatException($"Not a duration (m:ss): '{text}'");
    }

    /// <summary>"6×" → 6</summary>
    public static int ParseRepeats(string text)
    {
        var m = RepeatsPattern().Match(text.Trim());
        return m.Success ? int.Parse(m.Groups[1].Value) : throw new FormatException($"Not a repeat count: '{text}'");
    }

    /// <summary>"Herhaal fasen 4–5 zes keer" → (4, 5)</summary>
    public static (int From, int To) ParseRepeatRange(string note)
    {
        var m = RangePattern().Match(note);
        return m.Success
            ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))
            : throw new FormatException($"No phase range in: '{note}'");
    }

    /// <summary>Plan sport column → the sessions it stands for ("Lopen + kracht" is two).</summary>
    public static IReadOnlyList<SportFamily> ParseSports(string text) => text.Trim().ToLowerInvariant() switch
    {
        "lopen" => [SportFamily.Run],
        "fietsen" => [SportFamily.Ride],
        "kracht" => [SportFamily.Strength],
        "lopen + kracht" => [SportFamily.Run, SportFamily.Strength],
        "rust" => [],
        _ => throw new FormatException($"Unknown sport: '{text}'"),
    };

    [GeneratedRegex(@"^(\d{1,3}):([0-5]\d)$")]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"^(\d+)\s*×$")]
    private static partial Regex RepeatsPattern();

    [GeneratedRegex(@"fasen\s+(\d+)\s*[–-]\s*(\d+)")]
    private static partial Regex RangePattern();
}
