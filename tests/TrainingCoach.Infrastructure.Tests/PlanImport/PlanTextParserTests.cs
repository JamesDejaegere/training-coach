using TrainingCoach.Domain;
using TrainingCoach.Infrastructure.PlanImport;

namespace TrainingCoach.Infrastructure.Tests.PlanImport;

public class PlanTextParserTests
{
    [Theory]
    [InlineData("1,0 km", 1.0)]
    [InlineData("12,5 km", 12.5)]
    [InlineData("16,1 km", 16.1)]
    public void Parses_dutch_kilometres(string text, double expected)
    {
        Assert.Equal(expected, PlanTextParser.ParseKilometres(text));
    }

    [Theory]
    [InlineData("0:20", 20)]
    [InlineData("1:00", 60)]
    [InlineData("2:00", 120)]
    [InlineData("20:00", 1200)]
    [InlineData("0:15", 15)]
    public void Parses_minutes_and_seconds(string text, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), PlanTextParser.ParseDuration(text));
    }

    [Theory]
    [InlineData("6×", 6)]
    [InlineData("10×", 10)]
    public void Parses_repeat_counts(string text, int expected)
    {
        Assert.Equal(expected, PlanTextParser.ParseRepeats(text));
    }

    [Theory]
    [InlineData("Herhaal fasen 4–5 zes keer", 4, 5)]
    [InlineData("Herhaal fasen 3–4 (3×, laatste herstel mag weg)", 3, 4)]
    [InlineData("Herhaal fasen 3–4 vier keer", 3, 4)]
    public void Parses_repeat_range_from_note(string note, int from, int to)
    {
        Assert.Equal((from, to), PlanTextParser.ParseRepeatRange(note));
    }

    [Fact]
    public void Unparseable_values_throw_format_exception()
    {
        Assert.Throws<FormatException>(() => PlanTextParser.ParseKilometres("veel"));
        Assert.Throws<FormatException>(() => PlanTextParser.ParseDuration("kort"));
        Assert.Throws<FormatException>(() => PlanTextParser.ParseRepeatRange("geen bereik"));
    }

    [Theory]
    [InlineData("Lopen", new[] { SportFamily.Run })]
    [InlineData("Fietsen", new[] { SportFamily.Ride })]
    [InlineData("Kracht", new[] { SportFamily.Strength })]
    [InlineData("Lopen + kracht", new[] { SportFamily.Run, SportFamily.Strength })]
    [InlineData("Rust", new SportFamily[0])]
    public void Maps_plan_sport_to_sport_families(string text, SportFamily[] expected)
    {
        Assert.Equal(expected, PlanTextParser.ParseSports(text));
    }
}
