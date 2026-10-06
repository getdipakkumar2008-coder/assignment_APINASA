using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Output;
using MarsPhotos.Application.Processing;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Cli;

/// <summary>Test seams for <see cref="CliApp"/>; production uses the defaults.</summary>
public sealed class CliHostOptions
{
    /// <summary>Load a local <c>.env</c> into the environment (development convenience).</summary>
    public bool LoadDotEnv { get; init; } = true;

    /// <summary>Extra settings applied after environment variables and before command-line options.</summary>
    public IReadOnlyDictionary<string, string?>? Settings { get; init; }

    public Action<IServiceCollection>? ConfigureServices { get; init; }
}

/// <summary>The console runner. Exit codes: 0 completed, 1 fatal (config/input/output/arguments), 2 unexpected, 130 cancelled.</summary>
public static class CliApp
{
    public const int Completed = 0;
    public const int Fatal = 1;
    public const int Unexpected = 2;
    public const int Cancelled = 130;

    public const string Usage =
        """
        Usage: dotnet run --project src/MarsPhotos.Cli -- [options]

          --file <path>    dates file (default: dates.txt)
          --rover <name>   curiosity | opportunity | spirit (default: curiosity)
          --count <3-5>    photos per date (default: 5)
          --out <dir>      photos folder (default: photos)
          --json           print the run summary as JSON on stdout
          --help           show this help

        The NASA API key is read from NASA__ApiKey (process environment or the git-ignored .env file).
        Exit codes: 0 completed, 1 fatal error, 2 unexpected error, 130 cancelled.
        """;

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CliHostOptions? host = null,
        CancellationToken cancellationToken = default)
    {
        host ??= new CliHostOptions();

        var parsed = ParseArgs(args);
        if (parsed.Help)
        {
            await stdout.WriteLineAsync(Usage);
            return Completed;
        }

        if (parsed.Error is not null)
        {
            await stderr.WriteLineAsync($"Error: {parsed.Error}");
            await stderr.WriteLineAsync(Usage);
            return Fatal;
        }

        if (host.LoadDotEnv)
        {
            DotEnvLoader.Load();
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(host.Settings ?? new Dictionary<string, string?>())
            .AddInMemoryCollection(parsed.Overrides)
            .Build();

        var secret = configuration["Nasa:ApiKey"];
        string Safe(string? text) => Redaction.Redact(text, secret);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(new StderrLoggerProvider(stderr, Safe)));
        services.AddMarsPhotos(configuration);
        host.ConfigureServices?.Invoke(services);

        try
        {
            await using var provider = services.BuildServiceProvider();

            try
            {
                _ = provider.GetRequiredService<IOptions<NasaOptions>>().Value;
                _ = provider.GetRequiredService<IOptions<RunOptions>>().Value;
            }
            catch (OptionsValidationException ex)
            {
                await stderr.WriteLineAsync("Configuration error:");
                foreach (var failure in ex.Failures)
                {
                    await stderr.WriteLineAsync($"  {Safe(failure)}");
                }

                return Fatal;
            }

            var run = provider.GetRequiredService<IOptions<RunOptions>>().Value;
            var lines = await provider.GetRequiredService<IDateSource>().ReadAsync(run.InputFile, cancellationToken);
            var summary = await provider.GetRequiredService<ProcessDatesUseCase>()
                .RunAsync(lines, run.Rover, run.PhotosPerDate, cancellationToken);

            await stdout.WriteLineAsync(parsed.Json ? RunSummaryJson.Serialize(summary) : ConsoleSummaryRenderer.Render(summary));
            return cancellationToken.IsCancellationRequested ? Cancelled : Completed;
        }
        catch (FatalRunException ex)
        {
            await stderr.WriteLineAsync($"Error [{ex.ErrorCode}]: {Safe(ex.Message)}");
            return Fatal;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await stderr.WriteLineAsync("Cancelled.");
            return Cancelled;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"An unexpected error occurred ({ex.GetType().Name}): {Safe(ex.Message)}");
            return Unexpected;
        }
    }

    private sealed record ParsedArgs(Dictionary<string, string?> Overrides, bool Json, bool Help, string? Error);

    private static ParsedArgs ParseArgs(string[] args)
    {
        var overrides = new Dictionary<string, string?>();
        var json = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    return new ParsedArgs(overrides, false, true, null);
                case "--json":
                    json = true;
                    break;
                case "--file" or "--rover" or "--count" or "--out":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return new ParsedArgs(overrides, json, false, $"Option {args[i]} needs a value.");
                    }

                    var value = args[++i];
                    var key = args[i - 1] switch
                    {
                        "--file" => "Run:InputFile",
                        "--rover" => "Run:Rover",
                        "--count" => "Run:PhotosPerDate",
                        _ => "Run:OutputRoot",
                    };
                    if (key == "Run:PhotosPerDate" && !int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                    {
                        return new ParsedArgs(overrides, json, false, $"--count must be a whole number (was '{value}').");
                    }

                    // paths typed on the command line are relative to the current directory, as users expect
                    overrides[key] = key is "Run:InputFile" or "Run:OutputRoot" ? Path.GetFullPath(value) : value;
                    break;
                default:
                    return new ParsedArgs(overrides, json, false, $"Unknown argument '{args[i]}'.");
            }
        }

        return new ParsedArgs(overrides, json, false, null);
    }
}
