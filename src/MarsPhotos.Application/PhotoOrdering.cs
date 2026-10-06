using System.Globalization;
using MarsPhotos.Domain;

namespace MarsPhotos.Application;

/// <summary>Deterministic photo order (FR-030): numeric when every id is a whole number, otherwise plain character order.</summary>
public static class PhotoOrdering
{
    public static IReadOnlyList<RoverPhoto> Sort(IEnumerable<RoverPhoto> photos)
    {
        var list = photos.ToList();
        return list.All(p => IsWholeNumber(p.Id))
            ? list.OrderBy(p => long.Parse(p.Id, NumberStyles.None, CultureInfo.InvariantCulture)).ToList()
            : list.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Digits only, parsed with the invariant culture, so signs and spaces never make an id "numeric" on some machines only.</summary>
    private static bool IsWholeNumber(string id) =>
        long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
