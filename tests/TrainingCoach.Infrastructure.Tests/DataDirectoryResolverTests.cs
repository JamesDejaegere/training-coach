using TrainingCoach.Infrastructure.Storage;

namespace TrainingCoach.Infrastructure.Tests;

public sealed class DataDirectoryResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc-" + Guid.NewGuid().ToString("N"));

    public DataDirectoryResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Uses_configured_path_when_given()
    {
        var configured = Path.Combine(_root, "custom");

        var result = DataDirectoryResolver.Resolve(configured, _root);

        Assert.Equal(configured, result);
        Assert.True(Directory.Exists(result));
    }

    [Fact]
    public void Resolves_relative_configured_path_against_start_directory()
    {
        var result = DataDirectoryResolver.Resolve("mydata", _root);

        Assert.Equal(Path.Combine(_root, "mydata"), result);
    }

    [Fact]
    public void Defaults_to_data_folder_in_repository_root_outside_bin()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var nested = Directory.CreateDirectory(Path.Combine(_root, "src", "Web", "bin", "Debug")).FullName;

        var result = DataDirectoryResolver.Resolve(null, nested);

        Assert.Equal(Path.Combine(_root, "data"), result);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", result + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void Falls_back_to_data_next_to_start_directory_without_repository()
    {
        var start = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;

        var result = DataDirectoryResolver.Resolve(" ", start);

        Assert.Equal(Path.Combine(start, "data"), result);
    }
}
