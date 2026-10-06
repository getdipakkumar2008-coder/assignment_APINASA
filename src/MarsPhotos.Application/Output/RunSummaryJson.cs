using System.Text.Json;
using System.Text.Json.Serialization;
using MarsPhotos.Domain;

namespace MarsPhotos.Application.Output;

/// <summary>Wire shape of an error shown to clients.</summary>
public sealed record ErrorDto(string Code, string Message);

/// <summary>Wire shape of one photo outcome, with a relative URL when the photo was stored.</summary>
public sealed record PhotoDto(string Id, string Status, string? Url, ErrorDto? Error);

/// <summary>Wire shape of the result for one input line (see contracts/openapi.yaml).</summary>
public sealed record DateResultDto(
    string Input,
    DateOnly? Date,
    string Status,
    string? Provider,
    string? FallbackReason,
    string? Note,
    int PhotosAvailable,
    int Downloaded,
    int Skipped,
    int Failed,
    IReadOnlyList<PhotoDto> Photos,
    IReadOnlyList<ErrorDto> Errors);

/// <summary>Wire shape of the totals for a run.</summary>
public sealed record RunTotalsDto(
    int Lines,
    int Valid,
    int Invalid,
    int OutsideMissionRange,
    int Downloaded,
    int Skipped,
    int Failed);

/// <summary>Wire shape of a whole run summary (see contracts/openapi.yaml).</summary>
public sealed record RunSummaryDto(
    string Rover,
    DateTimeOffset StartedAt,
    long DurationMs,
    RunTotalsDto Totals,
    IReadOnlyList<DateResultDto> Results)
{
    /// <summary>Maps the domain summary to the wire shape in contracts/openapi.yaml (no error details, secrets redacted).</summary>
    public static RunSummaryDto From(RunSummary s) => new(
        s.Rover,
        s.StartedAt,
        (long)s.Duration.TotalMilliseconds,
        new RunTotalsDto(s.Totals.Lines, s.Totals.Valid, s.Totals.Invalid, s.Totals.OutsideMissionRange, s.Totals.Downloaded, s.Totals.Skipped, s.Totals.Failed),
        s.Results.Select(r => new DateResultDto(
            Redaction.RedactApiKey(r.Input),
            r.Date,
            r.Status.ToString(),
            r.Provider,
            r.FallbackReason is null ? null : Redaction.RedactApiKey(r.FallbackReason),
            r.Note is null ? null : Redaction.RedactApiKey(r.Note),
            r.PhotosAvailable,
            r.Downloaded,
            r.Skipped,
            r.Failed,
            r.Photos.Select(Photo).ToList(),
            r.Errors.Select(e => new ErrorDto(e.Code, Redaction.RedactApiKey(e.Message))).ToList())).ToList());

    private static PhotoDto Photo(PhotoOutcome p) => new(
        p.PhotoId,
        p.Status.ToString(),
        p.RelativePath is null ? null : "/photos/" + p.RelativePath,
        p.ErrorCode is null ? null : new ErrorDto(p.ErrorCode, Redaction.RedactApiKey(p.Message)));
}

/// <summary>JSON used by both the console runner (<c>--json</c>) and the API, so the two never drift.</summary>
public static class RunSummaryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public static string Serialize(RunSummary summary) => JsonSerializer.Serialize(RunSummaryDto.From(summary), Options);
}
