using Microsoft.EntityFrameworkCore;
using TrainingCoach.Domain.Strength;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Strength;

public sealed record CustomExerciseInput(
    string NameNl,
    MovementPattern Pattern,
    IReadOnlyList<Muscle> Primary,
    IReadOnlyList<Muscle> Secondary,
    IReadOnlyList<Equipment> Equipment,
    bool Unilateral,
    bool Timed,
    int Sets,
    int Target,
    string Instructions);

/// <summary>The curated library plus the athlete's own exercises (SPEC 9).</summary>
public sealed class ExerciseCatalog(AppDbContext db)
{
    public const string CustomPrefix = "custom-";

    public async Task<IReadOnlyList<ExerciseDefinition>> AllAsync(int userId = UserIds.Owner, CancellationToken ct = default)
    {
        var custom = await db.CustomExercises.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.Id).ToListAsync(ct);
        return [.. ExerciseLibrary.All, .. custom.Select(ToDefinition)];
    }

    public async Task<ExerciseDefinition?> FindAsync(string key, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        if (ExerciseLibrary.ByKey.TryGetValue(key, out var library)) return library;
        if (!key.StartsWith(CustomPrefix) || !int.TryParse(key[CustomPrefix.Length..], out var id)) return null;

        var entity = await db.CustomExercises.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        return entity is null ? null : ToDefinition(entity);
    }

    public async Task<ExerciseDefinition> AddCustomAsync(CustomExerciseInput input, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.NameNl)) throw new ArgumentException("A name is required.", nameof(input));
        if (input.Primary.Count == 0) throw new ArgumentException("At least one primary muscle is required.", nameof(input));
        if (input.Sets is < 1 or > 10 || input.Target < 1) throw new ArgumentException("Sets must be 1-10 and the target at least 1.", nameof(input));

        var entity = new CustomExerciseEntity
        {
            UserId = userId,
            NameNl = input.NameNl.Trim(),
            Pattern = input.Pattern.ToString(),
            PrimaryMuscles = string.Join(',', input.Primary),
            SecondaryMuscles = string.Join(',', input.Secondary.Except(input.Primary)),
            Equipment = string.Join(',', input.Equipment),
            Unilateral = input.Unilateral,
            Timed = input.Timed,
            Sets = input.Sets,
            Target = input.Target,
            Instructions = input.Instructions.Trim(),
        };
        db.CustomExercises.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDefinition(entity);
    }

    private static ExerciseDefinition ToDefinition(CustomExerciseEntity c) => new(
        CustomPrefix + c.Id,
        c.NameNl,
        c.NameNl,
        Enum.TryParse<MovementPattern>(c.Pattern, out var pattern) ? pattern : MovementPattern.Core,
        ParseList<Muscle>(c.PrimaryMuscles),
        ParseList<Muscle>(c.SecondaryMuscles),
        ParseList<Equipment>(c.Equipment),
        ExerciseLevel.Beginner,
        c.Unilateral,
        c.Timed,
        c.Sets,
        c.Target,
        null,
        null,
        c.Instructions);

    private static List<T> ParseList<T>(string csv) where T : struct, Enum =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<T>(s, out var v) ? (T?)v : null)
            .OfType<T>()
            .ToList();
}
