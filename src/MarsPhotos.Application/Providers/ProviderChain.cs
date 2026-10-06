using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;
using Microsoft.Extensions.Logging;

namespace MarsPhotos.Application.Providers;

/// <summary>
/// Tries photo providers in order. A provider that answers (photos or "no photos") ends the chain; an unavailable
/// provider is skipped and the reason is recorded so the summary can show why a fallback was used (FR-010, FR-040).
/// </summary>
public sealed class ProviderChain(IEnumerable<IPhotoProvider> providers, ILogger<ProviderChain> logger) : IPhotoProvider
{
    private readonly IReadOnlyList<IPhotoProvider> _providers = providers.ToList();

    public string Name => "ProviderChain";

    public async Task<ProviderResult> GetPhotosAsync(string rover, DateOnly date, int maxPhotos, CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        foreach (var provider in _providers)
        {
            var result = await provider.GetPhotosAsync(rover, date, maxPhotos, cancellationToken);
            if (result is ProviderResult.Unavailable unavailable)
            {
                var reason = $"{provider.Name}: {unavailable.Message}";
                reasons.Add(reason);
                logger.LogWarning("Photo source {Provider} could not serve {Date} ({Message}); trying the next source", provider.Name, date.ToString("yyyy-MM-dd"), unavailable.Message);
                continue;
            }

            return result with
            {
                Provider = result.Provider ?? provider.Name,
                FallbackReason = reasons.Count == 0 ? null : string.Join("; ", reasons),
            };
        }

        var all = string.Join(" | ", reasons);
        return new ProviderResult.Unavailable(ErrorCodes.ApiUnavailable, $"No photo source could serve this date. {all}", Retriable: true)
        {
            FallbackReason = all,
        };
    }
}
