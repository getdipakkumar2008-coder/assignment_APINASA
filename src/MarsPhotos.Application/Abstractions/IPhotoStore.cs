namespace MarsPhotos.Application.Abstractions;

/// <summary>Thrown by a store when content is rejected or cannot be written (<see cref="ErrorCode"/> is an <c>ErrorCodes</c> value).</summary>
public sealed class PhotoStoreException(string errorCode, string message, Exception? inner = null) : Exception(message, inner)
{
    public string ErrorCode { get; } = errorCode;
}

public interface IPhotoStore
{
    /// <summary>True when a complete (non-empty) file with this name exists for the date. A <c>.part</c> file never counts.</summary>
    bool Exists(DateOnly date, string fileName);

    /// <summary>Path relative to the photos root using forward slashes, e.g. <c>2017-02-27/123.jpg</c>.</summary>
    string RelativePath(DateOnly date, string fileName);

    /// <summary>
    /// Writes <paramref name="content"/> to a temporary file and atomically moves it into place.
    /// Rejects empty or oversized content and never overwrites an existing file. Returns bytes written.
    /// </summary>
    Task<long> WriteAsync(DateOnly date, string fileName, Stream content, long maxBytes, CancellationToken cancellationToken);
}
