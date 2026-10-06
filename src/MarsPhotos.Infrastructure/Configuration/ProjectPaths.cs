namespace MarsPhotos.Infrastructure.Configuration;

/// <summary>
/// Resolves relative configured paths (<c>dates.txt</c>, <c>photos</c>) against the solution root so the console runner
/// and the API behave the same however they are launched (<c>dotnet run --project</c> changes the API's working directory).
/// Without a solution file (for example in a container) the working directory is used.
/// </summary>
public static class ProjectPaths
{
    public const string SolutionMarker = "MarsPhotos.sln";

    public static string FindRoot(string? startDirectory = null)
    {
        var start = startDirectory ?? Directory.GetCurrentDirectory();
        return Search(start) ?? Search(AppContext.BaseDirectory) ?? Path.GetFullPath(start);
    }

    public static string Resolve(string path, string root) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root, path));

    private static string? Search(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionMarker)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
