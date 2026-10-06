using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Infrastructure.Storage;

/// <summary>
/// Stores photos as <c>{root}/{yyyy-MM-dd}/{name}</c>. Writes go to a <c>.part</c> file and are moved into place
/// only when complete, so an interrupted write can never look like a finished photo (FR-014 to FR-017).
/// </summary>
public sealed class FileSystemPhotoStore(IOptions<RunOptions> options) : IPhotoStore
{
    private const int BufferSize = 81920;

    private string Root => Path.GetFullPath(options.Value.OutputRoot);

    public string RelativePath(DateOnly date, string fileName) => $"{date:yyyy-MM-dd}/{fileName}";

    public bool Exists(DateOnly date, string fileName)
    {
        var path = Resolve(date, fileName);
        var info = new FileInfo(path);
        return info.Exists && info.Length > 0;
    }

    /// <summary>
    /// Full path for the photo. The file must sit directly inside that date's folder: names containing separators
    /// or dot segments (even ones that stay inside the root, like <c>../x.jpg</c>) are rejected.
    /// </summary>
    private string Resolve(DateOnly date, string fileName)
    {
        var full = FileNames.EnsureInside(Root, RelativePath(date, fileName));
        var expectedFolder = Path.GetFullPath(Path.Combine(Root, $"{date:yyyy-MM-dd}"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(full), expectedFolder, comparison))
        {
            throw new ArgumentException("File name must not contain path segments.", nameof(fileName));
        }

        return full;
    }

    public void EnsureWritable()
    {
        var root = Root;
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new FatalRunException(ErrorCodes.IoError, $"The photos folder '{root}' is not writable: {ex.Message}", ex);
        }
    }

    public async Task<long> WriteAsync(DateOnly date, string fileName, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        var finalPath = Resolve(date, fileName);
        var partPath = finalPath + ".part";
        var ownsPart = false; // never delete a .part file that another writer owns (review finding MEDIUM-3)

        try
        {
            Destination(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                if (File.Exists(finalPath) && new FileInfo(finalPath).Length > 0)
                {
                    throw new PhotoStoreException(ErrorCodes.IoError, $"'{RelativePath(date, fileName)}' already exists; refusing to overwrite it.");
                }
            });

            long total = 0;
            var destination = Destination(() => new FileStream(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true));
            ownsPart = true;
            await using (destination)
            {
                var buffer = new byte[BufferSize];
                while (true)
                {
                    var read = await content.ReadAsync(buffer.AsMemory(), cancellationToken); // source errors propagate to the caller
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > maxBytes)
                    {
                        throw new PhotoStoreException(ErrorCodes.DownloadTooLarge, $"Photo is larger than the {maxBytes} byte limit.");
                    }

                    await DestinationAsync(() => destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).AsTask());
                }
            }

            if (total == 0)
            {
                throw new PhotoStoreException(ErrorCodes.DownloadFailed, "The downloaded photo was empty.");
            }

            Destination(() =>
            {
                if (File.Exists(finalPath) && new FileInfo(finalPath).Length > 0)
                {
                    // someone else finished this photo while we were downloading: never overwrite a valid file
                    throw new PhotoStoreException(ErrorCodes.IoError, $"'{RelativePath(date, fileName)}' already exists; refusing to overwrite it.");
                }

                File.Move(partPath, finalPath, overwrite: true); // replaces only a zero-byte leftover, in one atomic step
            });
            return total;
        }
        catch
        {
            if (ownsPart)
            {
                TryDelete(partPath);
            }

            throw;
        }
    }

    private static T Destination<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PhotoStoreException(ErrorCodes.IoError, $"Could not write the photo: {ex.Message}", ex);
        }
    }

    private static void Destination(Action action) => Destination(() =>
    {
        action();
        return 0;
    });

    private static async Task DestinationAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PhotoStoreException(ErrorCodes.IoError, $"Could not write the photo: {ex.Message}", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort: a leftover .part file is never treated as a finished photo
        }
    }
}
