using System.Net;
using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace MarsPhotos.Infrastructure.Download;

/// <summary>
/// Streams a photo from an allow-listed NASA host into the store. Every address (including redirect targets) is
/// checked by <see cref="UrlPolicy"/> before it is contacted; only image content within the size limit is kept
/// (FR-017, FR-018, FR-035, SEC-4, SEC-6). Expected failures are returned, never thrown.
/// </summary>
public sealed class HttpPhotoDownloader(
    IHttpClientFactory httpClientFactory,
    IPhotoStore store,
    UrlPolicy urlPolicy,
    IOptions<RunOptions> options,
    ILogger<HttpPhotoDownloader> logger) : IPhotoDownloader
{
    public const string HttpClientName = "photo-download";
    private const int MaxRedirects = 3;

    public async Task<DownloadResult> DownloadAsync(RoverPhoto photo, DateOnly date, string fileName, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var check = urlPolicy.Evaluate(photo.ImageUrl.ToString());
        if (!check.IsAllowed)
        {
            return DownloadResult.Fail(ErrorCodes.DownloadRejectedUrl, $"Photo address rejected: {check.Reason}.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.DownloadTimeoutSeconds));
        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            var url = check.Url!;
            for (var hop = 0; ; hop++)
            {
                using var response = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (IsRedirect(response.StatusCode))
                {
                    if (hop >= MaxRedirects)
                    {
                        return DownloadResult.Fail(ErrorCodes.DownloadFailed, $"Too many redirects (more than {MaxRedirects}).");
                    }

                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return DownloadResult.Fail(ErrorCodes.DownloadFailed, "Redirect response had no Location header.");
                    }

                    var next = urlPolicy.Evaluate(new Uri(url, location).ToString());
                    if (!next.IsAllowed)
                    {
                        return DownloadResult.Fail(ErrorCodes.DownloadRejectedUrl, $"Redirect target rejected: {next.Reason}.");
                    }

                    url = next.Url!;
                    continue;
                }

                return await SaveAsync(response, date, fileName, settings, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutRejectedException)
        {
            return DownloadResult.Fail(ErrorCodes.DownloadFailed, $"Download timed out after {settings.DownloadTimeoutSeconds} seconds.");
        }
        catch (PhotoStoreException ex)
        {
            return DownloadResult.Fail(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            logger.LogWarning("Download of photo {PhotoId} failed: {Error}", photo.Id, ex.Message);
            return DownloadResult.Fail(ErrorCodes.DownloadFailed, $"Network error: {ex.Message}");
        }
    }

    private async Task<DownloadResult> SaveAsync(
        HttpResponseMessage response, DateOnly date, string fileName, RunOptions settings, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            return DownloadResult.Fail(ErrorCodes.DownloadFailed, $"The photo server answered HTTP {(int)response.StatusCode}.");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "(none)";
        if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return DownloadResult.Fail(ErrorCodes.DownloadFailed, $"Expected an image but the server sent '{mediaType}'.");
        }

        if (response.Content.Headers.ContentLength is { } declared && declared > settings.MaxImageBytes)
        {
            return DownloadResult.Fail(ErrorCodes.DownloadTooLarge, $"Photo is {declared} bytes, larger than the {settings.MaxImageBytes} byte limit.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        var bytes = await store.WriteAsync(date, fileName, body, settings.MaxImageBytes, ct);
        return DownloadResult.Ok(bytes);
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
