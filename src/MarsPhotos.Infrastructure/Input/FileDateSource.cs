using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Infrastructure.Input;

/// <summary>Reads dates from a text file, one per line (FR-001, FR-002, FR-029).</summary>
public sealed class FileDateSource(IOptions<RunOptions> options) : IDateSource
{
    public async Task<IReadOnlyList<DateInputLine>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var limits = options.Value;
        if (Directory.Exists(path))
        {
            throw new FatalRunException(ErrorCodes.InputFileError, $"Input path '{path}' is a folder, not a file.");
        }

        if (!File.Exists(path))
        {
            throw new FatalRunException(ErrorCodes.InputFileError, $"Input file '{path}' was not found.");
        }

        var lines = new List<DateInputLine>();
        var lineNumber = 0;
        var accepted = 0;
        try
        {
            await foreach (var raw in File.ReadLinesAsync(path, cancellationToken))
            {
                lineNumber++;
                var text = raw.Trim();
                if (text.Length == 0 || text.StartsWith('#'))
                {
                    continue;
                }

                string? reject = null;
                if (++accepted > limits.MaxFileLines)
                {
                    reject = $"Input limit of {limits.MaxFileLines} lines exceeded.";
                }

                if (text.Length > limits.MaxLineLength)
                {
                    text = text[..limits.MaxLineLength];
                    reject ??= $"Line is longer than {limits.MaxLineLength} characters.";
                }

                lines.Add(new DateInputLine(lineNumber, text, reject));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FatalRunException(ErrorCodes.InputFileError, $"Input file '{path}' could not be read: {ex.Message}", ex);
        }

        if (lines.Count == 0)
        {
            throw new FatalRunException(ErrorCodes.InputFileError, $"Input file '{path}' contains no dates.");
        }

        return lines;
    }
}
