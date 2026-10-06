using System.Text.Json;
using System.Text.Json.Serialization;
using MarsPhotos.Api;
using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Output;
using MarsPhotos.Application.Parsing;
using MarsPhotos.Application.Processing;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Configuration;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

// Development convenience: load the git-ignored .env before configuration is built (skipped by test hosts).
if (Environment.GetEnvironmentVariable("MARSPHOTOS_SKIP_DOTENV") != "1")
{
    DotEnvLoader.Load();
}

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:5180");
}

const string UiOrigin = "http://localhost:4200";
builder.Services.AddMarsPhotos(builder.Configuration);
builder.Services.AddSingleton<RunCoordinator>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(o => o.AddPolicy("ui", p => p.WithOrigins(UiOrigin).WithMethods("GET", "POST").WithHeaders("Content-Type")));

var app = builder.Build();

var runSettings = app.Services.GetRequiredService<IOptions<RunOptions>>().Value;
var photosRoot = Path.GetFullPath(runSettings.OutputRoot);
Directory.CreateDirectory(photosRoot);

app.UseExceptionHandler();
app.UseCors("ui");

// CORS only stops a page from *reading* a response; it does not stop another site's page from *sending* a simple POST that
// starts or cancels a run. Reject state-changing requests that carry a foreign Origin (review finding MEDIUM-1).
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    var isSafe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
    if (!isSafe
        && context.Request.Headers.TryGetValue("Origin", out var origin)
        && !string.Equals(origin.ToString(), UiOrigin, StringComparison.OrdinalIgnoreCase))
    {
        await Problem(403, "Forbidden origin", "Requests from this origin are not allowed.", ErrorCodes.ForbiddenOrigin).ExecuteAsync(context);
        return;
    }

    await next();
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(photosRoot),
    RequestPath = "/photos",
    ServeUnknownFileTypes = false,

    // Only images are ever served from the photos folder; every other extension (.part, .txt, ...) is a 404.
    ContentTypeProvider = new FileExtensionContentTypeProvider(new Dictionary<string, string>
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    }),
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/runs/latest", (RunCoordinator coordinator) =>
    coordinator.Latest is { } latest
        ? Results.Json(RunSummaryDto.From(latest), RunSummaryJson.Options)
        : Problem(404, "No run yet", "No run has completed in this process yet.", "NO_RUN_YET"));

app.MapPost("/api/runs/cancel", (RunCoordinator coordinator) =>
    coordinator.Cancel()
        ? Results.Accepted()
        : Problem(409, "No run in progress", "There is no active run to cancel.", ErrorCodes.NoRunInProgress));

app.MapPost("/api/runs", async (
    HttpRequest request,
    RunCoordinator coordinator,
    IDateSource dateSource,
    ProcessDatesUseCase useCase,
    IOptions<RunOptions> options,
    CancellationToken cancellationToken) =>
{
    var settings = options.Value;

    RunRequest? body = null;
    if (request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
    {
        if (!request.HasJsonContentType())
        {
            return Problem(415, "Unsupported media type", "The request body must be application/json.", ErrorCodes.UnsupportedMediaType);
        }

        try
        {
            body = await request.ReadFromJsonAsync<RunRequest>(RunSummaryJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return Problem(400, "Invalid request", "The request body is not valid JSON for a run request.", "BAD_REQUEST");
        }
    }

    var invalid = Validate(body, settings);
    if (invalid is not null)
    {
        return Problem(400, "Invalid request", invalid, "BAD_REQUEST");
    }

    try
    {
        var summary = await coordinator.TryRunAsync(
            async ct =>
            {
                IReadOnlyList<DateInputLine> lines = body?.Dates is { } dates
                    ? dates.Select((d, i) => new DateInputLine(i + 1, (d ?? string.Empty).Trim())).ToList()
                    : await dateSource.ReadAsync(settings.InputFile, ct);
                return await useCase.RunAsync(lines, body?.Rover, body?.PhotosPerDate, ct);
            },
            cancellationToken);

        return summary is null
            ? Problem(409, "Run in progress", "A run is already in progress. Wait for it to finish or cancel it.", ErrorCodes.RunInProgress)
            : Results.Json(RunSummaryDto.From(summary), RunSummaryJson.Options);
    }
    catch (FatalRunException ex)
    {
        return Problem(500, "The run could not be completed", Redaction.RedactApiKey(ex.Message), ex.ErrorCode);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        // Cancel arrived before the run reached the date loop (for example while the dates file was being read).
        return Problem(409, "Run cancelled", "The run was cancelled before any date was processed.", ErrorCodes.RunCancelled);
    }
});

app.Run();

static IResult Problem(int status, string title, string detail, string code) =>
    Results.Problem(title: title, detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

static string? Validate(RunRequest? body, RunOptions settings)
{
    if (body is null)
    {
        return null;
    }

    if (body.Dates is { } dates)
    {
        if (dates.Length == 0)
        {
            return "The 'dates' list must not be empty; omit it to use the configured dates file.";
        }

        if (dates.Length > settings.MaxApiLines)
        {
            return $"At most {settings.MaxApiLines} dates can be supplied in one request (got {dates.Length}).";
        }

        if (dates.Any(d => (d?.Length ?? 0) > settings.MaxLineLength))
        {
            return $"Each date line must be at most {settings.MaxLineLength} characters long.";
        }
    }

    if (body.Rover is { } rover && !MissionRange.IsKnownRover(rover))
    {
        return "The rover must be one of: curiosity, opportunity, spirit.";
    }

    if (body.PhotosPerDate is { } count && count is < 3 or > 5)
    {
        return "photosPerDate must be between 3 and 5.";
    }

    return null;
}

/// <summary>Request body for <c>POST /api/runs</c>; every property is optional.</summary>
public sealed record RunRequest(string[]? Dates, string? Rover, int? PhotosPerDate);

/// <summary>Entry point marker so integration tests can host the API.</summary>
public partial class Program;
