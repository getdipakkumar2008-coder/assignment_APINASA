namespace MarsPhotos.Domain;

/// <summary>Overall outcome for one input line.</summary>
public enum DateResultStatus
{
    Success,
    NoPhotos,
    InvalidDate,
    UnrecognizedDate,
    OutsideMissionRange,
    DuplicateDate,
    SourceUnavailable,
    PartialFailure,
    Failed,
    Cancelled,
}

/// <summary>An error shown to the operator. <paramref name="Detail"/> must never contain secrets.</summary>
public sealed record ErrorInfo(string Code, string Message, string? Detail = null);

/// <summary>Everything that happened for one input line.</summary>
public sealed record DateResult(
    string Input,
    DateOnly? Date,
    DateResultStatus Status,
    string? Provider,
    string? FallbackReason,
    int PhotosAvailable,
    IReadOnlyList<PhotoOutcome> Photos,
    IReadOnlyList<ErrorInfo> Errors,
    string? Note = null)
{
    public int Downloaded => Photos.Count(p => p.Status == PhotoStatus.Downloaded);

    public int Skipped => Photos.Count(p => p.Status == PhotoStatus.Skipped);

    public int Failed => Photos.Count(p => p.Status == PhotoStatus.Failed);

    public static DateResult WithoutLookup(string input, DateOnly? date, DateResultStatus status, ErrorInfo? error = null, string? note = null) =>
        new(input, date, status, null, null, 0, [], error is null ? [] : [error], note);
}

/// <summary>Totals across all lines of a run; derived from the per-line results.</summary>
public sealed record RunTotals(
    int Lines,
    int Valid,
    int Invalid,
    int OutsideMissionRange,
    int Downloaded,
    int Skipped,
    int Failed)
{
    public static RunTotals From(IReadOnlyCollection<DateResult> results) => new(
        results.Count,
        results.Count(r => r.Date.HasValue),
        results.Count(r => r.Status is DateResultStatus.InvalidDate or DateResultStatus.UnrecognizedDate),
        results.Count(r => r.Status == DateResultStatus.OutsideMissionRange),
        results.Sum(r => r.Downloaded),
        results.Sum(r => r.Skipped),
        results.Sum(r => r.Failed));
}

/// <summary>Result of a whole run; one <see cref="DateResult"/> per input line, in input order.</summary>
public sealed record RunSummary(
    string Rover,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    RunTotals Totals,
    IReadOnlyList<DateResult> Results);
