namespace MarsPhotos.Domain;

/// <summary>
/// Converts an Earth date to the Curiosity Mars day (sol) the original photo service assigned to it (decision D-1).
/// Reference time is noon UTC of the date, counted from landing, rounded down (FR-011, FR-031).
/// </summary>
public static class SolConverter
{
    public const double SolSeconds = 88_775.244;

    public static readonly DateTimeOffset CuriosityLanding = new(2012, 8, 6, 5, 17, 57, TimeSpan.Zero);

    public static int ForCuriosity(DateOnly date)
    {
        var noon = new DateTimeOffset(date.Year, date.Month, date.Day, 12, 0, 0, TimeSpan.Zero);
        var seconds = (noon - CuriosityLanding).TotalSeconds;
        if (seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, "The date is before Curiosity landed.");
        }

        return (int)Math.Floor(seconds / SolSeconds);
    }
}
