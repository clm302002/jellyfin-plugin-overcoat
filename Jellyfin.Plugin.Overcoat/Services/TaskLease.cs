namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// A process-wide mutual exclusion around Overcoat's mutating tasks.
///
/// Jellyfin serialises each scheduled task against itself, but Apply and Restore are *different*
/// tasks and can run at the same time. They also build their own <see cref="ProcessingState"/>
/// instances, so the locks inside that class — which only guard one instance — do not help. The
/// damaging interleaving is:
///
///   1. Apply reads the vaulted original for an item.
///   2. Restore restores that item and deletes the vault entry and its cache record.
///   3. Apply saves its overlay and re-writes state.
///
/// The item ends up overlaid with no clean original anywhere: the one file that could undo it was
/// deleted between Apply reading it and Apply finishing. Holding this lease for the whole of each
/// task makes that ordering impossible.
///
/// Deliberately a wait rather than a fail: the second task should run *after* the first, not be
/// dropped. Callers should still pass a cancellation token so a queued task can be cancelled from
/// the dashboard while it waits.
/// </summary>
internal static class TaskLease
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Waits for exclusive access. Dispose the result to release it.</summary>
    public static async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        return new Releaser();
    }

    /// <summary>Gets a value indicating whether a mutating Overcoat task currently holds the lease.</summary>
    public static bool IsHeld => Gate.CurrentCount == 0;

    private sealed class Releaser : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            Gate.Release();
        }
    }
}
