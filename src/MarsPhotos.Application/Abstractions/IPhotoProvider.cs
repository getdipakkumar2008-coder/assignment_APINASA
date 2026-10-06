using MarsPhotos.Domain;

namespace MarsPhotos.Application.Abstractions;

/// <summary>Answer from a photo source for one date. Expected failures are values, never exceptions.</summary>
public abstract record ProviderResult
{
    private ProviderResult()
    {
    }

    /// <summary>One or more photos; <paramref name="Selected"/> is already ordered and truncated, <paramref name="Available"/> counts all offered.</summary>
    public sealed record Photos(IReadOnlyList<RoverPhoto> Selected, int Available) : ProviderResult;

    /// <summary>A valid empty answer; the provider chain must NOT fall back.</summary>
    public sealed record NoPhotos : ProviderResult;

    /// <summary>The source could not answer; the chain tries the next provider.</summary>
    public sealed record Unavailable(string ErrorCode, string Message, bool Retriable) : ProviderResult;
}

public interface IPhotoProvider
{
    string Name { get; }

    Task<ProviderResult> GetPhotosAsync(string rover, DateOnly date, int maxPhotos, CancellationToken cancellationToken);
}
