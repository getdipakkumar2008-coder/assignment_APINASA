using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Processing;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Storage;
using MarsPhotos.Tests.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Storage;

/// <summary>Regression tests for code-review finding MEDIUM-3: colliding names, duplicate ids and concurrent writers.</summary>
public sealed class StoreConcurrencyTests : IDisposable
{
    private static readonly DateOnly Day = new(2017, 2, 27);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-conc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private FileSystemPhotoStore Store() => new(Options.Create(new RunOptions { OutputRoot = _root }));

    [Theory]
    [InlineData("1621MD0006280000200678E01_DXXX")]
    [InlineData("102693")]
    [InlineData("a-b_c.d")]
    public void StemFor_keeps_lossless_ids_unchanged(string id)
    {
        Assert.Equal(id, FileNames.StemFor(id));
    }

    [Fact]
    public void StemFor_gives_different_ids_different_names_even_when_sanitizing_would_merge_them()
    {
        var a = FileNames.StemFor("a/b");
        var b = FileNames.StemFor("a_b");
        var c = FileNames.StemFor("a b");

        Assert.Equal("a_b", b); // lossless id keeps its name
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith("a_b_", a);
        Assert.Equal(a, FileNames.StemFor("a/b")); // deterministic across runs
    }

    [Fact]
    public void StemFor_differentiates_long_ids_that_would_truncate_to_the_same_prefix()
    {
        var first = FileNames.StemFor(new string('x', 150) + "1");
        var second = FileNames.StemFor(new string('x', 150) + "2");

        Assert.NotEqual(first, second);
        Assert.True(first.Length <= 100 && second.Length <= 100);
    }

    [Fact]
    public async Task The_use_case_downloads_a_photo_listed_twice_only_once()
    {
        var provider = new FakeProvider((rover, date, _) =>
        {
            var one = FakeProvider.Photos(rover, date, 1)[0];
            return new ProviderResult.Photos([one, one, one], 3);
        });
        var store = new InMemoryStore();
        var downloader = new FakeDownloader(store);
        var useCase = new ProcessDatesUseCase(provider, downloader, store, FixedClock.Oct2026, Options.Create(new RunOptions()), NullLogger<ProcessDatesUseCase>.Instance);

        var summary = await useCase.RunAsync([new DateInputLine(1, "02/27/17")]);

        Assert.Equal(DateResultStatus.Success, summary.Results[0].Status);
        Assert.Equal(1, summary.Results[0].Downloaded);
        Assert.Single(downloader.Calls);
    }

    [Fact]
    public async Task Distinct_ids_that_sanitize_alike_are_both_stored_not_silently_merged()
    {
        var provider = new FakeProvider((rover, date, _) => new ProviderResult.Photos(
        [
            new RoverPhoto("a/b", null, "C", new Uri("https://mars.nasa.gov/1.jpg"), date, rover),
            new RoverPhoto("a_b", null, "C", new Uri("https://mars.nasa.gov/2.jpg"), date, rover),
            new RoverPhoto("a b", null, "C", new Uri("https://mars.nasa.gov/3.jpg"), date, rover),
        ], 3));
        var store = new InMemoryStore();
        var useCase = new ProcessDatesUseCase(provider, new FakeDownloader(store), store, FixedClock.Oct2026, Options.Create(new RunOptions()), NullLogger<ProcessDatesUseCase>.Instance);

        var summary = await useCase.RunAsync([new DateInputLine(1, "02/27/17")]);

        Assert.Equal(3, summary.Results[0].Downloaded);
        Assert.Equal(3, store.Files.Distinct().Count());
    }

    [Fact]
    public async Task A_part_file_owned_by_another_writer_is_neither_deleted_nor_overwritten()
    {
        var store = Store();
        var folder = Directory.CreateDirectory(Path.Combine(_root, "2017-02-27")).FullName;
        var part = Path.Combine(folder, "a.jpg.part");
        await using (var other = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await other.WriteAsync(new byte[] { 1, 2, 3 });

            var ex = await Assert.ThrowsAsync<PhotoStoreException>(
                () => store.WriteAsync(Day, "a.jpg", new MemoryStream(new byte[100]), 1000, CancellationToken.None));

            Assert.Equal(ErrorCodes.IoError, ex.ErrorCode);
            Assert.True(File.Exists(part), "the other writer's .part file must survive");
        }

        Assert.False(File.Exists(Path.Combine(folder, "a.jpg")));
    }

    [Fact]
    public async Task A_photo_finished_by_someone_else_during_the_download_is_never_overwritten()
    {
        var store = Store();
        var folder = Path.Combine(_root, "2017-02-27");
        var final = Path.Combine(folder, "race.jpg");
        var source = new RacingStream(() =>
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(final, [9, 9, 9, 9]); // another run completed this photo first
        });

        var ex = await Assert.ThrowsAsync<PhotoStoreException>(
            () => store.WriteAsync(Day, "race.jpg", source, 1000, CancellationToken.None));

        Assert.Equal(ErrorCodes.IoError, ex.ErrorCode);
        Assert.Equal([9, 9, 9, 9], await File.ReadAllBytesAsync(final));
        Assert.Empty(Directory.GetFiles(folder, "*.part"));
    }

    [Fact]
    public async Task Parallel_writers_to_different_names_all_succeed_and_leave_no_part_files()
    {
        var store = Store();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            store.WriteAsync(Day, $"p{i}.jpg", new MemoryStream(new byte[2000 + i]), 100_000, CancellationToken.None)));

        Assert.Equal(20, results.Length);
        Assert.Equal(20, Directory.GetFiles(_root, "*.jpg", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.GetFiles(_root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Parallel_writers_to_the_same_name_produce_one_valid_file_and_clean_failures()
    {
        var store = Store();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                return await store.WriteAsync(Day, "same.jpg", new MemoryStream(new byte[5000]), 100_000, CancellationToken.None);
            }
            catch (PhotoStoreException)
            {
                return -1L;
            }
        }));

        Assert.Contains(outcomes, o => o == 5000);
        var final = Path.Combine(_root, "2017-02-27", "same.jpg");
        Assert.Equal(5000, new FileInfo(final).Length);
        Assert.Empty(Directory.GetFiles(_root, "*.part", SearchOption.AllDirectories));
    }

    /// <summary>Yields a few bytes, runs a side effect, then ends.</summary>
    private sealed class RacingStream(Action beforeEnd) : Stream
    {
        private int _step;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_step++ == 0)
            {
                buffer.Span[..10].Fill(7);
                return ValueTask.FromResult(10);
            }

            beforeEnd();
            return ValueTask.FromResult(0);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
