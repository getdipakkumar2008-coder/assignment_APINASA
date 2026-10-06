using System.Net;
using MarsPhotos.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace MarsPhotos.Infrastructure.Http;

public static class ResilienceExtensions
{
    /// <summary>
    /// Adds the standard resilience pipeline: total time budget, retries for temporary failures only
    /// (timeouts, network errors, HTTP 408/429/5xx) with exponential backoff + jitter and Retry-After support,
    /// and a per-attempt timeout. Permanent failures (400/401/403/404) are never retried (FR-012, FR-033).
    /// </summary>
    public static IHttpClientBuilder AddPhotoResilience(
        this IHttpClientBuilder builder,
        Func<RunOptions, TimeSpan> attemptTimeout,
        Func<RunOptions, TimeSpan> totalBudget)
    {
        builder.AddResilienceHandler("photo-resilience", (pipeline, context) =>
        {
            var run = context.ServiceProvider.GetRequiredService<IOptions<RunOptions>>().Value;

            pipeline.AddTimeout(totalBudget(run));
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = run.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(run.RetryBaseDelayMilliseconds),
                MaxDelay = TimeSpan.FromMilliseconds(run.RetryMaxDelayMilliseconds),
                ShouldRetryAfterHeader = true,
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
            });
            pipeline.AddTimeout(attemptTimeout(run));
        });

        return builder;
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        if (outcome.Result is null)
        {
            return false;
        }

        var status = (int)outcome.Result.StatusCode;
        return status is 408 or 429 or >= 500;
    }
}
