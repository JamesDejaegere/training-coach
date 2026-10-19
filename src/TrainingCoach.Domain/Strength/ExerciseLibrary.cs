namespace TrainingCoach.Domain.Strength;

/// <summary>
/// An exercise in the library. <see cref="Target"/> is the top of the rep range per set (or seconds for timed
/// exercises); reaching it on all <see cref="Sets"/> at RPE ≤ 7 suggests the next ladder step (SPEC 9).
/// </summary>
public sealed record ExerciseDefinition(
    string Key,
    string NameNl,
    string NameEn,
    MovementPattern Pattern,
    IReadOnlyList<Muscle> Primary,
    IReadOnlyList<Muscle> Secondary,
    IReadOnlyList<Equipment> Equipment,
    ExerciseLevel Level,
    bool Unilateral,
    bool Timed,
    int Sets,
    int Target,
    string? LadderKey,
    int? LadderStep,
    string InstructionsNl);

public sealed record ProgressionLadder(string Key, string NameNl, IReadOnlyList<string> Steps);

/// <summary>
/// Curated home exercise library with Dutch text (SPEC 9, D22/D23). Names and the muscle taxonomy follow
/// free-exercise-db (Unlicense); no photos are used: the app's own muscle map illustrates each exercise.
/// </summary>
public static class ExerciseLibrary
{
    public static readonly IReadOnlySet<Equipment> HomeEquipment = new HashSet<Equipment>
    {
        Equipment.Bodyweight, Equipment.AbWheel, Equipment.Stairs, Equipment.Chair, Equipment.Towel, Equipment.Wall,
    };

    public static readonly IReadOnlyList<ProgressionLadder> Ladders =
    [
        new("calf", "Kuit", ["calf-raise-two-leg", "calf-raise-single-leg", "calf-raise-single-leg-slow", "calf-raise-single-leg-pause"]),
        new("hamstring", "Hamstring", ["glute-bridge", "single-leg-bridge", "towel-hamstring-curl", "nordic-curl"]),
        new("squat", "Squat", ["bodyweight-squat", "split-squat", "bulgarian-split-squat", "skater-squat"]),
        new("core", "Romp", ["dead-bug", "plank", "ab-wheel-rollout"]),
        new("push", "Duwen", ["incline-push-up", "push-up", "pike-push-up"]),
    ];

    /// <summary>The strength routine from the training plan (±25 minutes, twice a week).</summary>
    public static readonly IReadOnlyList<string> Routine =
    [
        "calf-raise-single-leg-slow", "calf-raise-bent-knee", "single-leg-bridge", "bulgarian-split-squat", "side-lying-hip-abduction", "plank",
    ];

    public static ProgressionLadder Ladder(string key) => Ladders.Single(l => l.Key == key);

    public static IReadOnlyList<ExerciseDefinition> All { get; } = Build();

    public static IReadOnlyDictionary<string, ExerciseDefinition> ByKey { get; } = All.ToDictionary(e => e.Key);

