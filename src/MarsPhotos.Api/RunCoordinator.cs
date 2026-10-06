using MarsPhotos.Domain;

namespace MarsPhotos.Api;

/// <summary>Allows one run at a time, supports cancelling it, and remembers the latest summary (FR-037, FR-042).</summary>
public sealed class RunCoordinator
{
    private readonly object _sync = new();
    private CancellationTokenSource? _active;

    public RunSummary? Latest { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _active is not null;
            }
        }
    }

    /// <summary>Runs <paramref name="work"/> unless another run is active; returns null when busy.</summary>
    public async Task<RunSummary?> TryRunAsync(Func<CancellationToken, Task<RunSummary>> work, CancellationToken requestAborted)
    {
        CancellationTokenSource cts;
        lock (_sync)
        {
            if (_active is not null)
            {
                return null;
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            _active = cts;
        }

        try
        {
            var summary = await work(cts.Token);
            Latest = summary;
            return summary;
        }
        finally
        {
            lock (_sync)
            {
                _active = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>Requests cancellation of the active run; false when none is running.</summary>
    public bool Cancel()
    {
        lock (_sync)
        {
            if (_active is null)
            {
                return false;
            }

            _active.Cancel();
            return true;
        }
    }
}
