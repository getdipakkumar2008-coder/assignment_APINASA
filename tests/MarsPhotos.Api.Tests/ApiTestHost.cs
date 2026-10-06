using System.Collections.Concurrent;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MarsPhotos.Api.Tests;

public static class TestKey
{
    public const string Value = "test-key-0000";
}

/// <summary>Provider whose answer (and optional blocking) is controlled by the test.</summary>
public sealed class StubProvider(Func<string, DateOnly, int, CancellationToken, Task<ProviderResult>> answer) : IPhotoProvider
{
    private readonly ConcurrentQueue<DateOnly> _dates = new();

    public string Name => "StubProvider";

    public IReadOnlyCollection<DateOnly> Dates => _dates;

    public Task<ProviderResult> GetPhotosAsync(string rover, DateOnly date, int maxPhotos, CancellationToken cancellationToken)
    {
        _dates.Enqueue(date);
        return answer(rover, date, maxPhotos, cancellationToken);
    }

    public static StubProvider WithPhotos(int count = 5) => new((rover, date, _, _) => Task.FromResult<ProviderResult>(
        new ProviderResult.Photos(Photos(rover, date, count), 20) { Provider = "StubProvider" }));

    public static IReadOnlyList<RoverPhoto> Photos(string rover, DateOnly date, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new RoverPhoto($"{date:yyyyMMdd}{i:00}", 1000 + i, "CAM", new Uri($"https://mars.nasa.gov/x/{date:yyyyMMdd}{i:00}.jpg"), date, rover))
            .ToList();
}

/// <summary>Writes fixed image-like bytes to the store instead of using the network.</summary>
public sealed class StubDownloader(IPhotoStore store) : IPhotoDownloader
{
    public async Task<DownloadResult> DownloadAsync(RoverPhoto photo, DateOnly date, string fileName, CancellationToken cancellationToken)
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8 };
        var written = await store.WriteAsync(date, fileName, new MemoryStream(bytes), 1_000_000, cancellationToken);
        return DownloadResult.Ok(written);
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    static ApiFactory()
    {
        // Never load the developer's real .env into test hosts.
        Environment.SetEnvironmentVariable("MARSPHOTOS_SKIP_DOTENV", "1");
    }

    public ApiFactory(StubProvider? provider = null, string? apiKey = TestKey.Value, string? datesText = "02/27/17\nJune 2, 2018\nJul-13-2016\nApril 31, 2018\n")
    {
        Root = Path.Combine(Path.GetTempPath(), "mp-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DatesFile = Path.Combine(Root, "dates.txt");
        if (datesText is not null)
        {
            File.WriteAllText(DatesFile, datesText);
        }

        PhotosRoot = Path.Combine(Root, "photos");
        Provider = provider ?? StubProvider.WithPhotos();
        ApiKey = apiKey;
    }

    public string Root { get; }

    public string DatesFile { get; }

    public string PhotosRoot { get; }

    public StubProvider Provider { get; }

    private string? ApiKey { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Nasa:ApiKey"] = ApiKey,
            ["Run:OutputRoot"] = PhotosRoot,
            ["Run:InputFile"] = DatesFile,
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPhotoProvider>(Provider);
            services.AddSingleton<IPhotoDownloader>(sp => new StubDownloader(sp.GetRequiredService<IPhotoStore>()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(Root))
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // temp cleanup is best effort
            }
        }
    }
}
