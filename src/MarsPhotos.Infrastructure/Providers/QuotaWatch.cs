using Microsoft.Extensions.Logging;

namespace MarsPhotos.Infrastructure.Providers;

/// <summary>Warns when the provider's request quota is nearly used up (FR-040). Never fails a request and never logs the key.</summary>
internal static class QuotaWatch
{
    public const int WarnBelow = 100;

    public static void Check(HttpResponseMessage response, ILogger logger)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Remaining", out var values))
        {
            return;
        }

        if (int.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var remaining) && remaining < WarnBelow)
        {
            logger.LogWarning(
                "NASA API quota is nearly used up: {Remaining} requests remaining (warning below {Threshold}).",
                remaining,
                WarnBelow);
        }
    }
}
