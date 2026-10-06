using System.Text.Json;
using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Output;
using MarsPhotos.Application.Processing;
using MarsPhotos.Domain;
using MarsPhotos.Tests.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Logging;

/// <summary>AC-8 / SC-007: the key must not escape through logs, results, JSON or console output, even when a failure message contains it.</summary>
public class NoSecretInLogsTests
{
    private const string Key = "test-key-0000";

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
    }

    [Fact]
    [Trait("AC", "AC-8")]
    public async Task Keys_in_provider_download_and_exception_messages_never_reach_logs_or_outputs()
    {
        var leak = $"GET https://api.nasa.gov/mars-photos/api/v1/rovers/curiosity/photos?earth_date=2017-02-27&api_key={Key} failed";
        var provider = new FakeProvider((rover, date, _) => date.Year switch
        {
            2018 => new ProviderResult.Unavailable(ErrorCodes.ApiUnavailable, leak, true),
            2016 => throw new InvalidOperationException(leak),
            _ => new ProviderResult.Photos(FakeProvider.Photos(rover, date, 3), 3),
        });
        var store = new InMemoryStore();
        var downloader = new FakeDownloader(store)
        {
            Fail = p => p.Id.EndsWith("02", StringComparison.Ordinal)
                ? DownloadResult(leak)
                : p.Id.EndsWith("03", StringComparison.Ordinal) ? throw new InvalidOperationException(leak) : null,
        };
        var logger = new CapturingLogger<ProcessDatesUseCase>();
        var useCase = new ProcessDatesUseCase(provider, downloader, store, FixedClock.Oct2026, Options.Create(new RunOptions()), logger);

        var summary = await useCase.RunAsync([
            new DateInputLine(1, "02/27/17"),
            new DateInputLine(2, "June 2, 2018"),
            new DateInputLine(3, "Jul-13-2016"),
        ]);

        var everything = string.Join('\n', logger.Lines)
            + ConsoleSummaryRenderer.Render(summary)
            + RunSummaryJson.Serialize(summary)
            + JsonSerializer.Serialize(summary);

        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain(Key, everything);
        Assert.Contains("api_key=***", everything);
    }

    private static DownloadResult DownloadResult(string message) =>
        Application.Abstractions.DownloadResult.Fail(ErrorCodes.DownloadFailed, message);
}
