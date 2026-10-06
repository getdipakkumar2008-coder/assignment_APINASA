using MarsPhotos.Application;
using MarsPhotos.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Configuration;

public class RedactionTests
{
    [Theory]
    [InlineData("https://api.nasa.gov/x?earth_date=2017-02-27&api_key=test-key-0000", "https://api.nasa.gov/x?earth_date=2017-02-27&api_key=***")]
    [InlineData("https://api.nasa.gov/x?api_key=test-key-0000&earth_date=2017-02-27", "https://api.nasa.gov/x?api_key=***&earth_date=2017-02-27")]
    [InlineData("GET https://x/y?API_KEY=test-key-0000 failed", "GET https://x/y?API_KEY=*** failed")]
    [InlineData("https://x/y?apikey=test-key-0000", "https://x/y?apikey=***")]
    [InlineData("no secrets here", "no secrets here")]
    public void RedactApiKey_masks_the_key_value(string input, string expected)
    {
        Assert.Equal(expected, Redaction.RedactApiKey(input));
    }

    [Fact]
    public void RedactApiKey_handles_null_and_empty()
    {
        Assert.Equal(string.Empty, Redaction.RedactApiKey(null));
        Assert.Equal(string.Empty, Redaction.RedactApiKey(string.Empty));
    }

    [Fact]
    public void Redact_also_masks_a_bare_secret_value()
    {
        var text = Redaction.Redact("failed with key test-key-0000 somewhere", "test-key-0000");

        Assert.DoesNotContain("test-key-0000", text);
    }
}

public class DotEnvLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mp-env-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _keys = [];

    public DotEnvLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var k in _keys)
        {
            Environment.SetEnvironmentVariable(k, null);
        }

        Directory.Delete(_dir, recursive: true);
    }

    private string Key(string suffix)
    {
        var key = "MP_TEST_" + Guid.NewGuid().ToString("N")[..8] + "_" + suffix;
        _keys.Add(key);
        return key;
    }

    [Fact]
    public void Loads_pairs_ignoring_comments_blank_lines_export_and_quotes()
    {
        var a = Key("A");
        var b = Key("B");
        var c = Key("C");
        File.WriteAllText(Path.Combine(_dir, ".env"), $"# comment\n\n{a}=one\nexport {b}=\"two words\"\n{c}='three'\nnot a pair\n");

        var count = DotEnvLoader.Load(_dir);

        Assert.Equal(3, count);
        Assert.Equal("one", Environment.GetEnvironmentVariable(a));
        Assert.Equal("two words", Environment.GetEnvironmentVariable(b));
        Assert.Equal("three", Environment.GetEnvironmentVariable(c));
    }

    [Fact]
    public void Does_not_override_existing_environment_variables()
    {
        var a = Key("EXISTING");
        Environment.SetEnvironmentVariable(a, "from-environment");
        File.WriteAllText(Path.Combine(_dir, ".env"), $"{a}=from-file\n");

        var count = DotEnvLoader.Load(_dir);

        Assert.Equal(0, count);
        Assert.Equal("from-environment", Environment.GetEnvironmentVariable(a));
    }

    [Fact]
    public void Finds_the_file_in_a_parent_directory_inside_the_repository()
    {
        var a = Key("PARENT");
        Directory.CreateDirectory(Path.Combine(_dir, ".git")); // repository root marker
        File.WriteAllText(Path.Combine(_dir, ".env"), $"{a}=found\n");
        var child = Directory.CreateDirectory(Path.Combine(_dir, "sub", "deeper")).FullName;

        DotEnvLoader.Load(child);

        Assert.Equal("found", Environment.GetEnvironmentVariable(a));
    }

    [Fact]
    public void Never_loads_an_env_file_from_above_the_repository_root()
    {
        var a = Key("ABOVE");
        File.WriteAllText(Path.Combine(_dir, ".env"), $"{a}=stray\n"); // outside the repository
        var repo = Directory.CreateDirectory(Path.Combine(_dir, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var inside = Directory.CreateDirectory(Path.Combine(repo, "src", "app")).FullName;

        Assert.Equal(0, DotEnvLoader.Load(inside));
        Assert.Null(Environment.GetEnvironmentVariable(a));
    }

    [Fact]
    public void Missing_file_is_a_no_op()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git")); // repository root marker stops the search
        Assert.Equal(0, DotEnvLoader.Load(_dir));
    }

    [Fact]
    public void Malformed_lines_never_throw_or_leak_values()
    {
        var a = Key("OK");
        File.WriteAllText(Path.Combine(_dir, ".env"), $"=novalue\n   \n{a}=test-key-0000\n");

        var ex = Record.Exception(() => DotEnvLoader.Load(_dir));

        Assert.Null(ex);
        Assert.Equal("test-key-0000", Environment.GetEnvironmentVariable(a));
    }
}

