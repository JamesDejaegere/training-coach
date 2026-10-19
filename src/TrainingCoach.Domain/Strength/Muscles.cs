namespace TrainingCoach.Domain.Strength;

/// <summary>
/// Muscle groups. The taxonomy follows free-exercise-db (Unlicense) plus hip flexors, which running loads (SPEC 7.3, D23).
/// </summary>
public enum Muscle
{
    Calves,
    Hamstrings,
    Quadriceps,
    Glutes,
    HipFlexors,
    Abductors,
    Adductors,
    Abdominals,
    LowerBack,
    MiddleBack,
    Lats,
    Traps,
    Chest,
    Shoulders,
    Biceps,
    Triceps,
    Forearms,
}

public static class MuscleNames
{
    public static string Dutch(Muscle muscle) => muscle switch
    {
        Muscle.Calves => "Kuiten",
        Muscle.Hamstrings => "Hamstrings",
        Muscle.Quadriceps => "Quadriceps",
        Muscle.Glutes => "Bilspieren",
        Muscle.HipFlexors => "Heupflexoren",
        Muscle.Abductors => "Heupabductoren",
        Muscle.Adductors => "Adductoren",
        Muscle.Abdominals => "Buikspieren",
        Muscle.LowerBack => "Onderrug",
        Muscle.MiddleBack => "Middenrug",
        Muscle.Lats => "Brede rugspieren",
        Muscle.Traps => "Trapezius",
        Muscle.Chest => "Borst",
        Muscle.Shoulders => "Schouders",
        Muscle.Biceps => "Biceps",
        Muscle.Triceps => "Triceps",
        Muscle.Forearms => "Onderarmen",
        _ => muscle.ToString(),
    };
}

/// <summary>Home equipment (SPEC 9, D22); extensible later (bands, dumbbells, gym).</summary>
public enum Equipment
{
    Bodyweight,
    AbWheel,
    Stairs,
    Chair,
    Towel,
    Wall,
}

public static class EquipmentNames
{
    public static string Dutch(Equipment equipment) => equipment switch
    {
        Equipment.Bodyweight => "Lichaamsgewicht",
        Equipment.AbWheel => "Ab wheel",
        Equipment.Stairs => "Trap / opstapje",
        Equipment.Chair => "Stoel / bank",
        Equipment.Towel => "Handdoek",
        Equipment.Wall => "Muur",
        _ => equipment.ToString(),
    };
}

public enum MovementPattern
{
    Calf,
    Hamstring,
    Squat,
    Hip,
    Core,
    Push,
    Back,
}

public static class PatternNames
{
    public static string Dutch(MovementPattern pattern) => pattern switch
    {
        MovementPattern.Calf => "Kuit",
        MovementPattern.Hamstring => "Hamstring",
        MovementPattern.Squat => "Squat / been",
        MovementPattern.Hip => "Heup",
        MovementPattern.Core => "Romp",
        MovementPattern.Push => "Duwen",
        MovementPattern.Back => "Rug",
        _ => pattern.ToString(),
    };
}

public enum ExerciseLevel
{
    Beginner,
    Intermediate,
    Advanced,
}

public enum SetSide
{
    None,
    Left,
    Right,
}

/// <summary>What the athlete did in one set: reps, or seconds for timed exercises.</summary>
public readonly record struct SetResult(int Reps, int DurationSeconds, bool Completed, SetSide Side);
