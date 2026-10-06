using System.Text.Json;
using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Infrastructure.Providers;

/// <summary>
/// NASA's public Curiosity raw-image feed, used when the Mars Rover Photos service cannot answer (decision OQ-10).
/// The feed is keyed by Mars day, so the Earth date is converted to the sol that corresponds to it (decision D-1).
/// No API key is needed or sent.
/// </summary>
public sealed class RawImageFeedProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<NasaOptions> options,
    ILogger<RawImageFeedProvider> logger) : IPhotoProvider
{
    public const string ProviderName = "RawImageFeed";
    public const string HttpClientName = "raw-image-feed";

    public string Name => ProviderName;

    public async Task<ProviderResult> GetPhotosAsync(string rover, DateOnly date, int maxPhotos, CancellationToken cancellationToken)
    {
        if (!string.Equals(rover, "curiosity", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderResult.Unavailable(ErrorCodes.ApiUnavailable, "The fallback photo source supports Curiosity only.", Retriable: false) { Provider = Name };
        }

        int sol;
        try
        {
            sol = SolConverter.ForCuriosity(date);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new ProviderResult.NoPhotos { Provider = Name };
        }

        var url = new UriBuilder(new Uri(options.Value.FallbackBaseUrl))
        {
            Query = $"feed=raw_images&category=msl&feedtype=json&sol={sol}",
        }.Uri;

        logger.LogDebug("Requesting sol {Sol} (for {Date}) from {Provider}", sol, date, Name);
        var outcome = await ProviderHttp.GetJsonAsync(httpClientFactory, HttpClientName, url, logger, cancellationToken);
        if (outcome.Failure is { } failure)
        {
            return failure with { Provider = Name };
        }

        using var document = outcome.Json!;
        if (!document.RootElement.TryGetProperty("images", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return new ProviderResult.Unavailable(ErrorCodes.ApiBadResponse, "The response did not contain an 'images' list.", Retriable: false) { Provider = Name };
        }

        if (array.GetArrayLength() == 0)
        {
            return new ProviderResult.NoPhotos { Provider = Name };
        }

        var photos = new List<RoverPhoto>();
        foreach (var item in array.EnumerateArray())
        {
            if (Map(item, date, sol) is { } photo)
            {
                photos.Add(photo);
            }
        }

        if (photos.Count == 0)
        {
            return new ProviderResult.Unavailable(ErrorCodes.ApiBadResponse, "None of the images in the response had a usable id and address.", Retriable: false) { Provider = Name };
        }

        var sorted = PhotoOrdering.Sort(photos);
        return new ProviderResult.Photos(sorted.Take(maxPhotos).ToList(), sorted.Count) { Provider = Name };
    }

    private static RoverPhoto? Map(JsonElement item, DateOnly date, int sol)
    {
        var id = ProviderHttp.String(item, "imageid");
        var files = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("image_files", out var f) ? f : default;
        var address = ProviderHttp.HttpUri(ProviderHttp.String(files, "full_res") ?? ProviderHttp.String(files, "large"));
        if (string.IsNullOrWhiteSpace(id) || address is null)
        {
            return null;
        }

        var secure = address.Scheme == Uri.UriSchemeHttp
            ? new UriBuilder(address) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri
            : address;
        var camera = item.TryGetProperty("camera", out var cam) ? ProviderHttp.String(cam, "instrument") : null;
        return new RoverPhoto(id, ProviderHttp.Int(item, "sol") ?? sol, camera ?? "UNK", secure, date, "curiosity");
    }
}
