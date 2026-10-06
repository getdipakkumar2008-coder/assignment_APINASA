using MarsPhotos.Domain;

namespace MarsPhotos.Application.Abstractions;

public sealed record DownloadResult(bool Success, long Bytes, string? ErrorCode, string? Message)
{
    public static DownloadResult Ok(long bytes) => new(true, bytes, null, null);

    public static DownloadResult Fail(string errorCode, string message) => new(false, 0, errorCode, message);
}

public interface IPhotoDownloader
{
    /// <summary>Downloads the photo and stores it via <see cref="IPhotoStore"/>. Never throws for expected failures.</summary>
    Task<DownloadResult> DownloadAsync(RoverPhoto photo, DateOnly date, string fileName, CancellationToken cancellationToken);
}
