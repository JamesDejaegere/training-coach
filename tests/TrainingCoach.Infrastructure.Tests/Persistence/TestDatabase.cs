using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.Tests.Persistence;

/// <summary>Real SQLite file in a temp folder with the migrations applied.</summary>
public sealed class TestDatabase : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "tc-db-" + Guid.NewGuid().ToString("N"));
    public string DbPath => Path.Combine(Directory, "trainingcoach.db");

    public TestDatabase()
    {
        System.IO.Directory.CreateDirectory(Directory);
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={DbPath}").Options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        System.IO.Directory.Delete(Directory, recursive: true);
    }
}
