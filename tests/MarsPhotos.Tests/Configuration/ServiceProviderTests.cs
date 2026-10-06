using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Processing;
using MarsPhotos.Application.Providers;
using MarsPhotos.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarsPhotos.Tests.Configuration;

public class ServiceProviderTests
{
    internal static ServiceProvider Build(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?> { ["Nasa:ApiKey"] = "test-key-0000" };
        foreach (var (k, v) in extra ?? [])
        {
            settings[k] = v;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddMarsPhotos(config).BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void Infrastructure_ports_resolve_with_a_fake_key()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<IPhotoStore>());
        Assert.NotNull(provider.GetRequiredService<IPhotoDownloader>());
        Assert.NotNull(provider.GetRequiredService<IDateSource>());
    }

    [Fact]
    public void The_use_case_resolves_with_a_fake_key_and_the_chain_is_the_provider()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<ProcessDatesUseCase>());
        Assert.IsType<ProviderChain>(provider.GetRequiredService<IPhotoProvider>());
    }

    [Theory]
    [InlineData("photo-download")]
    [InlineData("mars-photos-primary")]
    [InlineData("raw-image-feed")]
    public void The_real_http_clients_never_follow_redirects(string clientName)
    {
        // The unit tests use fake handlers, so this checks the actual registered handler chain.
        using var provider = Build();
        var handler = provider.GetRequiredService<System.Net.Http.IHttpMessageHandlerFactory>().CreateHandler(clientName);

        System.Net.Http.SocketsHttpHandler? primary = null;
        for (var h = handler; h is not null; h = (h as System.Net.Http.DelegatingHandler)?.InnerHandler)
        {
            primary = h as System.Net.Http.SocketsHttpHandler ?? primary;
        }

        Assert.NotNull(primary);
        Assert.False(primary.AllowAutoRedirect, $"{clientName} must not follow redirects (the key is in the query string / every hop is validated)");
    }

    [Fact]
    public void Resolving_the_use_case_without_a_key_fails_with_the_actionable_message()
    {
        using var provider = Build(new() { ["Nasa:ApiKey"] = string.Empty });

        var ex = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarsPhotos.Application.NasaOptions>>().Value);

        Assert.Contains("NASA__ApiKey", ex.Message);
    }
}
