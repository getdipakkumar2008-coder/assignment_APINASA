namespace MarsPhotos.Domain;

/// <summary>One image offered by a photo source.</summary>
public sealed record RoverPhoto(string Id, int? Sol, string Camera, Uri ImageUrl, DateOnly EarthDate, string Rover);

public enum PhotoStatus
{
    Downloaded,
    Skipped,
    Failed,
}

/// <summary>Result for one photo within a date.</summary>
public sealed record PhotoOutcome(
    string PhotoId,
    PhotoStatus Status,
    string? RelativePath,
    string? ErrorCode,
    string? Message,
    long? Bytes);
