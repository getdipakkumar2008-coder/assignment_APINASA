using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Processing;
using MarsPhotos.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Processing;

public class ProcessDatesUseCaseTests
{
    private static readonly string[] SampleLines = ["02/27/17", "June 2, 2018", "Jul-13-2016", "April 31, 2018"];

    private static IReadOnlyList<DateInputLine> Lines(params string[] texts) =>
        texts.Select((t, i) => new DateInputLine(i + 1, t)).ToList();

    private static (ProcessDatesUseCase UseCase, InMemoryStore Store, FakeDownloader Downloader) Build(
        IPhotoProvider provider,
        Action<RunOptions>? configure = null,
        InMemoryStore? store = null)
    {
        var options = new RunOptions();
        configure?.Invoke(options);
        store ??= new InMemoryStore();
        var downloader = new FakeDownloader(store);
        var useCase = new ProcessDatesUseCase(
            provider, downloader, store, FixedClock.Oct2026, Options.Create(options), NullLogger<ProcessDatesUseCase>.Instance);
        return (useCase, store, downloader);
    }

    [Fact]
    [Trait("AC", "AC-2")]
    [Trait("AC", "AC-3")]
    [Trait("AC", "AC-4")]
    [Trait("AC", "AC-7")]
    public async Task Sample_dates_yield_three_successes_and_one_invalid_in_input_order()
    {
        var provider = FakeProvider.WithPhotos(5);
        var (useCase, store, _) = Build(provider);

        var summary = await useCase.RunAsync(Lines(SampleLines));

        Assert.Equal(["02/27/17", "June 2, 2018", "Jul-13-2016", "April 31, 2018"], summary.Results.Select(r => r.Input));
        Assert.Equal(
            [DateResultStatus.Success, DateResultStatus.Success, DateResultStatus.Success, DateResultStatus.InvalidDate],
            summary.Results.Select(r => r.Status));
        Assert.Equal(
            [new DateOnly(2017, 2, 27), new DateOnly(2018, 6, 2), new DateOnly(2016, 7, 13)],
            summary.Results.Take(3).Select(r => r.Date!.Value));
        Assert.All(summary.Results.Take(3), r => Assert.Equal(5, r.Downloaded));
        Assert.Null(summary.Results[3].Date);
        Assert.Equal("Day 31 does not exist in April 2018", summary.Results[3].Errors.Single().Message);
        Assert.Equal(new RunTotals(4, 3, 1, 0, 15, 0, 0), summary.Totals);
        Assert.Equal(3, provider.Calls.Count);
        Assert.DoesNotContain(provider.Calls, c => c.Date.Month == 4 && c.Date.Day == 31);
        Assert.Equal(15, store.Files.Count);
        Assert.All(store.Files.Take(5), f => Assert.StartsWith("2016-07-13/", f));
    }

    [Fact]
    public async Task Files_are_named_from_the_sanitized_photo_id_with_a_safe_extension()
    {
        var provider = new FakeProvider((rover, date, _) => new ProviderResult.Photos(
            [new RoverPhoto("../weird id", null, "C", new Uri("https://mars.nasa.gov/a/b.PNG"), date, rover)], 1));
        var (useCase, store, _) = Build(provider);

        await useCase.RunAsync(Lines("02/27/17"));

        var file = Assert.Single(store.Files);
        Assert.StartsWith("2017-02-27/.._weird_id_", file); // lossy sanitizing adds a short hash of the original id
        Assert.EndsWith(".png", file);
    }

    [Fact]
    public async Task Rover_and_photo_count_overrides_reach_the_provider()
    {
        var provider = FakeProvider.WithPhotos(3);
        var (useCase, _, _) = Build(provider);

        await useCase.RunAsync(Lines("02/27/17"), rover: "curiosity", photosPerDate: 3);

        var call = Assert.Single(provider.Calls);
        Assert.Equal("curiosity", call.Rover);
        Assert.Equal(3, call.Max);
    }

    [Fact]
    public async Task Never_downloads_more_than_the_requested_count_even_if_the_provider_returns_more()
    {
        var provider = new FakeProvider((rover, date, _) => new ProviderResult.Photos(FakeProvider.Photos(rover, date, 9), 9));
        var (useCase, store, _) = Build(provider, o => o.PhotosPerDate = 4);

        var summary = await useCase.RunAsync(Lines("02/27/17"));

        Assert.Equal(4, summary.Results[0].Downloaded);
        Assert.Equal(4, store.Files.Count);
    }

