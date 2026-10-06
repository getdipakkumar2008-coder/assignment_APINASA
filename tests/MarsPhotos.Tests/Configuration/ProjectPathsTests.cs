using MarsPhotos.Infrastructure.Configuration;

namespace MarsPhotos.Tests.Configuration;

public sealed class ProjectPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-paths-" + Guid.NewGuid().ToString("N"));

    public ProjectPathsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Finds_the_solution_root_from_a_nested_project_directory()
    {
        File.WriteAllText(Path.Combine(_root, ProjectPaths.SolutionMarker), "");
        var nested = Directory.CreateDirectory(Path.Combine(_root, "src", "MarsPhotos.Api")).FullName;

        Assert.Equal(_root, ProjectPaths.FindRoot(nested));
    }

    [Fact]
    public void Relative_paths_resolve_under_the_root_and_absolute_paths_are_unchanged()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "dates.txt");

        Assert.Equal(Path.Combine(_root, "photos"), ProjectPaths.Resolve("photos", _root));
        Assert.Equal(Path.Combine(_root, "data", "dates.txt"), ProjectPaths.Resolve(Path.Combine("data", "dates.txt"), _root));
        Assert.Equal(absolute, ProjectPaths.Resolve(absolute, _root));
    }

    [Fact]
    public void Real_repository_layout_is_found_from_the_test_output_folder()
    {
        var root = ProjectPaths.FindRoot(AppContext.BaseDirectory);

        Assert.True(File.Exists(Path.Combine(root, ProjectPaths.SolutionMarker)), root);
        Assert.True(File.Exists(Path.Combine(root, "dates.txt")));
    }
}
