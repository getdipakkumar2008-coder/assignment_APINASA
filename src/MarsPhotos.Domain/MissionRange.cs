namespace MarsPhotos.Domain;

/// <summary>Mission date ranges per rover (inclusive); used to skip lookups outside the mission (FR-007, FR-032).</summary>
public static class MissionRange
{
    private static readonly Dictionary<string, (DateOnly Start, DateOnly? End)> Ranges =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["curiosity"] = (new DateOnly(2012, 8, 6), null), // active: ends today
            ["opportunity"] = (new DateOnly(2004, 1, 25), new DateOnly(2018, 6, 10)),
            ["spirit"] = (new DateOnly(2004, 1, 4), new DateOnly(2010, 3, 22)),
        };

    public static bool IsKnownRover(string rover) => Ranges.ContainsKey(rover);

    public static bool Contains(string rover, DateOnly date, DateOnly todayUtc)
    {
        if (!Ranges.TryGetValue(rover, out var range))
        {
            return false;
        }

        var end = range.End ?? todayUtc;
        return date >= range.Start && date <= end;
    }
}
