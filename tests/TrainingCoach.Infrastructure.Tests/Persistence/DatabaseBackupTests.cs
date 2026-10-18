using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TrainingCoach.Domain.Planning;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Tests.Persistence;

public sealed class DatabaseBackupTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private string BackupDir => Path.Combine(_db.Directory, "backups");

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Creates_a_dated_copy_that_contains_the_data()
    {
        using (var db = _db.CreateContext())
        {
            db.PlannedSessions.Add(new PlannedSessionEntity { UserId = 1, Date = new DateOnly(2026, 10, 19), Title = "x", Description = "", Phase = "p" });
            db.SaveChanges();
        }

        var path = DatabaseBackup.CreateDailyBackup(_db.DbPath, BackupDir, new DateOnly(2026, 10, 20));

        Assert.Equal(Path.Combine(BackupDir, "trainingcoach-20261020.db"), path);
        using var backup = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        backup.Open();
        using var cmd = backup.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM PlannedSessions";
        Assert.Equal(1L, cmd.ExecuteScalar());
    }

    [Fact]
    public void Does_not_overwrite_todays_backup_and_returns_null()
    {
        var day = new DateOnly(2026, 10, 20);
        DatabaseBackup.CreateDailyBackup(_db.DbPath, BackupDir, day);
        var first = File.GetLastWriteTimeUtc(Path.Combine(BackupDir, "trainingcoach-20261020.db"));

        Assert.Null(DatabaseBackup.CreateDailyBackup(_db.DbPath, BackupDir, day));
        Assert.Equal(first, File.GetLastWriteTimeUtc(Path.Combine(BackupDir, "trainingcoach-20261020.db")));
    }

    [Fact]
    public void Keeps_only_the_latest_14_backups()
    {
        var start = new DateOnly(2026, 10, 1);
        for (var i = 0; i < 20; i++)
            DatabaseBackup.CreateDailyBackup(_db.DbPath, BackupDir, start.AddDays(i));

        var names = Directory.GetFiles(BackupDir).Select(Path.GetFileName).Order().ToList();

        Assert.Equal(14, names.Count);
        Assert.Equal("trainingcoach-20261007.db", names.First());
        Assert.Equal("trainingcoach-20261020.db", names.Last());
    }

    [Fact]
    public void Ignores_unrelated_files_when_pruning()
    {
        Directory.CreateDirectory(BackupDir);
        File.WriteAllText(Path.Combine(BackupDir, "notes.txt"), "keep me");

        for (var i = 0; i < 16; i++)
            DatabaseBackup.CreateDailyBackup(_db.DbPath, BackupDir, new DateOnly(2026, 11, 1).AddDays(i));

        Assert.True(File.Exists(Path.Combine(BackupDir, "notes.txt")));
        Assert.Equal(14, Directory.GetFiles(BackupDir, "trainingcoach-*.db").Length);
    }
}
