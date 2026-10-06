using MarsPhotos.Domain;

namespace MarsPhotos.Application.Abstractions;

/// <summary>Outcome of downloading one photo; failures carry a stable error code and a message without secrets.</summary>
public sealed record DownloadResult(bool Success, long Bytes, string? ErrorCode, string? Message)
{
    public static DownloadResult Ok(long bytes) => new(true, bytes, null, null);

    public static DownloadResult Fail(string errorCode, string message) => new(false, 0, errorCode, message);
}

/// <summary>Port that fetches one photo from its source and stores it through <see cref="IPhotoStore"/>.</summary>
public interface IPhotoDownloader
{
    /// <summary>Downloads the photo and stores it via <see cref="IPhotoStore"/>. Never throws for expected failures.</summary>
    Task<DownloadResult> DownloadAsync(RoverPhoto photo, DateOnly date, string fileName, CancellationToken cancellationToken);
}
