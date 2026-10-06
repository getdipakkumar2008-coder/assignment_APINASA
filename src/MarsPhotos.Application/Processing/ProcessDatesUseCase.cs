using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Parsing;
using MarsPhotos.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Application.Processing;

/// <summary>
/// The whole pipeline for one run: parse lines, skip out-of-range and duplicate dates, ask the photo provider,
/// download new photos (skipping existing ones) and summarize every line. One failure never stops the others.
/// </summary>
public sealed class ProcessDatesUseCase(
    IPhotoProvider provider,
    IPhotoDownloader downloader,
    IPhotoStore store,
    TimeProvider clock,
    IOptions<RunOptions> options,
    ILogger<ProcessDatesUseCase> logger)
{
    public async Task<RunSummary> RunAsync(
        IReadOnlyList<DateInputLine> lines,
        string? rover = null,
        int? photosPerDate = null,
        CancellationToken ct = default)
    {
        var settings = options.Value;
        var roverName = (rover ?? settings.Rover).Trim().ToLowerInvariant();
        var count = photosPerDate ?? settings.PhotosPerDate;

        store.EnsureWritable();

        var startedAt = clock.GetUtcNow();
        var startStamp = clock.GetTimestamp();
        var today = DateOnly.FromDateTime(startedAt.UtcDateTime);
        var seen = new Dictionary<DateOnly, int>();
        var results = new List<DateResult>(lines.Count);

        for (var i = 0; i < lines.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                AddCancelled(results, lines, i, settings);
                break;
            }

            try
            {
                results.Add(await ProcessLineAsync(lines[i], roverName, count, today, seen, settings, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                AddCancelled(results, lines, i, settings);
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("Unexpected error while processing line {LineNumber}: {ErrorType} {Message}", lines[i].LineNumber, ex.GetType().Name, Redaction.RedactApiKey(ex.Message));
                results.Add(DateResult.WithoutLookup(
                    lines[i].RawText,
                    DateOf(lines[i], settings),
                    DateResultStatus.Failed,
                    new ErrorInfo(ErrorCodes.UnexpectedError, "An unexpected error occurred while processing this date.", ex.GetType().Name)));
            }
        }

        return new RunSummary(roverName, startedAt, clock.GetElapsedTime(startStamp), RunTotals.From(results), results);
    }

    private async Task<DateResult> ProcessLineAsync(
        DateInputLine line,
        string rover,
        int count,
        DateOnly today,
        Dictionary<DateOnly, int> seen,
        RunOptions settings,
        CancellationToken ct)
    {
        var parsed = DateLineParser.Parse(line, settings.MaxLineLength);
        switch (parsed.Status)
        {
            case DateStatus.Invalid:
                return DateResult.WithoutLookup(line.RawText, null, DateResultStatus.InvalidDate, new ErrorInfo(parsed.ErrorCode!, parsed.Message!));
            case DateStatus.Unrecognized:
                return DateResult.WithoutLookup(line.RawText, null, DateResultStatus.UnrecognizedDate, new ErrorInfo(parsed.ErrorCode!, parsed.Message!));
        }

        var date = parsed.Value!.Value;
        if (seen.TryGetValue(date, out var firstLine))
        {
            return DateResult.WithoutLookup(
                line.RawText, date, DateResultStatus.DuplicateDate, note: $"Same date as line {firstLine}; processed once.");
        }

        seen[date] = line.LineNumber;

        if (!MissionRange.Contains(rover, date, today))
        {
            return DateResult.WithoutLookup(
                line.RawText,
                date,
                DateResultStatus.OutsideMissionRange,
                note: $"Outside the {rover} mission range ({MissionRange.Describe(rover, today)}); no lookup made.");
        }

        var answer = await provider.GetPhotosAsync(rover, date, count, ct);
        switch (answer)
        {
            case ProviderResult.Unavailable unavailable:
                var message = Redaction.RedactApiKey(unavailable.Message);
                logger.LogWarning("No photo source could serve {Date}: {Code} {Message}", date, unavailable.ErrorCode, message);
                return new DateResult(
                    line.RawText, date, DateResultStatus.SourceUnavailable, answer.Provider, answer.FallbackReason, 0, [],
                    [new ErrorInfo(unavailable.ErrorCode, message)]);

            case ProviderResult.NoPhotos:
                return new DateResult(
                    line.RawText, date, DateResultStatus.NoPhotos, answer.Provider, answer.FallbackReason, 0, [], [],
                    "No photos are available for this date.");

            case ProviderResult.Photos found:
                return await DownloadAsync(line, date, found, count, settings, ct);

            default:
                throw new InvalidOperationException($"Unknown provider result {answer.GetType().Name}.");
        }
    }

    private async Task<DateResult> DownloadAsync(
        DateInputLine line,
        DateOnly date,
        ProviderResult.Photos found,
        int count,
        RunOptions settings,
        CancellationToken ct)
    {
        // A provider listing the same photo twice, or two ids mapping to one file, must not cause concurrent writes to one file.
        var selected = found.Selected.DistinctBy(FileNameFor).Take(count).ToList();
        var outcomes = new PhotoOutcome[selected.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, settings.MaxParallelDownloads));

        await Task.WhenAll(selected.Select(async (photo, index) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                outcomes[index] = await DownloadOneAsync(photo, date, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        var failed = outcomes.Count(o => o.Status == PhotoStatus.Failed);
        var status = failed == 0
            ? DateResultStatus.Success
            : failed == outcomes.Length ? DateResultStatus.Failed : DateResultStatus.PartialFailure;
        var errors = outcomes
            .Where(o => o.Status == PhotoStatus.Failed)
            .Select(o => new ErrorInfo(o.ErrorCode ?? ErrorCodes.DownloadFailed, $"{o.PhotoId}: {o.Message}"))
            .ToList();

        return new DateResult(
            line.RawText, date, status, found.Provider, found.FallbackReason, found.Available, outcomes, errors);
    }

    private async Task<PhotoOutcome> DownloadOneAsync(RoverPhoto photo, DateOnly date, CancellationToken ct)
    {
        var fileName = FileNameFor(photo);
        var relative = store.RelativePath(date, fileName);
        try
        {
            if (store.Exists(date, fileName))
            {
                return new PhotoOutcome(photo.Id, PhotoStatus.Skipped, relative, null, null, null);
            }

            var result = await downloader.DownloadAsync(photo, date, fileName, ct);
            return result.Success
                ? new PhotoOutcome(photo.Id, PhotoStatus.Downloaded, relative, null, null, result.Bytes)
                : Failed(photo, result.ErrorCode ?? ErrorCodes.DownloadFailed, result.Message ?? "download failed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PhotoStoreException ex)
        {
            return Failed(photo, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(photo, ErrorCodes.IoError, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError("Unexpected error downloading photo {PhotoId}: {ErrorType} {Message}", photo.Id, ex.GetType().Name, Redaction.RedactApiKey(ex.Message));
            return Failed(photo, ErrorCodes.UnexpectedError, ex.GetType().Name);
        }
    }

    private static string FileNameFor(RoverPhoto photo) => FileNames.StemFor(photo.Id) + FileNames.ExtensionFor(photo.ImageUrl);

    private PhotoOutcome Failed(RoverPhoto photo, string code, string rawMessage)
    {
        var message = Redaction.RedactApiKey(rawMessage);
        logger.LogWarning("Photo {PhotoId} failed: {Code} {Message}", photo.Id, code, message);
        return new PhotoOutcome(photo.Id, PhotoStatus.Failed, null, code, message, null);
    }

    private static void AddCancelled(List<DateResult> results, IReadOnlyList<DateInputLine> lines, int from, RunOptions settings)
    {
        for (var i = from; i < lines.Count; i++)
        {
            results.Add(DateResult.WithoutLookup(lines[i].RawText, DateOf(lines[i], settings), DateResultStatus.Cancelled, note: "The run was cancelled before this date was processed."));
        }
    }

    private static DateOnly? DateOf(DateInputLine line, RunOptions settings) =>
        DateLineParser.Parse(line, settings.MaxLineLength).Value;
}
