using Microsoft.Data.Sqlite;

namespace TrainingCoach.Infrastructure.Persistence;

/// <summary>Daily copy of the SQLite database, keeping the latest 14 (SPEC 4.1, D17).</summary>
public static class DatabaseBackup
{
    public const int KeepCount = 14;
    private const string Prefix = "trainingcoach-";
    private const string Extension = ".db";

    /// <returns>The new backup path, or null when today's backup already exists.</returns>
    public static string? CreateDailyBackup(string databasePath, string backupDirectory, DateOnly today, int keep = KeepCount)
    {
        Directory.CreateDirectory(backupDirectory);
        var target = Path.Combine(backupDirectory, $"{Prefix}{today:yyyyMMdd}{Extension}");
        if (File.Exists(target)) return null;

        // SQLite online backup: consistent even while the app is writing. Write to a temp file first so a crash never leaves a partial backup.
        var temp = target + ".tmp";
        File.Delete(temp);
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }
        File.Move(temp, target);

        Prune(backupDirectory, keep);
        return target;
    }

    private static void Prune(string backupDirectory, int keep)
    {
        var old = Directory.GetFiles(backupDirectory, $"{Prefix}????????{Extension}")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(keep);
        foreach (var file in old) File.Delete(file);
    }
}
