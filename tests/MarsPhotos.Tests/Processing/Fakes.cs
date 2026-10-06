using System.Collections.Concurrent;
using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;

namespace MarsPhotos.Tests.Processing;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public static readonly FixedClock Oct2026 = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FakeProvider(Func<string, DateOnly, int, ProviderResult> answer) : IPhotoProvider
{
    private readonly ConcurrentQueue<(string Rover, DateOnly Date, int Max)> _calls = new();

    public string Name => "FakeProvider";

    public IReadOnlyCollection<(string Rover, DateOnly Date, int Max)> Calls => _calls;

    public Task<ProviderResult> GetPhotosAsync(string rover, DateOnly date, int maxPhotos, CancellationToken cancellationToken)
    {
        _calls.Enqueue((rover, date, maxPhotos));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(answer(rover, date, maxPhotos));
    }

    /// <summary>Always returns <paramref name="count"/> photos with numeric ids that depend on the date.</summary>
    public static FakeProvider WithPhotos(int count = 5, int available = 20) => new((rover, date, _) =>
        new ProviderResult.Photos(Photos(rover, date, count), available) { Provider = "FakeProvider" });

    public static IReadOnlyList<RoverPhoto> Photos(string rover, DateOnly date, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new RoverPhoto(
                $"{date:yyyyMMdd}{i:00}",
                1000 + i,
                "CAM",
                new Uri($"https://mars.nasa.gov/msl-raw-images/{date:yyyyMMdd}{i:00}.jpg"),
                date,
                rover))
            .ToList();
}

internal sealed class InMemoryStore : IPhotoStore
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public bool WritableFails { get; set; }

    public IReadOnlyCollection<string> Files => _files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public bool Exists(DateOnly date, string fileName) =>
        _files.TryGetValue(RelativePath(date, fileName), out var b) && b.Length > 0;

    public string RelativePath(DateOnly date, string fileName) => $"{date:yyyy-MM-dd}/{fileName}";

    public void EnsureWritable()
    {
        if (WritableFails)
        {
            throw new FatalRunException(ErrorCodes.IoError, "Output folder is not writable.");
        }
    }

    public async Task<long> WriteAsync(DateOnly date, string fileName, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        if (!_files.TryAdd(RelativePath(date, fileName), ms.ToArray()))
        {
            throw new PhotoStoreException(ErrorCodes.IoError, "exists");
        }

        return ms.Length;
    }

    public void Seed(DateOnly date, string fileName, int bytes = 10) =>
        _files[RelativePath(date, fileName)] = new byte[bytes];
}

/// <summary>Downloader that "downloads" fixed bytes into the supplied store, with optional failure injection.</summary>
internal sealed class FakeDownloader(IPhotoStore store) : IPhotoDownloader
{
    private int _current;
    private int _maxConcurrent;
    private readonly ConcurrentQueue<string> _calls = new();

    public Func<RoverPhoto, DownloadResult?>? Fail { get; set; }

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public IReadOnlyCollection<string> Calls => _calls;

    public int MaxConcurrent => _maxConcurrent;

    public async Task<DownloadResult> DownloadAsync(RoverPhoto photo, DateOnly date, string fileName, CancellationToken cancellationToken)
    {
        _calls.Enqueue(fileName);
        var now = Interlocked.Increment(ref _current);
        InterlockedMax(ref _maxConcurrent, now);
        try
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            var failure = Fail?.Invoke(photo);
            if (failure is not null)
            {
                return failure;
            }

            var bytes = await store.WriteAsync(date, fileName, new MemoryStream(new byte[2048]), 1_000_000, cancellationToken);
            return DownloadResult.Ok(bytes);
        }
        finally
        {
            Interlocked.Decrement(ref _current);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
