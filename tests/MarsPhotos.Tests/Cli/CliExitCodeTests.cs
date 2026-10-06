using System.Text.Json;
using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Cli;
using MarsPhotos.Domain;
using MarsPhotos.Tests.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace MarsPhotos.Tests.Cli;

public sealed class CliExitCodeTests : IDisposable
{
    private const string FakeKey = "test-key-0000";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mp-cli-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public CliExitCodeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string DatesFile(string content = "02/27/17\nJune 2, 2018\nJul-13-2016\nApril 31, 2018\n")
    {
        var path = Path.Combine(_dir, "dates.txt");
        File.WriteAllText(path, content);
        return path;
    }

    private Task<int> Run(
        string[] args,
        IPhotoProvider? provider = null,
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configure = null,
        CancellationToken ct = default)
    {
        var all = new Dictionary<string, string?>
        {
            ["Nasa:ApiKey"] = FakeKey,
            ["Run:OutputRoot"] = Path.Combine(_dir, "photos"),
            ["Run:InputFile"] = Path.Combine(_dir, "dates.txt"),
        };
        foreach (var (k, v) in settings ?? [])
        {
            all[k] = v;
        }

        var host = new CliHostOptions
        {
            LoadDotEnv = false,
            Settings = all,
            ConfigureServices = services =>
            {
                if (provider is not null)
                {
                    services.AddSingleton(provider);
                }

                services.AddSingleton<IPhotoDownloader>(sp => new FakeDownloader(sp.GetRequiredService<IPhotoStore>()));
                configure?.Invoke(services);
            },
        };
        return CliApp.RunAsync(args, _out, _err, host, ct);
    }

    [Fact]
    [Trait("AC", "AC-1")]
    [Trait("AC", "AC-7")]
    public async Task A_completed_run_exits_0_and_prints_the_summary()
    {
        DatesFile();

        var code = await Run([], FakeProvider.WithPhotos(5));

        Assert.Equal(0, code);
        var text = _out.ToString();
        foreach (var input in new[] { "02/27/17", "June 2, 2018", "Jul-13-2016", "April 31, 2018" })
        {
            Assert.Contains(input, text);
        }

        Assert.Contains("Totals: 4 lines | 3 valid | 1 invalid", text);
        Assert.True(Directory.Exists(Path.Combine(_dir, "photos", "2017-02-27")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "photos", "2018-04-31")));
        Assert.DoesNotContain(FakeKey, _out.ToString() + _err);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task Partial_failures_still_exit_0()
    {
        DatesFile();
        var provider = new FakeProvider((rover, date, _) => date.Year == 2018
            ? new ProviderResult.Unavailable(ErrorCodes.ApiUnavailable, "down", true)
            : new ProviderResult.Photos(FakeProvider.Photos(rover, date, 3), 3));

        var code = await Run([], provider);

        Assert.Equal(0, code);
        Assert.Contains("SourceUnavailable", _out.ToString());
    }

    [Fact]
    public async Task Missing_key_exits_1_with_guidance_and_no_key_text()
    {
        DatesFile();

        var code = await Run([], FakeProvider.WithPhotos(), new() { ["Nasa:ApiKey"] = string.Empty });

        Assert.Equal(1, code);
        Assert.Contains("NASA__ApiKey", _err.ToString());
        Assert.DoesNotContain(FakeKey, _err.ToString());
        Assert.Equal(string.Empty, _out.ToString());
    }

    [Fact]
    public async Task Missing_input_file_exits_1()
    {
        var code = await Run([], FakeProvider.WithPhotos());

        Assert.Equal(1, code);
        Assert.Contains("dates.txt", _err.ToString());
    }

    [Fact]
    public async Task The_file_option_overrides_the_input_file()
    {
        var other = Path.Combine(_dir, "other.txt");
        File.WriteAllText(other, "02/27/17\n");

        var code = await Run(["--file", other], FakeProvider.WithPhotos());

        Assert.Equal(0, code);
        Assert.Contains("Totals: 1 lines", _out.ToString());
    }

    [Theory]
    [InlineData("--count", "9")]
    [InlineData("--count", "abc")]
    [InlineData("--count", "2")]
    [InlineData("--rover", "sojourner")]
    public async Task Invalid_option_values_exit_1(string option, string value)
    {
        DatesFile();

        var code = await Run([option, value], FakeProvider.WithPhotos());

        Assert.Equal(1, code);
        Assert.False(string.IsNullOrWhiteSpace(_err.ToString()));
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--file")]
    [InlineData("positional")]
    public async Task Unknown_or_incomplete_arguments_exit_1_with_usage(string arg)
    {
        DatesFile();

        var code = await Run([arg], FakeProvider.WithPhotos());

        Assert.Equal(1, code);
        Assert.Contains("Usage", _err.ToString());
    }

    [Fact]
    public async Task Help_prints_usage_and_exits_0()
    {
        var code = await Run(["--help"]);

        Assert.Equal(0, code);
        Assert.Contains("Usage", _out.ToString());
        Assert.Contains("--file", _out.ToString());
    }

    [Fact]
    public async Task Json_flag_prints_the_run_summary_json_to_stdout_and_logs_go_elsewhere()
    {
        DatesFile();

        var code = await Run(["--json"], FakeProvider.WithPhotos(3), new() { ["Run:PhotosPerDate"] = "3" });

        Assert.Equal(0, code);
        var root = JsonDocument.Parse(_out.ToString()).RootElement;
        Assert.Equal(4, root.GetProperty("results").GetArrayLength());
        Assert.Equal("InvalidDate", root.GetProperty("results")[3].GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_unwritable_output_folder_is_fatal_exit_1()
    {
        DatesFile();
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "file");

        var code = await Run([], FakeProvider.WithPhotos(), new() { ["Run:OutputRoot"] = Path.Combine(blocker, "photos") });

        Assert.Equal(1, code);
        Assert.Contains("IO_ERROR", _err.ToString());
    }

    [Fact]
    public async Task Unexpected_errors_exit_2()
    {
        var code = await Run([], FakeProvider.WithPhotos(), configure: s => s.AddSingleton<IDateSource>(new ThrowingSource()));

        Assert.Equal(2, code);
        Assert.Contains("unexpected", _err.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, _err.ToString());
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task Cancellation_exits_130_and_still_prints_the_partial_summary()
    {
        DatesFile();
        using var cts = new CancellationTokenSource();
        var provider = new FakeProvider((rover, date, _) =>
        {
            if (date.Year == 2018)
            {
                cts.Cancel();
            }

            return new ProviderResult.Photos(FakeProvider.Photos(rover, date, 3), 3);
        });

        var code = await Run([], provider, ct: cts.Token);

        Assert.Equal(130, code);
        Assert.Contains("Cancelled", _out.ToString());
    }

    private sealed class ThrowingSource : IDateSource
    {
        public Task<IReadOnlyList<DateInputLine>> ReadAsync(string path, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom with secret test-key-0000");
    }
}
