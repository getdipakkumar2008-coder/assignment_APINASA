namespace MarsPhotos.Domain;

/// <summary>Stable error codes reported to operators (data-model.md catalogue).</summary>
public static class ErrorCodes
{
    public const string DateUnrecognized = "DATE_UNRECOGNIZED";
    public const string DateInvalid = "DATE_INVALID";
    public const string DateOutOfRange = "DATE_OUT_OF_RANGE";
    public const string DateDuplicate = "DATE_DUPLICATE";
    public const string ApiNoPhotos = "API_NO_PHOTOS";
    public const string ApiRateLimited = "API_RATE_LIMITED";
    public const string ApiAuth = "API_AUTH";
    public const string ApiUnavailable = "API_UNAVAILABLE";
    public const string ApiBadResponse = "API_BAD_RESPONSE";
    public const string DownloadFailed = "DL_FAILED";
    public const string DownloadRejectedUrl = "DL_REJECTED_URL";
    public const string DownloadTooLarge = "DL_TOO_LARGE";
    public const string IoError = "IO_ERROR";
    public const string ConfigMissingKey = "CONFIG_MISSING_KEY";
    public const string InputFileError = "INPUT_FILE_ERROR";
    public const string RunInProgress = "RUN_IN_PROGRESS";
}
