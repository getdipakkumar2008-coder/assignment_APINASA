using MarsPhotos.Application.Abstractions;
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
}
