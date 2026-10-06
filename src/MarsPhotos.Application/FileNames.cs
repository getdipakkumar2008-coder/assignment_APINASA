using System.Text;

namespace MarsPhotos.Application;

/// <summary>Builds file-system-safe names and guards against path traversal (FR-014, FR-018, SEC-3).</summary>
public static class FileNames
{
    private const int MaxStemLength = 100;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png"];
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Keeps <c>[A-Za-z0-9._-]</c>, replaces every other character with <c>_</c>, never returns an empty or dots-only stem.</summary>
    public static string Sanitize(string? value)
    {
        var sb = new StringBuilder();
        foreach (var c in value ?? string.Empty)
        {
            sb.Append(c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or '-' ? c : '_');
        }

        if (sb.Length > MaxStemLength)
        {
            sb.Length = MaxStemLength;
        }

        var stem = sb.ToString();
        if (stem.Length == 0 || stem.All(c => c == '.'))
        {
            stem = "_" + stem;
        }

        var firstSegment = stem.Split('.')[0];
        return Reserved.Contains(firstSegment) ? "_" + stem : stem;
    }

    /// <summary>Extension from the address path if it is on the allow-list, otherwise <c>.jpg</c>.</summary>
    public static string ExtensionFor(Uri url)
    {
        var ext = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
        return Array.IndexOf(AllowedExtensions, ext) >= 0 ? ext : ".jpg";
    }

    /// <summary>Resolves <paramref name="relativePath"/> under <paramref name="root"/> or throws if it would escape it.</summary>
    public static string EnsureInside(string root, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Absolute paths are not allowed.", nameof(relativePath));
        }

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(rootFull, normalized));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(rootFull, comparison))
        {
            throw new ArgumentException("Path escapes the photos root.", nameof(relativePath));
        }

        return full;
    }
}
