using System.Text.RegularExpressions;

namespace MarsPhotos.Infrastructure.Logging;

/// <summary>Removes secrets from text that may be logged or shown (FR-026, SEC-1).</summary>
public static partial class Redaction
{
    public const string Mask = "***";

    [GeneratedRegex("(api[_-]?key=)[^&\\s\"'#]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyPattern();

    /// <summary>Masks <c>api_key=...</c> values (case-insensitive) in URLs or messages.</summary>
    public static string RedactApiKey(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : ApiKeyPattern().Replace(text, "$1" + Mask);

    /// <summary>Also masks an exact secret value wherever it appears (defense in depth).</summary>
    public static string Redact(string? text, string? secret)
    {
        var redacted = RedactApiKey(text);
        return string.IsNullOrEmpty(secret) || secret.Length < 4
            ? redacted
            : redacted.Replace(secret, Mask, StringComparison.Ordinal);
    }
}
