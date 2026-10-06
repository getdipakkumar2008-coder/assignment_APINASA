using MarsPhotos.Application;
using MarsPhotos.Domain;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Infrastructure.Configuration;

/// <summary>Validates <see cref="NasaOptions"/> at startup; a missing key is refused with guidance and never replaced by a shared demo key.</summary>
public sealed class NasaOptionsValidator : IValidateOptions<NasaOptions>
{
    public const string MissingKeyMessage =
        "The NASA API key is not configured. Set NASA__ApiKey in the environment, in a local .env file "
        + "(copy .env.example). Get a free key at https://api.nasa.gov.";

    public ValidateOptionsResult Validate(string? name, NasaOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            errors.Add($"{ErrorCodes.ConfigMissingKey}: {MissingKeyMessage}");
        }
        else if (options.ApiKey.Trim().Length < 8)
        {
            errors.Add($"{ErrorCodes.ConfigMissingKey}: The configured NASA API key is too short to be valid.");
        }

        RequireHttps(errors, "Nasa:PrimaryBaseUrl", options.PrimaryBaseUrl);
        RequireHttps(errors, "Nasa:FallbackBaseUrl", options.FallbackBaseUrl);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void RequireHttps(List<string> errors, string setting, string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{setting} must be an absolute https URL.");
        }
    }
}

/// <summary>Validates <see cref="RunOptions"/> ranges at startup and names the offending setting.</summary>
public sealed class RunOptionsValidator : IValidateOptions<RunOptions>
{
    public ValidateOptionsResult Validate(string? name, RunOptions o)
    {
        var errors = new List<string>();

        if (!MissionRange.IsKnownRover(o.Rover))
        {
            errors.Add("Run:Rover must be one of curiosity, opportunity, spirit.");
        }

        Range(errors, "Run:PhotosPerDate", o.PhotosPerDate, 3, 5);
        Range(errors, "Run:MaxRetries", o.MaxRetries, 0, 5);
        Range(errors, "Run:MaxParallelDownloads", o.MaxParallelDownloads, 1, 5);
        Positive(errors, "Run:LookupTimeoutSeconds", o.LookupTimeoutSeconds);
        Positive(errors, "Run:DownloadTimeoutSeconds", o.DownloadTimeoutSeconds);
        Positive(errors, "Run:TotalBudgetSeconds", o.TotalBudgetSeconds);
        Positive(errors, "Run:RetryBaseDelayMilliseconds", o.RetryBaseDelayMilliseconds);
        Positive(errors, "Run:RetryMaxDelayMilliseconds", o.RetryMaxDelayMilliseconds);
        Positive(errors, "Run:MaxImageBytes", o.MaxImageBytes);
        Positive(errors, "Run:MaxFileLines", o.MaxFileLines);
        Positive(errors, "Run:MaxApiLines", o.MaxApiLines);
        Positive(errors, "Run:MaxLineLength", o.MaxLineLength);

        if (string.IsNullOrWhiteSpace(o.InputFile))
        {
            errors.Add("Run:InputFile must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(o.OutputRoot))
        {
            errors.Add("Run:OutputRoot must not be empty.");
        }

        if (o.AllowedImageHosts is null || o.AllowedImageHosts.Length == 0 || o.AllowedImageHosts.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Run:AllowedImageHosts must list at least one host.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Range(List<string> errors, string setting, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            errors.Add($"{setting} must be between {min} and {max} (was {value}).");
        }
    }

    private static void Positive(List<string> errors, string setting, long value)
    {
        if (value <= 0)
        {
            errors.Add($"{setting} must be greater than 0 (was {value}).");
        }
    }
}
