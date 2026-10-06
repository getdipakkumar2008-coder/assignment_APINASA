namespace MarsPhotos.Infrastructure.Configuration;

/// <summary>
/// Development convenience: loads <c>KEY=VALUE</c> pairs from a local, git-ignored <c>.env</c> file into process
/// environment variables so <c>NASA__ApiKey</c> reaches configuration. Existing environment variables win.
/// Values are never logged or included in exceptions (FR-024, SEC-1, SEC-10).
/// </summary>
public static class DotEnvLoader
{
    /// <summary>Finds <c>.env</c> from <paramref name="startDirectory"/> upward (stopping at the repository root) and loads it. Returns the number of variables set.</summary>
    public static int Load(string? startDirectory = null)
    {
        var path = Find(startDirectory ?? Directory.GetCurrentDirectory());
        return path is null ? 0 : LoadFile(path);
    }

    /// <summary>
    /// Looks for <c>.env</c> from <paramref name="startDirectory"/> upward, but never above the repository / solution root.
    /// Without such a marker only the start directory is checked, so a stray <c>/.env</c> or <c>C:\.env</c> can never be loaded
    /// (for example inside a container).
    /// </summary>
    public static string? Find(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (IsProjectRoot(dir))
            {
                return null;
            }

            dir = HasProjectRootAbove(dir) ? dir.Parent : null;
        }

        return null;
    }

    private static bool IsProjectRoot(DirectoryInfo dir) =>
        Directory.Exists(Path.Combine(dir.FullName, ".git"))
        || File.Exists(Path.Combine(dir.FullName, ".git"))
        || File.Exists(Path.Combine(dir.FullName, "MarsPhotos.sln"));

    private static bool HasProjectRootAbove(DirectoryInfo dir)
    {
        for (var parent = dir.Parent; parent is not null; parent = parent.Parent)
        {
            if (IsProjectRoot(parent))
            {
                return true;
            }
        }

        return false;
    }

    public static int LoadFile(string path)
    {
        var set = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = Unquote(line[(eq + 1)..].Trim());
            if (key.Length == 0 || Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
            set++;
        }

        return set;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
}
