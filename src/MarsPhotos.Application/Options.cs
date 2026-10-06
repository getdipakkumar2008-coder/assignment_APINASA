namespace MarsPhotos.Application;

public sealed class NasaOptions
{
    public const string SectionName = "Nasa";

    /// <summary>NASA API key. Supplied only via environment / .env / user-secrets; never logged.</summary>
    public string? ApiKey { get; set; }

    public string PrimaryBaseUrl { get; set; } = "https://api.nasa.gov/mars-photos/api/v1/";

    public string FallbackBaseUrl { get; set; } = "https://mars.nasa.gov/rss/api/";
}

public sealed class RunOptions
{
    public const string SectionName = "Run";

    public string Rover { get; set; } = "curiosity";

    public int PhotosPerDate { get; set; } = 5;

    public string InputFile { get; set; } = "dates.txt";

    public string OutputRoot { get; set; } = "photos";

    public int LookupTimeoutSeconds { get; set; } = 30;

    public int DownloadTimeoutSeconds { get; set; } = 60;

    public int MaxRetries { get; set; } = 3;

    /// <summary>First retry wait; doubles each attempt (with jitter) up to <see cref="RetryMaxDelayMilliseconds"/>.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 1000;

    public int RetryMaxDelayMilliseconds { get; set; } = 8000;

    /// <summary>Total time budget for one lookup request including retries.</summary>
    public int TotalBudgetSeconds { get; set; } = 60;

    public int MaxParallelDownloads { get; set; } = 3;

    public long MaxImageBytes { get; set; } = 26_214_400;

    public string[] AllowedImageHosts { get; set; } = ["mars.nasa.gov", "mars.jpl.nasa.gov"];

    public int MaxFileLines { get; set; } = 1000;

    public int MaxApiLines { get; set; } = 100;

    public int MaxLineLength { get; set; } = 200;
}
