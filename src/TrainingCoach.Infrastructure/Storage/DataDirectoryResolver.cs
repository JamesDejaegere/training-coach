namespace TrainingCoach.Infrastructure.Storage;

/// <summary>
/// Resolves the folder that holds the SQLite database and backups (SPEC 4.1).
/// The folder is never inside the build output so builds cannot wipe it.
/// </summary>
public static class DataDirectoryResolver
{
    public const string FolderName = "data";

    /// <param name="configuredPath">Explicit <c>DataDirectory</c> setting; wins when present.</param>
    /// <param name="startDirectory">Directory to start looking for the repository root (usually the content root).</param>
    public static string Resolve(string? configuredPath, string startDirectory)
    {
        var path = !string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(configuredPath, startDirectory)
            : Path.Combine(FindRepositoryRoot(startDirectory) ?? startDirectory, FolderName);

        Directory.CreateDirectory(path);
        return path;
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || dir.GetFiles("*.slnx").Length > 0)
                return dir.FullName;
        }
        return null;
    }
}