    [Fact]
    [Trait("AC", "AC-4")]
    public async Task Out_of_range_dates_make_no_provider_call_and_are_reported()
    {
        var provider = FakeProvider.WithPhotos();
        var (useCase, store, _) = Build(provider);

        var summary = await useCase.RunAsync(Lines("01/01/01", "12/31/2030"));

        Assert.All(summary.Results, r =>
        {
            Assert.Equal(DateResultStatus.OutsideMissionRange, r.Status);
            Assert.Empty(r.Errors);
            Assert.Equal(0, r.Downloaded);
            Assert.Contains("mission", r.Note, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Empty(provider.Calls);
        Assert.Empty(store.Files);
        Assert.Equal(2, summary.Totals.OutsideMissionRange);
    }

    [Fact]
    public async Task In_range_dates_next_to_out_of_range_dates_are_still_looked_up()
    {
        var provider = FakeProvider.WithPhotos();
        var (useCase, _, _) = Build(provider);

        var summary = await useCase.RunAsync(Lines("08/05/12", "08/06/12"));

        Assert.Equal(DateResultStatus.OutsideMissionRange, summary.Results[0].Status);
        Assert.Equal(DateResultStatus.Success, summary.Results[1].Status);
        Assert.Single(provider.Calls);
    }

    [Fact]
    public async Task Duplicate_dates_are_processed_once_and_noted()
    {
        var provider = FakeProvider.WithPhotos();
        var (useCase, _, _) = Build(provider);

        var summary = await useCase.RunAsync(Lines("02/27/17", "2017-02-27", "Feb 27, 2017"));

        Assert.Equal(DateResultStatus.Success, summary.Results[0].Status);
        Assert.Equal(DateResultStatus.DuplicateDate, summary.Results[1].Status);
        Assert.Equal(DateResultStatus.DuplicateDate, summary.Results[2].Status);
        Assert.Contains("line 1", summary.Results[1].Note);
        Assert.Single(provider.Calls);
    }

    [Fact]
    public async Task A_list_with_no_valid_dates_still_produces_a_full_summary_without_lookups()
    {
        var provider = FakeProvider.WithPhotos();
        var (useCase, _, downloader) = Build(provider);

        var summary = await useCase.RunAsync(Lines("April 31, 2018", "abc", "13/45/99"));

        Assert.Equal(3, summary.Results.Count);
        Assert.Empty(provider.Calls);
        Assert.Empty(downloader.Calls);
        Assert.Equal(new RunTotals(3, 0, 3, 0, 0, 0, 0), summary.Totals);
        Assert.Equal(DateResultStatus.UnrecognizedDate, summary.Results[1].Status);
    }

    [Fact]
    public async Task Fewer_photos_than_requested_is_still_a_success()
    {
        var provider = new FakeProvider((rover, date, _) => new ProviderResult.Photos(FakeProvider.Photos(rover, date, 2), 2));
        var (useCase, _, _) = Build(provider);

        var result = (await useCase.RunAsync(Lines("02/27/17"))).Results[0];

        Assert.Equal(DateResultStatus.Success, result.Status);
        Assert.Equal(2, result.PhotosAvailable);
        Assert.Equal(2, result.Downloaded);
    }

    [Fact]
    public async Task No_photos_is_reported_without_an_error()
    {
        var provider = new FakeProvider((_, _, _) => new ProviderResult.NoPhotos { Provider = "FakeProvider" });
        var (useCase, _, _) = Build(provider);

        var result = (await useCase.RunAsync(Lines("02/27/17"))).Results[0];

        Assert.Equal(DateResultStatus.NoPhotos, result.Status);
        Assert.Empty(result.Errors);
        Assert.Equal("FakeProvider", result.Provider);
        Assert.Equal(0, result.PhotosAvailable);
    }

    [Fact]
    public async Task Unavailable_source_is_an_error_for_that_date_only()
    {
        var provider = new FakeProvider((rover, date, _) => date.Year == 2018
            ? new ProviderResult.Unavailable(ErrorCodes.ApiUnavailable, "both sources failed", true)
            : new ProviderResult.Photos(FakeProvider.Photos(rover, date, 3), 3));
        var (useCase, _, _) = Build(provider);

        var summary = await useCase.RunAsync(Lines("02/27/17", "June 2, 2018", "Jul-13-2016"));

        Assert.Equal(
            [DateResultStatus.Success, DateResultStatus.SourceUnavailable, DateResultStatus.Success],
            summary.Results.Select(r => r.Status));
        var error = Assert.Single(summary.Results[1].Errors);
        Assert.Equal(ErrorCodes.ApiUnavailable, error.Code);
        Assert.Equal(6, summary.Totals.Downloaded);
    }

    [Fact]
    public async Task Provider_name_and_fallback_reason_are_carried_into_the_result()
    {
        var provider = new FakeProvider((rover, date, _) => new ProviderResult.Photos(FakeProvider.Photos(rover, date, 3), 3)
        {
            Provider = "RawImageFeed",
            FallbackReason = "primary returned 404",
        });
        var (useCase, _, _) = Build(provider);

        var result = (await useCase.RunAsync(Lines("02/27/17"))).Results[0];

        Assert.Equal("RawImageFeed", result.Provider);
        Assert.Equal("primary returned 404", result.FallbackReason);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task One_failed_download_makes_the_date_a_partial_failure_and_others_are_saved()
    {
        var provider = FakeProvider.WithPhotos(5);
        var (useCase, store, downloader) = Build(provider);
        downloader.Fail = p => p.Id.EndsWith("03", StringComparison.Ordinal)
            ? DownloadResult.Fail(ErrorCodes.DownloadFailed, "connection reset")
            : null;

        var result = (await useCase.RunAsync(Lines("02/27/17"))).Results[0];

        Assert.Equal(DateResultStatus.PartialFailure, result.Status);
        Assert.Equal(4, result.Downloaded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(4, store.Files.Count);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.DownloadFailed, error.Code);
        Assert.Contains("2017022703", error.Message);
        Assert.Contains("connection reset", error.Message);
    }

    [Fact]
    public async Task All_downloads_failing_makes_the_date_failed_but_the_run_continues()
    {
        var provider = FakeProvider.WithPhotos(3);
        var (useCase, _, downloader) = Build(provider);
        downloader.Fail = p => p.EarthDate.Year == 2017 ? DownloadResult.Fail(ErrorCodes.DownloadFailed, "x") : null;

        var summary = await useCase.RunAsync(Lines("02/27/17", "Jul-13-2016"));

        Assert.Equal(DateResultStatus.Failed, summary.Results[0].Status);
        Assert.Equal(DateResultStatus.Success, summary.Results[1].Status);
    }

    [Fact]
    public async Task Unexpected_exceptions_are_contained_per_photo_and_per_date()
    {
        var provider = new FakeProvider((rover, date, _) => date.Year == 2018
            ? throw new InvalidOperationException("provider bug")
            : new ProviderResult.Photos(FakeProvider.Photos(rover, date, 2), 2));
        var (useCase, _, downloader) = Build(provider);
        downloader.Fail = p => p.Id.EndsWith("02", StringComparison.Ordinal) && p.EarthDate.Year == 2016
            ? throw new IOException("disk full")
            : null;

        var summary = await useCase.RunAsync(Lines("02/27/17", "June 2, 2018", "Jul-13-2016"));

        Assert.Equal(DateResultStatus.Success, summary.Results[0].Status);
        Assert.Equal(DateResultStatus.Failed, summary.Results[1].Status);
        Assert.Equal(ErrorCodes.UnexpectedError, summary.Results[1].Errors.Single().Code);
        Assert.Equal(DateResultStatus.PartialFailure, summary.Results[2].Status);
        Assert.Equal(ErrorCodes.IoError, summary.Results[2].Errors.Single().Code);
    }

    [Fact]
    public async Task Cancellation_returns_the_partial_summary_with_remaining_dates_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var provider = new FakeProvider((rover, date, _) =>
        {
            if (date.Year == 2018)
            {
                cts.Cancel();
            }

            return new ProviderResult.Photos(FakeProvider.Photos(rover, date, 2), 2);
        });
        var (useCase, _, _) = Build(provider);

        var summary = await useCase.RunAsync(Lines("02/27/17", "June 2, 2018", "Jul-13-2016", "April 31, 2018"), ct: cts.Token);

        Assert.Equal(DateResultStatus.Success, summary.Results[0].Status);
        Assert.Equal(DateResultStatus.Cancelled, summary.Results[1].Status);
        Assert.Equal(DateResultStatus.Cancelled, summary.Results[2].Status);
        Assert.Equal(DateResultStatus.Cancelled, summary.Results[3].Status);
        Assert.Equal(4, summary.Results.Count);
    }

    [Fact]
    public async Task Downloads_are_bounded_by_the_configured_parallelism()
    {
        var provider = FakeProvider.WithPhotos(5);
        var (useCase, _, downloader) = Build(provider, o => o.MaxParallelDownloads = 2);
        downloader.Delay = TimeSpan.FromMilliseconds(40);

        await useCase.RunAsync(Lines("02/27/17"));

        Assert.True(downloader.MaxConcurrent is >= 1 and <= 2, $"max concurrent was {downloader.MaxConcurrent}");
    }

    [Fact]
    public async Task An_unwritable_output_location_is_fatal_before_any_work()
    {
        var provider = FakeProvider.WithPhotos();
        var (useCase, _, _) = Build(provider, store: new InMemoryStore { WritableFails = true });

        var ex = await Assert.ThrowsAsync<FatalRunException>(() => useCase.RunAsync(Lines("02/27/17")));

        Assert.Equal(ErrorCodes.IoError, ex.ErrorCode);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task Reader_rejected_lines_are_reported_as_unrecognized()
    {
        var provider = FakeProvider.WithPhotos();
        var (useCase, _, _) = Build(provider);

        var summary = await useCase.RunAsync([new DateInputLine(1001, "02/27/17", "Input limit of 1000 lines exceeded")]);

        Assert.Equal(DateResultStatus.UnrecognizedDate, summary.Results[0].Status);
        Assert.Empty(provider.Calls);
    }
}
