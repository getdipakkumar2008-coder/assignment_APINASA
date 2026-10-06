using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Storage;

public sealed class FileSystemPhotoStoreTests : IDisposable
{
    private static readonly DateOnly Day = new(2017, 2, 27);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private FileSystemPhotoStore Store() => new(Options.Create(new RunOptions { OutputRoot = _root }));

    private static MemoryStream Bytes(int count, byte value = 7) => new(Enumerable.Repeat(value, count).ToArray());

    private string[] AllFiles() => Directory.Exists(_root)
        ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        : [];

    [Fact]
    [Trait("AC", "AC-4")]
    public async Task Writes_a_new_photo_under_the_date_folder()
    {
        var store = Store();

        var written = await store.WriteAsync(Day, "123.jpg", Bytes(1000), 10_000, CancellationToken.None);

        Assert.Equal(1000, written);
        var path = Path.Combine(_root, "2017-02-27", "123.jpg");
        Assert.Equal(1000, new FileInfo(path).Length);
        Assert.Equal([path], AllFiles());
        Assert.Equal("2017-02-27/123.jpg", store.RelativePath(Day, "123.jpg"));
    }

    [Fact]
    public async Task Exists_is_true_only_for_complete_non_empty_files()
    {
        var store = Store();
        Assert.False(store.Exists(Day, "a.jpg"));

        await store.WriteAsync(Day, "a.jpg", Bytes(10), 100, CancellationToken.None);
        Assert.True(store.Exists(Day, "a.jpg"));

        Directory.CreateDirectory(Path.Combine(_root, "2017-02-27"));
        File.WriteAllBytes(Path.Combine(_root, "2017-02-27", "empty.jpg"), []);
        Assert.False(store.Exists(Day, "empty.jpg"));

        File.WriteAllBytes(Path.Combine(_root, "2017-02-27", "half.jpg.part"), [1, 2, 3]);
        Assert.False(store.Exists(Day, "half.jpg"));
    }

    [Fact]
    public async Task Never_overwrites_an_existing_valid_file()
    {
        var store = Store();
        await store.WriteAsync(Day, "a.jpg", Bytes(10, 1), 100, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<PhotoStoreException>(
            () => store.WriteAsync(Day, "a.jpg", Bytes(20, 2), 100, CancellationToken.None));

        Assert.Equal(ErrorCodes.IoError, ex.ErrorCode);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "2017-02-27", "a.jpg"));
        Assert.Equal(10, bytes.Length);
        Assert.All(bytes, b => Assert.Equal(1, b));
        Assert.Single(AllFiles());
    }

    [Fact]
    public async Task Replaces_a_zero_byte_leftover()
    {
        var store = Store();
        Directory.CreateDirectory(Path.Combine(_root, "2017-02-27"));
        File.WriteAllBytes(Path.Combine(_root, "2017-02-27", "a.jpg"), []);

        await store.WriteAsync(Day, "a.jpg", Bytes(10), 100, CancellationToken.None);

        Assert.Equal(10, new FileInfo(Path.Combine(_root, "2017-02-27", "a.jpg")).Length);
    }

    [Fact]
    public async Task Overwrites_a_stale_part_file()
    {
        var store = Store();
        Directory.CreateDirectory(Path.Combine(_root, "2017-02-27"));
        File.WriteAllBytes(Path.Combine(_root, "2017-02-27", "a.jpg.part"), [9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9]);

        await store.WriteAsync(Day, "a.jpg", Bytes(5), 100, CancellationToken.None);

        Assert.Equal([Path.Combine(_root, "2017-02-27", "a.jpg")], AllFiles());
        Assert.Equal(5, new FileInfo(Path.Combine(_root, "2017-02-27", "a.jpg")).Length);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task A_failing_source_stream_leaves_no_partial_or_final_file()
    {
        var store = Store();

        await Assert.ThrowsAsync<IOException>(
            () => store.WriteAsync(Day, "a.jpg", new FailingStream(failAfter: 2000), 100_000, CancellationToken.None));

        Assert.Empty(AllFiles());
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task Cancellation_mid_copy_leaves_no_files()
    {
        var store = Store();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.WriteAsync(Day, "a.jpg", new FailingStream(failAfter: 2000, cancel: cts), 100_000, cts.Token));

        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Oversized_content_is_rejected_and_cleaned_up()
    {
        var store = Store();

        var ex = await Assert.ThrowsAsync<PhotoStoreException>(
            () => store.WriteAsync(Day, "big.jpg", Bytes(5000), maxBytes: 4096, CancellationToken.None));

        Assert.Equal(ErrorCodes.DownloadTooLarge, ex.ErrorCode);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Empty_content_is_rejected()
    {
        var store = Store();

        var ex = await Assert.ThrowsAsync<PhotoStoreException>(
            () => store.WriteAsync(Day, "none.jpg", Bytes(0), 100, CancellationToken.None));

        Assert.Equal(ErrorCodes.DownloadFailed, ex.ErrorCode);
        Assert.Empty(AllFiles());
    }

    [Theory]
    [InlineData("../evil.jpg")]
    [InlineData("..\\evil.jpg")]
    [InlineData("sub/../../evil.jpg")]
    public async Task Traversal_in_a_file_name_is_rejected(string name)
    {
        var store = Store();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteAsync(Day, name, Bytes(10), 100, CancellationToken.None));

        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Existing_files_are_never_deleted_by_other_writes()
    {
        var store = Store();
        await store.WriteAsync(Day, "keep.jpg", Bytes(10), 100, CancellationToken.None);

        await Assert.ThrowsAsync<PhotoStoreException>(
            () => store.WriteAsync(Day, "big.jpg", Bytes(500), 100, CancellationToken.None));

        Assert.True(store.Exists(Day, "keep.jpg"));
    }

    [Fact]
    public void EnsureWritable_creates_the_root_and_leaves_no_probe_files()
    {
        Store().EnsureWritable();

        Assert.True(Directory.Exists(_root));
        Assert.Empty(AllFiles());
    }

    [Fact]
    public void EnsureWritable_reports_an_unusable_root_as_a_fatal_io_error()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "mp-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "i am a file, not a directory");
        try
        {
            var store = new FileSystemPhotoStore(Options.Create(new RunOptions { OutputRoot = Path.Combine(blocker, "photos") }));

            var ex = Assert.Throws<FatalRunException>(store.EnsureWritable);

            Assert.Equal(ErrorCodes.IoError, ex.ErrorCode);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    private sealed class FailingStream(int failAfter, CancellationTokenSource? cancel = null) : Stream
    {
        private int _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_served >= failAfter)
            {
                if (cancel is not null)
                {
                    await cancel.CancelAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                throw new IOException("connection dropped");
            }

            var n = Math.Min(buffer.Length, Math.Min(1024, failAfter - _served));
            buffer.Span[..n].Fill(5);
            _served += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
