using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Processing;
using MarsPhotos.Application.Providers;
using MarsPhotos.Infrastructure.Download;
using MarsPhotos.Infrastructure.Http;
using MarsPhotos.Infrastructure.Input;
using MarsPhotos.Infrastructure.Providers;
using MarsPhotos.Infrastructure.Security;
using MarsPhotos.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Infrastructure.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers options (validated at startup, no demo-key fallback) and shared infrastructure.
    /// The NASA key comes from <c>NASA__ApiKey</c> (environment / .env / user-secrets) only.
    /// </summary>
    public static IServiceCollection AddMarsPhotos(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NasaOptions>()
            .Bind(configuration.GetSection(NasaOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<RunOptions>()
            .Bind(configuration.GetSection(RunOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NasaOptions>, NasaOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RunOptions>, RunOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new UrlPolicy(sp.GetRequiredService<IOptions<RunOptions>>().Value.AllowedImageHosts));

        services.TryAddTransient<ProcessDatesUseCase>();
        services.TryAddSingleton<IDateSource, FileDateSource>();
        services.TryAddSingleton<IPhotoStore, FileSystemPhotoStore>();
        services.TryAddSingleton<IPhotoDownloader, HttpPhotoDownloader>();

        // Redirects are followed manually by the downloader so every hop is checked against the URL policy.
        services.AddHttpClient(HttpPhotoDownloader.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MarsPhotoDownloader/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .AddPhotoResilience(
                run => TimeSpan.FromSeconds(run.DownloadTimeoutSeconds),
                run => TimeSpan.FromSeconds(run.DownloadTimeoutSeconds * 2));

        // Photo sources: the Mars Rover Photos service first, NASA's raw-image feed as automatic fallback (OQ-10).
        foreach (var clientName in new[] { MarsRoverPhotosProvider.HttpClientName, RawImageFeedProvider.HttpClientName })
        {
            services.AddHttpClient(clientName, client => client.DefaultRequestHeaders.UserAgent.ParseAdd("MarsPhotoDownloader/1.0"))
                .AddPhotoResilience(
                    run => TimeSpan.FromSeconds(run.LookupTimeoutSeconds),
                    run => TimeSpan.FromSeconds(run.TotalBudgetSeconds));
        }

        services.TryAddSingleton<MarsRoverPhotosProvider>();
        services.TryAddSingleton<RawImageFeedProvider>();
        services.TryAddSingleton<IPhotoProvider>(sp => new ProviderChain(
            [sp.GetRequiredService<MarsRoverPhotosProvider>(), sp.GetRequiredService<RawImageFeedProvider>()],
            sp.GetRequiredService<ILogger<ProviderChain>>()));

        // The key must never reach logs through request URLs: keep HttpClient's own request logging at Warning.
        services.AddLogging(b =>
        {
            b.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
            b.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
            b.AddFilter("Polly", LogLevel.Warning);
        });

        return services;
    }
}
