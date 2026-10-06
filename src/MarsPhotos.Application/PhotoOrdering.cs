using MarsPhotos.Domain;

namespace MarsPhotos.Application;

/// <summary>Deterministic photo order (FR-030): numeric when every id is a whole number, otherwise plain character order.</summary>
public static class PhotoOrdering
{
    public static IReadOnlyList<RoverPhoto> Sort(IEnumerable<RoverPhoto> photos)
    {
        var list = photos.ToList();
        return list.All(p => long.TryParse(p.Id, out _))
            ? list.OrderBy(p => long.Parse(p.Id)).ToList()
            : list.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
    }
}