public class OptionsValidationTests
{
    private static readonly NasaOptionsValidator Nasa = new();
    private static readonly RunOptionsValidator Run = new();

    [Fact]
    public void Missing_key_fails_with_actionable_message_and_no_key_text()
    {
        var result = Nasa.Validate(null, new NasaOptions { ApiKey = null });

        Assert.True(result.Failed);
        var message = string.Join(" ", result.Failures!);
        Assert.Contains("CONFIG_MISSING_KEY", message);
        Assert.Contains("NASA__ApiKey", message);
        Assert.Contains(".env", message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    public void Blank_or_short_key_is_rejected(string key)
    {
        Assert.True(Nasa.Validate(null, new NasaOptions { ApiKey = key }).Failed);
    }

    [Fact]
    public void Valid_key_and_defaults_pass()
    {
        Assert.True(Nasa.Validate(null, new NasaOptions { ApiKey = "test-key-0000" }).Succeeded);
        Assert.True(Run.Validate(null, new RunOptions()).Succeeded);
    }

    [Fact]
    public void Non_https_base_urls_are_rejected()
    {
        var result = Nasa.Validate(null, new NasaOptions { ApiKey = "test-key-0000", PrimaryBaseUrl = "http://api.nasa.gov/" });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(nameof(RunOptions.PhotosPerDate), 2)]
    [InlineData(nameof(RunOptions.PhotosPerDate), 6)]
    [InlineData(nameof(RunOptions.MaxRetries), -1)]
    [InlineData(nameof(RunOptions.MaxRetries), 6)]
    [InlineData(nameof(RunOptions.MaxParallelDownloads), 0)]
    [InlineData(nameof(RunOptions.MaxParallelDownloads), 6)]
    [InlineData(nameof(RunOptions.LookupTimeoutSeconds), 0)]
    [InlineData(nameof(RunOptions.DownloadTimeoutSeconds), -5)]
    public void Out_of_range_run_options_are_rejected_and_named(string property, int value)
    {
        var options = new RunOptions();
        typeof(RunOptions).GetProperty(property)!.SetValue(options, value);

        var result = Run.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(property, string.Join(" ", result.Failures!));
    }

    [Fact]
    public void Unknown_rover_and_empty_host_list_are_rejected()
    {
        Assert.True(Run.Validate(null, new RunOptions { Rover = "sojourner" }).Failed);
        Assert.True(Run.Validate(null, new RunOptions { AllowedImageHosts = [] }).Failed);
    }

    [Fact]
    public void Host_startup_without_a_key_fails_with_the_actionable_message()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection().AddMarsPhotos(config);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NasaOptions>>().Value);

        Assert.Contains("NASA__ApiKey", ex.Message);
    }

    [Fact]
    public void Key_is_bound_from_the_NASA__ApiKey_environment_style_setting_and_never_echoed_on_failure()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nasa:ApiKey"] = "test-key-0000",
                ["Run:PhotosPerDate"] = "9",
            })
            .Build();
        using var provider = new ServiceCollection().AddMarsPhotos(config).BuildServiceProvider();

        Assert.Equal("test-key-0000", provider.GetRequiredService<IOptions<NasaOptions>>().Value.ApiKey);
        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RunOptions>>().Value);
        Assert.DoesNotContain("test-key-0000", ex.Message);
    }
}
