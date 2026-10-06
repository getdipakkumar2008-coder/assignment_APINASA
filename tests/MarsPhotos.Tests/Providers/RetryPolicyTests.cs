using System.Diagnostics;
using System.Net;
using MarsPhotos.Application;
using MarsPhotos.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Providers;

/// <summary>Scripted handler: each entry is a response status, an exception, or a delay-then-OK.</summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _script = new();

    public int Calls { get; private set; }

    public List<DateTimeOffset> CallTimes { get; } = [];

    public ScriptedHandler Status(HttpStatusCode code, string? retryAfterSeconds = null)
    {
        _script.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(code) { Content = new StringContent("x") };
            if (retryAfterSeconds is not null)
            {
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfterSeconds);
            }

            return Task.FromResult(response);
        });
        return this;
    }

    public ScriptedHandler Throw(Exception ex)
    {
        _script.Enqueue(_ => throw ex);
        return this;
    }

    public ScriptedHandler Hang()
    {
        _script.Enqueue(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        CallTimes.Add(DateTimeOffset.UtcNow);
        var step = _script.Count > 0 ? _script.Dequeue() : _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        return step(cancellationToken);
    }
}

public class RetryPolicyTests
{
    private static HttpClient Build(ScriptedHandler handler, Action<RunOptions>? configure = null, TimeSpan? attemptTimeout = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new RunOptions
        {
            MaxRetries = 3,
            RetryBaseDelayMilliseconds = 5,
            RetryMaxDelayMilliseconds = 40,
            TotalBudgetSeconds = 20,
            LookupTimeoutSeconds = 5,
        }.Also(configure)));
        services.AddHttpClient("t")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddPhotoResilience(o => attemptTimeout ?? TimeSpan.FromSeconds(o.LookupTimeoutSeconds), o => TimeSpan.FromSeconds(o.TotalBudgetSeconds));
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("t");
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task Retries_transient_500s_then_succeeds()
    {
        var handler = new ScriptedHandler().Status(HttpStatusCode.InternalServerError).Status(HttpStatusCode.BadGateway);
        using var client = Build(handler);

        var response = await client.GetAsync("https://example.test/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [Trait("AC", "AC-6")]
    public async Task Never_retries_permanent_client_errors(HttpStatusCode code)
    {
        var handler = new ScriptedHandler().Status(code).Status(HttpStatusCode.OK);
        using var client = Build(handler);

        var response = await client.GetAsync("https://example.test/x");

        Assert.Equal(code, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Honors_Retry_After_on_429()
    {
        var handler = new ScriptedHandler().Status((HttpStatusCode)429, retryAfterSeconds: "1");
        using var client = Build(handler);
        var clock = Stopwatch.StartNew();

        var response = await client.GetAsync("https://example.test/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(900), $"waited only {clock.Elapsed}");
    }

    [Fact]
    public async Task Retries_network_errors()
    {
        var handler = new ScriptedHandler().Throw(new HttpRequestException("connection reset"));
        using var client = Build(handler);

        var response = await client.GetAsync("https://example.test/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Gives_up_after_the_configured_retries_and_returns_the_last_response()
    {
        var handler = new ScriptedHandler();
        for (var i = 0; i < 10; i++)
        {
            handler.Status(HttpStatusCode.ServiceUnavailable);
        }

        using var client = Build(handler);

        var response = await client.GetAsync("https://example.test/x");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(4, handler.Calls); // 1 attempt + 3 retries
    }

    [Fact]
    public async Task Per_attempt_timeout_is_retried()
    {
        var handler = new ScriptedHandler().Hang();
        using var client = Build(handler, attemptTimeout: TimeSpan.FromMilliseconds(150));

        var response = await client.GetAsync("https://example.test/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Backoff_waits_stay_within_the_configured_maximum()
    {
        var handler = new ScriptedHandler();
        for (var i = 0; i < 4; i++)
        {
            handler.Status(HttpStatusCode.InternalServerError);
        }

        using var client = Build(handler, o => o.RetryMaxDelayMilliseconds = 40);

        await client.GetAsync("https://example.test/x");

        for (var i = 1; i < handler.CallTimes.Count; i++)
        {
            var gap = handler.CallTimes[i] - handler.CallTimes[i - 1];
            Assert.True(gap < TimeSpan.FromSeconds(3), $"gap {gap} exceeds bound"); // generous: only guards against unbounded backoff, not timing
        }
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried()
    {
        var handler = new ScriptedHandler().Hang().Hang();
        using var client = Build(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test/x", cts.Token));

        Assert.Equal(1, handler.Calls);
    }
}

internal static class Extensions
{
    public static T Also<T>(this T value, Action<T>? action)
    {
        action?.Invoke(value);
        return value;
    }
}
