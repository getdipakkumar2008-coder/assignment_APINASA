using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Cli;
using MarsPhotos.Tests.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Configuration;

/// <summary>Precedence is command line &gt; extra settings &gt; environment variables &gt; appsettings (FR-024).</summary>
public sealed class ConfigurationPrecedenceTests : IDisposable
{
    // Run:MaxApiLines is not touched by any other test, so this process-wide variable cannot disturb them.
    private const string EnvName = "Run__MaxApiLines";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mp-prec-" + Guid.NewGuid().ToString("N"));

    public ConfigurationPrecedenceTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "dates.txt"), "02/27/17\n");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvName, null);
        Directory.Delete(_dir, recursive: true);
    }

    private async Task<(int Code, RunOptions Options)> Run(string[] args, Dictionary<string, string?> settings)
    {
        RunOptions? captured = null;
        var all = new Dictionary<string, string?>
        {
            ["Nasa:ApiKey"] = "test-key-0000",
            ["Run:OutputRoot"] = Path.Combine(_dir, "photos"),
            ["Run:InputFile"] = Path.Combine(_dir, "dates.txt"),
        };
        foreach (var (k, v) in settings)
        {
            all[k] = v;
        }

        var host = new CliHostOptions
        {
            LoadDotEnv = false,
            Settings = all,
            ConfigureServices = services =>
            {
                services.AddSingleton<IPhotoProvider>(sp =>
                {
                    captured = sp.GetRequiredService<IOptions<RunOptions>>().Value;
                    return FakeProvider.WithPhotos(5);
                });
                services.AddSingleton<IPhotoDownloader>(sp => new FakeDownloader(sp.GetRequiredService<IPhotoStore>()));
            },
        };
        var code = await CliApp.RunAsync(args, new StringWriter(), new StringWriter(), host);
        return (code, captured ?? throw new InvalidOperationException("options were not captured"));
    }

    [Fact]
    public async Task Defaults_come_from_the_options_class_and_appsettings()
    {
        var (code, options) = await Run([], []);

        Assert.Equal(0, code);
        Assert.Equal(5, options.PhotosPerDate);
        Assert.Equal("curiosity", options.Rover);
        Assert.Equal(100, options.MaxApiLines);
    }

    [Fact]
    public async Task Environment_variables_override_defaults()
    {
        Environment.SetEnvironmentVariable(EnvName, "7");

        var (_, options) = await Run([], []);

        Assert.Equal(7, options.MaxApiLines);
    }

    [Fact]
    public async Task Extra_settings_override_environment_variables()
    {
        Environment.SetEnvironmentVariable(EnvName, "7");

        var (_, options) = await Run([], new() { ["Run:MaxApiLines"] = "8" });

        Assert.Equal(8, options.MaxApiLines);
    }

    [Fact]
    public async Task Command_line_options_override_everything()
    {
        var (_, options) = await Run(["--count", "3", "--rover", "curiosity"], new() { ["Run:PhotosPerDate"] = "4" });

        Assert.Equal(3, options.PhotosPerDate);
    }

    [Fact]
    public async Task Out_of_range_values_from_any_layer_are_rejected_naming_the_setting()
    {
        var errors = new StringWriter();
        var host = new CliHostOptions
        {
            LoadDotEnv = false,
            Settings = new Dictionary<string, string?> { ["Nasa:ApiKey"] = "test-key-0000", ["Run:MaxRetries"] = "99" },
        };

        var code = await CliApp.RunAsync([], new StringWriter(), errors, host);

        Assert.Equal(1, code);
        Assert.Contains("Run:MaxRetries", errors.ToString());
        Assert.DoesNotContain("test-key-0000", errors.ToString());
    }
}
