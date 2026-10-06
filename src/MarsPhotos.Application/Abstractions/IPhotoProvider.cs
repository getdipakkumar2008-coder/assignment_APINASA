using MarsPhotos.Domain;

namespace MarsPhotos.Application.Abstractions;

/// <summary>Answer from a photo source for one date. Expected failures are values, never exceptions.</summary>
public abstract record ProviderResult
{
    private ProviderResult()
    {
    }

    /// <summary>Name of the provider that produced this answer (set by the provider itself).</summary>
    public string? Provider { get; init; }

    /// <summary>When an earlier provider was bypassed, why (set by the provider chain).</summary>
    public string? FallbackReason { get; init; }

    /// <summary>One or more photos; <paramref name="Selected"/> is already ordered and truncated, <paramref name="Available"/> counts all offered.</summary>
    public sealed record Photos(IReadOnlyList<RoverPhoto> Selected, int Available) : ProviderResult;

    /// <summary>A valid empty answer; the provider chain must NOT fall back.</summary>
    public sealed record NoPhotos : ProviderResult;

    /// <summary>The source could not answer; the chain tries the next provider.</summary>
    public sealed record Unavailable(string ErrorCode, string Message, bool Retriable) : ProviderResult;
}

/// <summary>Port to a photo source (or a chain of sources) that lists photos for a rover on an Earth date.</summary>
public interface IPhotoProvider
{
    string Name { get; }

    Task<ProviderResult> GetPhotosAsync(string rover, DateOnly date, int maxPhotos, CancellationToken cancellationToken);
}
