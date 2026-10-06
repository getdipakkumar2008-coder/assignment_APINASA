using System.Text.RegularExpressions;
using MarsPhotos.Infrastructure.Configuration;

namespace MarsPhotos.Tests.Configuration;

/// <summary>
/// AC-8 / SC-007 guard: no source file may contain a key-like assignment or the real key from the environment.
/// The git-ignored <c>.env</c> is the only file allowed to hold it.
/// </summary>
public class RepoSecretScanTests
{
    private static readonly string[] SkippedFolders = ["bin", "obj", ".git", "node_modules", "photos", ".angular", "dist", "TestResults", "coverage"];
    private static readonly string[] ScannedExtensions =
    [
        ".cs",
        ".csproj",
        ".props",
        ".json",
        ".md",
        ".yml",
        ".yaml",
        ".ts",
        ".html",
        ".css",
        ".sh",
        ".ps1",
        ".txt",
        ".xml",
        ".sln",
        ".example",
        ".editorconfig",
    ];

    private static readonly Regex KeyAssignment = new(
        @"(api[_-]?key|apikey|secret|token|password)\W{0,5}[:=]\s*[""']?[A-Za-z0-9_\-]{32,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName;
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                var parts = rel.Split('/');
                if (parts.Take(parts.Length - 1).Any(p => SkippedFolders.Contains(p, StringComparer.OrdinalIgnoreCase)))
                {
                    return false;
                }

                var name = Path.GetFileName(f);
                if (name == ".env")
                {
                    return false; // the one place the key may live; it is git-ignored
                }

                return ScannedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                    || name is "Dockerfile" or ".gitignore" or ".dockerignore";
            });

    [Fact]
    [Trait("AC", "AC-8")]
    public void No_source_file_contains_a_key_like_assignment()
    {
        var root = RepoRoot();
        if (root is null)
        {
            return; // not inside a git checkout (e.g. a source archive): nothing to scan
        }

        var offenders = SourceFiles(root)
            .Where(f => new FileInfo(f).Length < 2_000_000)
            .Where(f => File.ReadLines(f).Any(l => KeyAssignment.IsMatch(l)))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Possible secrets in: " + string.Join(", ", offenders));
    }

    [Fact]
    [Trait("AC", "AC-8")]
    public void The_real_key_from_the_environment_is_not_in_any_source_file()
    {
        var root = RepoRoot();
        if (root is null)
        {
            return;
        }

        DotEnvLoader.Load(root);
        var key = Environment.GetEnvironmentVariable("NASA__ApiKey");
        if (string.IsNullOrWhiteSpace(key) || key.Length < 12 || key.StartsWith("test-", StringComparison.Ordinal))
        {
            return; // no real key on this machine (e.g. CI): the pattern scan above still applies
        }

        var offenders = SourceFiles(root)
            .Where(f => new FileInfo(f).Length < 2_000_000)
            .Where(f => File.ReadAllText(f).Contains(key, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        // Report file names only, never the key.
        Assert.True(offenders.Count == 0, "The NASA key appears in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Env_files_are_git_ignored_and_an_example_is_provided()
    {
        var root = RepoRoot();
        if (root is null)
        {
            return;
        }

        var ignore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        Assert.Matches(@"(?m)^\.env\s*$", ignore);
        Assert.Matches(@"(?m)^photos/\s*$", ignore);
        var example = File.ReadAllText(Path.Combine(root, ".env.example"));
        Assert.Contains("NASA__ApiKey=YOUR_NASA_API_KEY", example);
    }
}
