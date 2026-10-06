using MarsPhotos.Application;
using MarsPhotos.Infrastructure.Security;
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

        // The key must never reach logs through request URLs: keep HttpClient's own request logging at Warning.
        services.AddLogging(b =>
        {
            b.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
            b.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
        });

        return services;
    }
}
