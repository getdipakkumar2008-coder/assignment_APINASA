using System.Net;
using System.Text.Json;
using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace MarsPhotos.Infrastructure.Providers;

/// <summary>Shared HTTP handling for photo providers: classifies every failure into a stable <see cref="ProviderResult.Unavailable"/>.</summary>
internal static class ProviderHttp
{
    private const long MaxBodyBytes = 20 * 1024 * 1024;

    internal sealed record Outcome(JsonDocument? Json, ProviderResult.Unavailable? Failure)
    {
        public static Outcome Fail(string code, string message, bool retriable) => new(null, new ProviderResult.Unavailable(code, Redaction.RedactApiKey(message), retriable));
    }

    public static async Task<Outcome> GetJsonAsync(
        IHttpClientFactory factory, string clientName, Uri url, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            using var client = factory.CreateClient(clientName);
            using var response = await client.GetAsync(url, cancellationToken);
            QuotaWatch.Check(response, logger);

            if (!response.IsSuccessStatusCode)
            {
                return FromStatus(response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is > MaxBodyBytes)
            {
                return Outcome.Fail(ErrorCodes.ApiBadResponse, "The response was unexpectedly large.", false);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutRejectedException)
        {
            return Outcome.Fail(ErrorCodes.ApiUnavailable, "The request timed out.", true);
        }
        catch (HttpRequestException ex)
        {
            return Outcome.Fail(ErrorCodes.ApiUnavailable, $"Network error: {ex.Message}", true);
        }
    }

    private static Outcome FromStatus(HttpStatusCode status) => (int)status switch
    {
        404 => Outcome.Fail(ErrorCodes.ApiUnavailable, "HTTP 404 (service not found)", false),
        401 or 403 => Outcome.Fail(ErrorCodes.ApiAuth, $"HTTP {(int)status} (the API key was rejected)", false),
        429 => Outcome.Fail(ErrorCodes.ApiRateLimited, "HTTP 429 (rate limit reached)", true),
        >= 500 => Outcome.Fail(ErrorCodes.ApiUnavailable, $"HTTP {(int)status} (server error)", true),
        _ => Outcome.Fail(ErrorCodes.ApiBadResponse, $"HTTP {(int)status} (unexpected response)", false),
    };

    private static Outcome Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Outcome.Fail(ErrorCodes.ApiBadResponse, "The response was empty.", false);
        }

        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                return Outcome.Fail(ErrorCodes.ApiBadResponse, "The response was JSON but not the expected object.", false);
            }

            return new Outcome(doc, null);
        }
        catch (JsonException)
        {
            var looksLikeHtml = body.TrimStart().StartsWith('<');
            return Outcome.Fail(
                ErrorCodes.ApiBadResponse,
                looksLikeHtml ? "The response was a web page, not JSON data." : "The response was not valid JSON.",
                false);
        }
    }

    public static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : null;

    public static Uri? HttpUri(string? text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return uri;
    }
}
