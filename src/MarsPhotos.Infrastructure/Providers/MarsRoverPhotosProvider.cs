using System.Text.Json;
using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Infrastructure.Providers;

/// <summary>
/// The Mars Rover Photos service named in the brief: <c>GET rovers/{rover}/photos?earth_date=...&amp;api_key=...</c>.
/// Implemented to the documented contract so it works unchanged if the service returns (contracts/photo-provider.md).
/// </summary>
public sealed class MarsRoverPhotosProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<NasaOptions> options,
    ILogger<MarsRoverPhotosProvider> logger) : IPhotoProvider
{
    public const string ProviderName = "MarsRoverPhotos";
    public const string HttpClientName = "mars-photos-primary";

    public string Name => ProviderName;

    public async Task<ProviderResult> GetPhotosAsync(string rover, DateOnly date, int maxPhotos, CancellationToken cancellationToken)
    {
        var nasa = options.Value;
        if (!MissionRange.IsKnownRover(rover))
        {
            return Fail(ErrorCodes.ApiUnavailable, $"Unknown rover '{rover}'.");
        }

        var baseUri = new Uri(nasa.PrimaryBaseUrl.EndsWith('/') ? nasa.PrimaryBaseUrl : nasa.PrimaryBaseUrl + "/");
        var url = new UriBuilder(new Uri(baseUri, $"rovers/{rover.ToLowerInvariant()}/photos"))
        {
            Query = $"earth_date={date:yyyy-MM-dd}&api_key={Uri.EscapeDataString(nasa.ApiKey ?? string.Empty)}",
        }.Uri;

        logger.LogDebug("Requesting {Date} from {Provider}: {Url}", date, Name, Redaction.RedactApiKey(url.ToString()));
        var outcome = await ProviderHttp.GetJsonAsync(httpClientFactory, HttpClientName, url, logger, cancellationToken);
        if (outcome.Failure is { } failure)
        {
            return failure with { Provider = Name };
        }

        using var document = outcome.Json!;
        if (!document.RootElement.TryGetProperty("photos", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Fail(ErrorCodes.ApiBadResponse, "The response did not contain a 'photos' list.");
        }

        var photos = new List<RoverPhoto>();
        foreach (var item in array.EnumerateArray())
        {
            if (Map(item, rover, date) is { } photo)
            {
                photos.Add(photo);
            }
        }

        if (array.GetArrayLength() == 0)
        {
            return new ProviderResult.NoPhotos { Provider = Name };
        }

        if (photos.Count == 0)
        {
            return Fail(ErrorCodes.ApiBadResponse, "None of the photos in the response had a usable id and image address.");
        }

        var sorted = PhotoOrdering.Sort(photos);
        return new ProviderResult.Photos(sorted.Take(maxPhotos).ToList(), sorted.Count) { Provider = Name };
    }

    private ProviderResult.Unavailable Fail(string code, string message) =>
        new(code, message, Retriable: false) { Provider = Name };

    private static RoverPhoto? Map(JsonElement item, string rover, DateOnly date)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idElement))
        {
            return null;
        }

        var id = idElement.ValueKind switch
        {
            JsonValueKind.Number => idElement.GetRawText(),
            JsonValueKind.String => idElement.GetString(),
            _ => null,
        };
        var image = ProviderHttp.HttpUri(ProviderHttp.String(item, "img_src"));
        if (string.IsNullOrWhiteSpace(id) || image is null)
        {
            return null;
        }

        var camera = item.TryGetProperty("camera", out var cam) ? ProviderHttp.String(cam, "name") : null;
        return new RoverPhoto(id, ProviderHttp.Int(item, "sol"), camera ?? "UNK", image, date, rover.ToLowerInvariant());
    }
}
