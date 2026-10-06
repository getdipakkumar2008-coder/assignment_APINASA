using MarsPhotos.Domain;

namespace MarsPhotos.Application.Abstractions;

/// <summary>Port that supplies the raw date lines for a run (a file today).</summary>
public interface IDateSource
{
    /// <summary>Reads input lines. Throws <see cref="FatalRunException"/> when the source is missing, empty or unreadable.</summary>
    Task<IReadOnlyList<DateInputLine>> ReadAsync(string path, CancellationToken cancellationToken);
}
