using MarsPhotos.Domain;

namespace MarsPhotos.Application.Abstractions;

public interface IDateSource
{
    /// <summary>Reads input lines. Throws <see cref="FatalRunException"/> when the source is missing, empty or unreadable.</summary>
    Task<IReadOnlyList<DateInputLine>> ReadAsync(string path, CancellationToken cancellationToken);
}
