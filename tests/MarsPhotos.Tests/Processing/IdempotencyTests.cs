using MarsPhotos.Application;
using MarsPhotos.Application.Processing;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Processing;

/// <summary>Runs the real use case against the real file store to prove re-runs are safe (FR-015, FR-016, NFR-3, AC-5).</summary>
public sealed class IdempotencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-idem-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private (ProcessDatesUseCase UseCase, FakeDownloader Downloader) Build(FakeProvider provider)
    {
        var options = Options.Create(new RunOptions { OutputRoot = _root });
        var store = new FileSystemPhotoStore(options);
        var downloader = new FakeDownloader(store);
        return (new ProcessDatesUseCase(provider, downloader, store, FixedClock.Oct2026, options, NullLogger<ProcessDatesUseCase>.Instance), downloader);
    }

    private static IReadOnlyList<DateInputLine> Lines(params string[] texts) =>
        texts.Select((t, i) => new DateInputLine(i + 1, t)).ToList();

    private Dictionary<string, DateTime> Snapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => File.GetLastWriteTimeUtc(f));

    [Fact]
    [Trait("AC", "AC-5")]
    public async Task A_second_run_downloads_nothing_and_leaves_files_untouched()
    {
        var (useCase, downloader) = Build(FakeProvider.WithPhotos(5));
        var input = Lines("02/27/17", "June 2, 2018", "Jul-13-2016", "April 31, 2018");

        var first = await useCase.RunAsync(input);
        var before = Snapshot();
        await Task.Delay(50); // make any rewrite visible in timestamps
        var callsAfterFirst = downloader.Calls.Count;
        var second = await useCase.RunAsync(input);

        Assert.Equal(15, first.Totals.Downloaded);
        Assert.Equal(0, first.Totals.Skipped);
        Assert.Equal(0, second.Totals.Downloaded);
        Assert.Equal(15, second.Totals.Skipped);
        Assert.Equal(callsAfterFirst, downloader.Calls.Count); // no new download attempts at all
        Assert.Equal(before, Snapshot());
        Assert.All(second.Results.Take(3), r => Assert.Equal(DateResultStatus.Success, r.Status));
    }

    [Fact]
    [Trait("AC", "AC-5")]
    public async Task Deleting_one_photo_downloads_exactly_that_photo_next_time()
    {
        var (useCase, downloader) = Build(FakeProvider.WithPhotos(5));
        var input = Lines("02/27/17");
        await useCase.RunAsync(input);
        var victim = Directory.GetFiles(_root, "*.jpg", SearchOption.AllDirectories).OrderBy(f => f).Skip(2).First();
        File.Delete(victim);
        var calls = downloader.Calls.Count;

        var again = await useCase.RunAsync(input);

        Assert.Equal(1, again.Totals.Downloaded);
        Assert.Equal(4, again.Totals.Skipped);
        Assert.Equal(calls + 1, downloader.Calls.Count);
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public async Task A_zero_byte_leftover_is_downloaded_again()
    {
        var (useCase, _) = Build(FakeProvider.WithPhotos(3));
        var input = Lines("02/27/17");
        await useCase.RunAsync(input);
        var victim = Directory.GetFiles(_root, "*.jpg", SearchOption.AllDirectories).OrderBy(f => f).First();
        File.WriteAllBytes(victim, []);

        var again = await useCase.RunAsync(input);

        Assert.Equal(1, again.Totals.Downloaded);
        Assert.Equal(2048, new FileInfo(victim).Length);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task A_stale_part_file_never_counts_as_a_finished_photo()
    {
        var (useCase, _) = Build(FakeProvider.WithPhotos(3));
        var folder = Directory.CreateDirectory(Path.Combine(_root, "2017-02-27")).FullName;
        var photos = FakeProvider.Photos("curiosity", new DateOnly(2017, 2, 27), 3);
        await File.WriteAllBytesAsync(Path.Combine(folder, photos[0].Id + ".jpg.part"), new byte[5000]);

        var summary = await useCase.RunAsync(Lines("02/27/17"));

        Assert.Equal(3, summary.Totals.Downloaded);
        Assert.Equal(0, summary.Totals.Skipped);
        Assert.Empty(Directory.GetFiles(_root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Adding_one_date_downloads_only_that_dates_photos()
    {
        var (useCase, downloader) = Build(FakeProvider.WithPhotos(5));
        await useCase.RunAsync(Lines("02/27/17", "Jul-13-2016"));
        var calls = downloader.Calls.Count;

        var summary = await useCase.RunAsync(Lines("02/27/17", "Jul-13-2016", "June 2, 2018"));

        Assert.Equal(5, summary.Totals.Downloaded);
        Assert.Equal(10, summary.Totals.Skipped);
        Assert.Equal(calls + 5, downloader.Calls.Count);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task An_interrupted_run_is_completed_by_the_next_run_without_redoing_finished_photos()
    {
        var (useCase, downloader) = Build(FakeProvider.WithPhotos(5));
        downloader.Fail = p => p.Id.EndsWith("04", StringComparison.Ordinal) ? throw new IOException("network dropped") : null;
        var input = Lines("02/27/17");

        var first = await useCase.RunAsync(input);
        downloader.Fail = null;
        var second = await useCase.RunAsync(input);

        Assert.Equal(DateResultStatus.PartialFailure, first.Results[0].Status);
        Assert.Equal(4, first.Totals.Downloaded);
        Assert.Equal(1, second.Totals.Downloaded);
        Assert.Equal(4, second.Totals.Skipped);
        Assert.Equal(DateResultStatus.Success, second.Results[0].Status);
        Assert.Empty(Directory.GetFiles(_root, "*.part", SearchOption.AllDirectories));
    }
}
