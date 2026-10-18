using ClosedXML.Excel;
using TrainingCoach.Domain;
using TrainingCoach.Domain.Planning;

namespace TrainingCoach.Infrastructure.PlanImport;

/// <summary>
/// One-off import of the training plan workbook (SPEC 5.4): sheet <c>Dagschema</c> for the sessions,
/// sheet <c>Polar trainingen</c> for the phased workout definitions. The app is the source of truth afterwards.
/// </summary>
public static class ExcelPlanImporter
{
    private const string DaySheet = "Dagschema";
    private const string WorkoutSheet = "Polar trainingen";
    private const string NoWorkout = "—";

    public static TrainingPlan Import(string path)
    {
        // Share-read so the import also works while the workbook is open in Excel.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Import(stream);
    }

    public static TrainingPlan Import(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var workouts = ReadWorkouts(workbook.Worksheet(WorkoutSheet));
        var sessions = ReadSessions(workbook.Worksheet(DaySheet), workouts);
        return new TrainingPlan(sessions, workouts);
    }

    private static List<PlannedSession> ReadSessions(IXLWorksheet sheet, IReadOnlyDictionary<string, WorkoutTemplate> workouts)
    {
        var sessions = new List<PlannedSession>();
        foreach (var row in sheet.RowsUsed().Skip(1)) // header
        {
            if (row.Cell(1).IsEmpty()) continue;

            var date = DateOnly.FromDateTime(row.Cell(1).GetDateTime());
            var week = (int)row.Cell(3).GetDouble();
            var phase = row.Cell(4).GetString().Trim();
            var sportText = row.Cell(5).GetString();
            var favourite = row.Cell(6).GetString().Trim();
            var description = row.Cell(7).GetString().Trim();
            var runKm = Optional(row.Cell(8));
            var rideKm = Optional(row.Cell(9));

            foreach (var sport in PlanTextParser.ParseSports(sportText))
            {
                sessions.Add(sport switch
                {
                    SportFamily.Run => Run(),
                    SportFamily.Ride => Ride(),
                    _ => Strength(),
                });
            }

            PlannedSession Run()
            {
                var workout = favourite is "" or NoWorkout ? null : favourite;
                if (workout is not null && !workouts.ContainsKey(workout))
                    throw new InvalidDataException($"{date}: workout '{workout}' has no definition in '{WorkoutSheet}'.");
                var text = SplitCombined(description).Run;
                return new(date, SportFamily.Run, workout ?? "Hardlopen", text, workout, runKm, week, phase, IsCommute: false);
            }

            PlannedSession Ride()
            {
                var commute = description.StartsWith("Pendel", StringComparison.OrdinalIgnoreCase);
                return new(date, SportFamily.Ride, commute ? "Pendel Aalter–Gent" : "Fietsen", description, null, rideKm, week, phase, commute);
            }

            PlannedSession Strength() =>
                new(date, SportFamily.Strength, "Krachtroutine", SplitCombined(description).Strength, null, null, week, phase, IsCommute: false);
        }
        return sessions;
    }

    /// <summary>"Run text  |  Daarna krachtroutine (±20')" → both parts; plain text is the strength text for strength-only days.</summary>
    private static (string Run, string Strength) SplitCombined(string description)
    {
        var parts = description.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return (description, description);

        var strength = parts[1].StartsWith("Daarna ", StringComparison.OrdinalIgnoreCase) ? parts[1]["Daarna ".Length..] : parts[1];
        return (parts[0], char.ToUpperInvariant(strength[0]) + strength[1..]);
    }

    private static double? Optional(IXLCell cell) =>
        cell.IsEmpty() ? null : cell.GetDouble();

    private static Dictionary<string, WorkoutTemplate> ReadWorkouts(IXLWorksheet sheet)
    {
        var templates = new Dictionary<string, WorkoutTemplate>();
        string? name = null;
        double totalKm = 0;
        var phases = new List<WorkoutPhase>();

        void Flush()
        {
            if (name is not null) templates[name] = new WorkoutTemplate(name, totalKm, phases.ToList());
            phases.Clear();
        }

        foreach (var row in sheet.RowsUsed())
        {
            if (row.Cell(4).GetString().StartsWith("Totaal", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                name = row.Cell(1).GetString().Trim();
                totalKm = row.Cell(5).GetDouble();
            }
            else if (name is not null && row.Cell(1).TryGetValue<int>(out var number) && row.Cell(1).DataType == XLDataType.Number)
            {
                phases.Add(ReadPhase(row, number));
            }
        }
        Flush();
        return templates;
    }

    private static WorkoutPhase ReadPhase(IXLRow row, int number)
    {
        var phaseName = row.Cell(2).GetString().Trim();
        var type = row.Cell(3).GetString().Trim();
        var amount = row.Cell(4).GetString().Trim();
        var target = row.Cell(5).GetString().Trim();
        var notes = row.Cell(6).GetString().Trim();

        return type switch
        {
            "Afstand" => new(number, phaseName, PhaseKind.Distance, PlanTextParser.ParseKilometres(amount), null, null, null, null, target, notes),
            "Tijd" => new(number, phaseName, PhaseKind.Time, null, PlanTextParser.ParseDuration(amount), null, null, null, target, notes),
            "Herhaling" => ReadRepeat(),
            _ => throw new InvalidDataException($"Unknown phase type '{type}' in phase {number} ('{phaseName}')."),
        };

        WorkoutPhase ReadRepeat()
        {
            var (from, to) = PlanTextParser.ParseRepeatRange(notes);
            return new(number, phaseName, PhaseKind.Repeat, null, null, PlanTextParser.ParseRepeats(amount), from, to, target, notes);
        }
    }
}
