using Jellyfin.Plugin.Overcoat.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Overcoat.ScheduledTasks;

/// <summary>
/// Re-applies Overcoat's overlays after a Jellyfin library scan reverts them.
///
/// Why this is needed: when a poster lives in the media folder (common — the *arr stack, or hand-placed
/// art), Jellyfin's local image provider re-adopts that file on every scan and re-points the item's
/// primary image back to it (ItemImageProvider.MergeImages, hard-coded — the local provider cannot be
/// disabled). That silently strips Overcoat's overlay off movies and wide cards. Overcoat itself never
/// touches media-folder files, by design, so it cannot win that fight in place — instead it watches for
/// a scan to finish and runs a follow-up pass, restoring the overlays within seconds.
///
/// This does NOT bloat anything: the follow-up run is the ordinary overlay task, which is cache-gated,
/// so only the handful of items a scan actually reverted are re-rendered; everything else is skipped.
/// The originals vault holds ONE clean copy per item and overwrites it in place on re-baseline, so
/// repeated scan→re-overlay cycles never grow it.
/// </summary>
public sealed class ScanFollowUp : IHostedService, IDisposable
{
    /// <summary>The full library-scan scheduled task (Jellyfin's <c>RefreshMediaLibraryTask.Key</c>).</summary>
    private const string LibraryScanTaskKey = "RefreshLibrary";

    /// <summary>Overcoat's own overlay task, so we never treat our writes as an external scan.</summary>
    private const string OvercoatApplyTaskKey = "OvercoatApply";

    /// <summary>
    /// Quiet period before firing. A scan emits image updates in bursts; each one restarts this timer,
    /// so the run only fires once the dust has settled and the scan is genuinely done. Long enough to
    /// span the gaps in a large scan, short enough that overlays return promptly.
    /// </summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(60);

    private readonly ITaskManager _taskManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ScanFollowUp> _logger;
    private readonly object _gate = new();

    private Timer? _timer;
    private bool _subscribed;
    private bool _pending;

    public ScanFollowUp(ITaskManager taskManager, ILibraryManager libraryManager, ILogger<ScanFollowUp> logger)
    {
        _taskManager = taskManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            // A full scan (scheduled or triggered from the dashboard) completing.
            _taskManager.TaskCompleted += OnTaskCompleted;
            // Per-library / per-item scans and refreshes, which re-point images without running the
            // scheduled task. This is what catches a manual "Scan library files" on one library.
            _libraryManager.ItemUpdated += OnItemUpdated;
            _subscribed = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return Task.CompletedTask;
    }

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs e)
    {
        if (string.Equals(e.Task?.ScheduledTask?.Key, LibraryScanTaskKey, StringComparison.Ordinal))
        {
            ScheduleRun("a library scan finished");
        }
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        // Ignore everything while Overcoat itself is writing — otherwise our own SaveImage calls would
        // retrigger us in a loop. (A spurious run would be a harmless no-op thanks to the cache, but
        // suppressing it keeps the logs clean and avoids churn.)
        if (TaskLease.IsHeld)
        {
            return;
        }

        // Only image changes matter, and only on the item kinds Overcoat overlays.
        if (!e.UpdateReason.HasFlag(ItemUpdateType.ImageUpdate))
        {
            return;
        }

        var kind = e.Item?.GetType().Name;
        if (kind is "Movie" or "Series")
        {
            ScheduleRun("library images changed");
        }
    }

    /// <summary>(Re)arms the debounce timer. The follow-up fires once events go quiet.</summary>
    private void ScheduleRun(string reason)
    {
        if (Plugin.Instance?.Configuration.ReapplyAfterScan != true)
        {
            return;
        }

        lock (_gate)
        {
            if (!_pending)
            {
                _pending = true;
                _logger.LogDebug("Overcoat: {Reason}; will re-apply overlays once scanning settles.", reason);
            }

            _timer ??= new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            _pending = false;
        }

        // The scan may still be finishing, or Overcoat may already be running from the daily trigger.
        // QueueIfNotRunning coalesces both cases — if a run is in flight it's a no-op, and if not, the
        // cache-gated task only touches what actually changed.
        if (TaskLease.IsHeld)
        {
            return;
        }

        try
        {
            _taskManager.QueueIfNotRunning<OverlayTask>();
            _logger.LogInformation("Overcoat: re-applying overlays after a library scan.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Overcoat: could not queue the post-scan overlay run.");
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed)
        {
            _taskManager.TaskCompleted -= OnTaskCompleted;
            _libraryManager.ItemUpdated -= OnItemUpdated;
            _subscribed = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Unsubscribe();
        _timer?.Dispose();
        _timer = null;
    }
}
