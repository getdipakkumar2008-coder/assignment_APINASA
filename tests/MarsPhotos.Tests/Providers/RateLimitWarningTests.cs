using MarsPhotos.Application;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Providers;

public class RateLimitWarningTests
{
    private const string Key = "test-key-0000";
    private static readonly DateOnly Day = new(2017, 2, 27);

    private static async Task<List<(LogLevel Level, string Message)>> RunPrimary(string? remaining)
    {
        var log = new CapturingLogger<MarsRoverPhotosProvider>();
        var handler = RecordingHandler.Json(Fixture.Text("primary_photos_ok.json"), remaining: remaining);
        var provider = new MarsRoverPhotosProvider(new StubHttpClientFactory(handler), Options.Create(new NasaOptions { ApiKey = Key }), log);

        await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);
        return log.Entries;
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Low_remaining_quota_logs_a_warning_with_the_count(string remaining)
    {
        var entries = await RunPrimary(remaining);

        var warning = Assert.Single(entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(remaining, warning.Message);
        Assert.DoesNotContain(Key, warning.Message);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("1999")]
    [InlineData("not-a-number")]
    [InlineData(null)]
    public async Task Healthy_or_missing_quota_does_not_warn(string? remaining)
    {
        var entries = await RunPrimary(remaining);

        Assert.DoesNotContain(entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task The_fallback_source_warns_too_and_still_returns_photos()
    {
        var log = new CapturingLogger<RawImageFeedProvider>();
        var handler = RecordingHandler.Json(Fixture.Text("rawfeed_sol1621.json"), remaining: "5");
        var provider = new RawImageFeedProvider(new StubHttpClientFactory(handler), Options.Create(new NasaOptions { ApiKey = Key }), log);

        var result = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.IsType<MarsPhotos.Application.Abstractions.ProviderResult.Photos>(result);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_failed_request_with_low_quota_still_warns_and_still_reports_the_failure()
    {
        var log = new CapturingLogger<MarsRoverPhotosProvider>();
        var handler = new RecordingHandler((_, _) => Task.FromResult(RecordingHandler.Response("{}", "application/json", (System.Net.HttpStatusCode)429, remaining: "0")));
        var provider = new MarsRoverPhotosProvider(new StubHttpClientFactory(handler), Options.Create(new NasaOptions { ApiKey = Key }), log);

        var result = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.Equal(ErrorCodes.ApiRateLimited, Assert.IsType<MarsPhotos.Application.Abstractions.ProviderResult.Unavailable>(result).ErrorCode);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
    }
}
