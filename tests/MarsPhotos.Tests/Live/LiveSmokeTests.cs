using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Processing;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarsPhotos.Tests.Live;

/// <summary>Skipped unless <c>RUN_LIVE_TESTS=1</c>; calls the real NASA services using the key from the environment / .env (never printed).</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_LIVE_TESTS") != "1")
        {
            Skip = "Live NASA test; set RUN_LIVE_TESTS=1 (and NASA__ApiKey or a .env file) to run.";
        }
    }
}

public sealed class LiveSmokeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-live-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [LiveFact]
    [Trait("AC", "AC-3")]
    [Trait("Category", "Live")]
    public async Task Sample_dates_yield_real_photos_through_the_provider_chain()
    {
        DotEnvLoader.Load();
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Run:OutputRoot"] = _root })
            .Build();
        var services = new ServiceCollection().AddLogging().AddMarsPhotos(config);
        await using var provider = services.BuildServiceProvider();
        var useCase = provider.GetRequiredService<ProcessDatesUseCase>();
        var lines = new[] { "02/27/17", "June 2, 2018", "Jul-13-2016", "April 31, 2018" }
            .Select((t, i) => new DateInputLine(i + 1, t)).ToList();

        var summary = await useCase.RunAsync(lines);

        Assert.Equal(DateResultStatus.InvalidDate, summary.Results[3].Status);
        foreach (var result in summary.Results.Take(3))
        {
            Assert.True(result.Status == DateResultStatus.Success, $"{result.Input}: {result.Status} {string.Join("; ", result.Errors.Select(e => e.Message))}");
            Assert.InRange(result.Downloaded, 3, 5);
            Assert.NotNull(result.Provider);
        }

        var files = Directory.GetFiles(_root, "*.jpg", SearchOption.AllDirectories);
        Assert.True(files.Length >= 9, $"expected at least 9 photos, found {files.Length}");
        Assert.All(files, f => Assert.True(new FileInfo(f).Length > 0));
        Assert.DoesNotContain(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), f => f.EndsWith(".part", StringComparison.Ordinal));

        // Second run downloads nothing (AC-5) against the real store.
        var again = await useCase.RunAsync(lines);
        Assert.Equal(0, again.Totals.Downloaded);
        Assert.Equal(summary.Totals.Downloaded, again.Totals.Skipped);
    }
}
