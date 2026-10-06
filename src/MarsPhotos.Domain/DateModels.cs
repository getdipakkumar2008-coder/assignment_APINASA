namespace MarsPhotos.Domain;

/// <summary>One non-blank line read from the dates file or request.</summary>
/// <param name="LineNumber">1-based line number; blank lines are skipped but numbering is preserved.</param>
/// <param name="RawText">Trimmed original text (maximum 200 characters).</param>
/// <param name="RejectReason">Set by the reader when the line exceeded an input limit; the line is then reported as unrecognized.</param>
public sealed record DateInputLine(int LineNumber, string RawText, string? RejectReason = null);

public enum DateStatus
{
    Valid,
    Invalid,
    Unrecognized,
}

/// <summary>Result of interpreting one input line.</summary>
public sealed record ParsedDate(DateInputLine Line, DateStatus Status, DateOnly? Value, string? ErrorCode, string? Message)
{
    public static ParsedDate Valid(DateInputLine line, DateOnly value) => new(line, DateStatus.Valid, value, null, null);

    public static ParsedDate Invalid(DateInputLine line, string message) =>
        new(line, DateStatus.Invalid, null, ErrorCodes.DateInvalid, message);

    public static ParsedDate Unrecognized(DateInputLine line, string message) =>
        new(line, DateStatus.Unrecognized, null, ErrorCodes.DateUnrecognized, message);
}