    private static List<ExerciseDefinition> Build()
    {
        var list = new List<ExerciseDefinition>();

        void Add(string key, string nl, string en, MovementPattern pattern, Muscle[] primary, Muscle[] secondary, Equipment[] equipment,
            ExerciseLevel level, int sets, int target, string instructions, bool unilateral = false, bool timed = false)
        {
            var ladder = Ladders.FirstOrDefault(l => l.Steps.Contains(key));
            list.Add(new ExerciseDefinition(key, nl, en, pattern, primary, secondary, equipment, level, unilateral, timed, sets, target,
                ladder?.Key, ladder is null ? null : ladder.Steps.ToList().IndexOf(key), instructions));
        }

        const ExerciseLevel B = ExerciseLevel.Beginner, I = ExerciseLevel.Intermediate, A = ExerciseLevel.Advanced;
        Equipment[] body = [Equipment.Bodyweight], stairs = [Equipment.Stairs], chair = [Equipment.Chair];

        // ---- Kuit ----
        Add("calf-raise-two-leg", "Kuitheffen op twee benen", "Standing calf raise", MovementPattern.Calf,
            [Muscle.Calves], [], stairs, B, 3, 15,
            "Sta met de voorvoeten op de rand van een trede, hielen vrij. Kom omhoog op de tenen, houd even vast en zak gecontroleerd tot onder trede-niveau. Houd je evenwicht aan de muur of leuning.");
        Add("calf-raise-single-leg", "Kuitheffen op één been", "Single-leg calf raise", MovementPattern.Calf,
            [Muscle.Calves], [], stairs, I, 3, 15,
            "Sta op één been met de voorvoet op de rand van een trede. Kom volledig omhoog en zak rustig tot de hiel onder de trede komt. Doe alle herhalingen per been en wissel dan.", unilateral: true);
        Add("calf-raise-single-leg-slow", "Kuitheffen op één been, traag zakken", "Single-leg calf raise, slow eccentric", MovementPattern.Calf,
            [Muscle.Calves], [], stairs, I, 3, 12,
            "Kom op één been omhoog (of met hulp van het andere been) en zak dan in 3 tot 4 seconden zo traag mogelijk. Dit excentrische werk versterkt kuit en achillespees.", unilateral: true);
        Add("calf-raise-single-leg-pause", "Kuitheffen op één been, pauze onderaan", "Single-leg calf raise, pause in stretch", MovementPattern.Calf,
            [Muscle.Calves], [], stairs, A, 3, 10,
            "Zak op één been tot de diepste rek en blijf daar 2 seconden stil zonder te veren. Duw dan krachtig omhoog. Dit is de zwaarste variant zonder extra gewicht.", unilateral: true);
        Add("calf-raise-bent-knee", "Kuitheffen met gebogen knie (soleus)", "Bent-knee calf raise (soleus)", MovementPattern.Calf,
            [Muscle.Calves], [Muscle.Quadriceps], stairs, I, 3, 15,
            "Sta op één been op de rand van een trede, buig de knie lichtjes en houd die gebogen. Kom omhoog en zak gecontroleerd. De gebogen knie legt de nadruk op de diepe kuitspier (soleus), die veel van de loopbelasting draagt.", unilateral: true);

        // ---- Hamstring ----
        Add("glute-bridge", "Bilbrug", "Glute bridge", MovementPattern.Hamstring,
            [Muscle.Glutes, Muscle.Hamstrings], [Muscle.LowerBack], body, B, 3, 15,
            "Lig op je rug, knieën gebogen, voeten plat. Duw je heupen omhoog tot je lichaam een rechte lijn vormt, knijp de billen samen en zak rustig.");
        Add("single-leg-bridge", "Bilbrug op één been", "Single-leg glute bridge", MovementPattern.Hamstring,
            [Muscle.Hamstrings, Muscle.Glutes], [Muscle.LowerBack, Muscle.Abdominals], body, I, 3, 12,
            "Zoals de bilbrug, maar met één been gestrekt in de lucht. Houd het bekken recht en duw door de hiel van het steunbeen.", unilateral: true);
        Add("towel-hamstring-curl", "Hamstringcurl met handdoek", "Towel hamstring curl", MovementPattern.Hamstring,
            [Muscle.Hamstrings], [Muscle.Glutes, Muscle.Calves], [Equipment.Towel], I, 3, 10,
            "Lig op je rug met je hielen op een handdoek op een gladde vloer. Til het bekken op en trek je hielen langzaam naar je billen, strek daarna gecontroleerd weer uit.");
        Add("nordic-curl", "Nordic hamstring curl", "Nordic hamstring curl", MovementPattern.Hamstring,
            [Muscle.Hamstrings], [Muscle.Glutes, Muscle.Calves], [Equipment.Bodyweight, Equipment.Chair], A, 3, 6,
            "Kniel met de enkels vastgezet (bijvoorbeeld onder een zware bank). Laat je romp zo traag mogelijk naar voren zakken met een rechte heup-knie-schouderlijn en vang jezelf op met de handen. Duw je terug omhoog met zo weinig hulp van de armen als mogelijk.");
        Add("single-leg-rdl", "Roemeense deadlift op één been", "Single-leg Romanian deadlift", MovementPattern.Hamstring,
            [Muscle.Hamstrings, Muscle.Glutes], [Muscle.LowerBack, Muscle.Abdominals], body, I, 3, 10,
            "Sta op één been, kantel vanuit de heup naar voren terwijl het andere been naar achter gaat en houd je rug recht. Kom terug door de bil aan te spannen.", unilateral: true);

        // ---- Squat / been ----
        Add("bodyweight-squat", "Squat", "Bodyweight squat", MovementPattern.Squat,
            [Muscle.Quadriceps, Muscle.Glutes], [Muscle.Hamstrings, Muscle.Adductors, Muscle.LowerBack], body, B, 3, 15,
            "Voeten op schouderbreedte, zak door de knieën en heupen tot de dijen horizontaal zijn, borst omhoog, knieën in lijn met de tenen. Duw door de hielen weer omhoog.");
        Add("split-squat", "Split squat", "Split squat", MovementPattern.Squat,
            [Muscle.Quadriceps, Muscle.Glutes], [Muscle.Hamstrings, Muscle.Adductors], body, B, 3, 12,
            "Neem een grote stap vooruit en blijf staan in spreidstand. Zak recht naar beneden tot de achterste knie bijna de grond raakt en duw terug omhoog. Alle herhalingen per been.", unilateral: true);
        Add("bulgarian-split-squat", "Bulgarian split squat", "Bulgarian split squat", MovementPattern.Squat,
            [Muscle.Quadriceps, Muscle.Glutes], [Muscle.Hamstrings, Muscle.Adductors, Muscle.Abdominals], chair, I, 3, 10,
            "Zet de wreef van het achterste been op een stoel. Zak recht naar beneden op het voorste been en duw terug omhoog. Houd het bovenlichaam rechtop.", unilateral: true);
        Add("skater-squat", "Skater squat", "Skater squat", MovementPattern.Squat,
            [Muscle.Quadriceps, Muscle.Glutes], [Muscle.Hamstrings, Muscle.Calves, Muscle.Abdominals], body, A, 3, 8,
            "Sta op één been en zak met het andere been naar achter tot de knie de grond bijna raakt. Houd het bovenlichaam licht naar voren en kom gecontroleerd terug omhoog.", unilateral: true);
        Add("wall-sit", "Wandzit", "Wall sit", MovementPattern.Squat,
            [Muscle.Quadriceps], [Muscle.Glutes, Muscle.Calves], [Equipment.Wall], B, 3, 45,
            "Leun met je rug tegen de muur en zak tot de knieën 90° gebogen zijn. Houd die houding vast zonder met je handen op je dijen te steunen.", timed: true);
        Add("step-up", "Opstappen", "Step-up", MovementPattern.Squat,
            [Muscle.Quadriceps, Muscle.Glutes], [Muscle.Hamstrings, Muscle.Calves], stairs, B, 3, 12,
            "Stap met één voet volledig op een trede en kom omhoog op dat been zonder af te duwen met het onderste been. Zak gecontroleerd terug.", unilateral: true);
        Add("reverse-lunge", "Achterwaartse lunge", "Reverse lunge", MovementPattern.Squat,
            [Muscle.Quadriceps, Muscle.Glutes], [Muscle.Hamstrings, Muscle.Adductors], body, B, 3, 10,
            "Stap met één been naar achter en zak tot beide knieën ongeveer 90° gebogen zijn. Duw terug naar de beginstand en wissel van been.", unilateral: true);

        // ---- Heup ----
        Add("side-lying-hip-abduction", "Zijlig heupabductie", "Side-lying hip abduction", MovementPattern.Hip,
            [Muscle.Abductors], [Muscle.Glutes], body, B, 3, 15,
            "Lig op je zij met het onderste been gebogen. Til het bovenste been gestrekt omhoog, iets naar achteren, zonder het bekken te laten kantelen, en zak rustig. Stabiliseert het bekken bij het lopen.", unilateral: true);
        Add("clamshell", "Clamshell", "Clamshell", MovementPattern.Hip,
            [Muscle.Abductors, Muscle.Glutes], [], body, B, 3, 15,
            "Lig op je zij, knieën gebogen en de hielen tegen elkaar. Open de bovenste knie zoals een schelp zonder het bekken mee te draaien en sluit gecontroleerd.", unilateral: true);
        Add("fire-hydrant", "Fire hydrant", "Fire hydrant", MovementPattern.Hip,
            [Muscle.Glutes, Muscle.Abductors], [Muscle.Abdominals], body, B, 3, 12,
            "Sta op handen en knieën. Til één gebogen been zijwaarts op tot heuphoogte, zonder de romp te draaien, en zak terug.", unilateral: true);
        Add("donkey-kick", "Donkey kick", "Donkey kick", MovementPattern.Hip,
            [Muscle.Glutes], [Muscle.Hamstrings, Muscle.LowerBack], body, B, 3, 12,
            "Sta op handen en knieën. Duw één voet met gebogen knie omhoog richting plafond, knijp de bil samen en breng terug zonder de onderrug te hol te maken.", unilateral: true);
        Add("standing-hip-abduction", "Staande heupabductie", "Standing hip abduction", MovementPattern.Hip,
            [Muscle.Abductors], [Muscle.Glutes], [Equipment.Wall], B, 3, 15,
            "Sta zijwaarts naast de muur voor steun. Til het buitenste been gestrekt zijwaarts op zonder de romp te kantelen en zak gecontroleerd.", unilateral: true);

        // ---- Romp ----
        Add("dead-bug", "Dead bug", "Dead bug", MovementPattern.Core,
            [Muscle.Abdominals], [Muscle.HipFlexors], body, B, 3, 10,
            "Lig op je rug, armen omhoog en knieën 90° gebogen. Strek tegenovergestelde arm en been naar de grond zonder je onderrug te laten loskomen en keer terug.", unilateral: true);
        Add("plank", "Plank", "Plank", MovementPattern.Core,
            [Muscle.Abdominals], [Muscle.Shoulders, Muscle.Glutes, Muscle.LowerBack], body, B, 3, 45,
            "Steun op de onderarmen en tenen met een rechte lijn van hoofd tot hiel. Span buik en billen aan en houd vast zonder door te zakken.", timed: true);
        Add("side-plank", "Zijplank", "Side plank", MovementPattern.Core,
            [Muscle.Abdominals, Muscle.Abductors], [Muscle.Shoulders, Muscle.Glutes], body, I, 3, 30,
            "Steun op één onderarm en de zijkant van je voet en til het bekken op tot je lichaam een rechte lijn vormt. Houd vast en wissel van kant.", unilateral: true, timed: true);
        Add("bird-dog", "Bird dog", "Bird dog", MovementPattern.Core,
            [Muscle.LowerBack, Muscle.Abdominals], [Muscle.Glutes, Muscle.Shoulders], body, B, 3, 10,
            "Sta op handen en knieën. Strek tegelijk een arm en het tegenovergestelde been tot ze horizontaal zijn, houd het bekken recht en keer terug.", unilateral: true);
        Add("ab-wheel-rollout", "Ab wheel rollout", "Ab wheel rollout", MovementPattern.Core,
            [Muscle.Abdominals], [Muscle.Shoulders, Muscle.Lats, Muscle.HipFlexors], [Equipment.AbWheel], A, 3, 8,
            "Kniel met het wiel onder je schouders. Rol langzaam naar voren met een ronde, aangespannen buik zonder door te zakken in de onderrug en trek jezelf terug met de buikspieren.");
        Add("hollow-hold", "Hollow hold", "Hollow hold", MovementPattern.Core,
            [Muscle.Abdominals], [Muscle.HipFlexors], body, I, 3, 30,
            "Lig op je rug, til schouders en gestrekte benen enkele centimeters op en druk je onderrug tegen de grond. Houd vast.", timed: true);

        // ---- Rug ----
        Add("superman", "Superman", "Superman", MovementPattern.Back,
            [Muscle.LowerBack], [Muscle.Glutes, Muscle.Shoulders], body, B, 3, 12,
            "Lig op je buik, armen vooruit. Til tegelijk armen, borst en benen een paar centimeter op, houd kort vast en zak rustig.");

        // ---- Duwen ----
        Add("incline-push-up", "Opdrukken met handen verhoogd", "Incline push-up", MovementPattern.Push,
            [Muscle.Chest, Muscle.Triceps], [Muscle.Shoulders, Muscle.Abdominals], chair, B, 3, 12,
            "Plaats de handen op de rand van een stevige stoel of de trap, lichaam in een rechte lijn. Zak met de borst naar de rand en duw terug.");
        Add("push-up", "Opdrukken", "Push-up", MovementPattern.Push,
            [Muscle.Chest, Muscle.Triceps], [Muscle.Shoulders, Muscle.Abdominals], body, I, 3, 12,
            "Handen iets breder dan de schouders, lichaam in een rechte lijn. Zak tot de borst vlak boven de grond en duw weer volledig omhoog.");
        Add("pike-push-up", "Pike push-up", "Pike push-up", MovementPattern.Push,
            [Muscle.Shoulders, Muscle.Triceps], [Muscle.Chest, Muscle.Traps], body, A, 3, 8,
            "Begin in een omgekeerde V met de heupen hoog. Buig de ellebogen en breng het hoofd richting de grond tussen de handen en duw terug omhoog.");
        Add("chair-dips", "Dips op een stoel", "Chair dips", MovementPattern.Push,
            [Muscle.Triceps], [Muscle.Shoulders, Muscle.Chest], chair, I, 3, 10,
            "Steun met de handen op de rand van een stevige stoel, benen gebogen vooruit. Zak door de ellebogen tot ongeveer 90° en duw terug omhoog.");

        return list;
    }
}
