using Jellyfin.Plugin.Overcoat.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Overcoat.ScheduledTasks;

/// <summary>
/// Undo: restores the clean, un-overlaid posters Overcoat vaulted. For each vaulted original it
/// re-uploads the image in-process and clears that item's cache entry + vaulted file. Manual-run
/// only (no default trigger) — invoke it from Dashboard → Scheduled Tasks.
/// </summary>
public class RestoreOriginalsTask : IScheduledTask
{
    private readonly ILogger<RestoreOriginalsTask> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IApplicationPaths _appPaths;

    public RestoreOriginalsTask(
        ILogger<RestoreOriginalsTask> logger,
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IApplicationPaths appPaths)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _appPaths = appPaths;
    }

    /// <inheritdoc />
    public string Name => "Restore Original Posters (Overcoat)";

    /// <inheritdoc />
    public string Key => "OvercoatRestore";

    /// <inheritdoc />
    public string Description => "Restores the clean, un-overlaid posters Overcoat saved, undoing its overlays.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var state = new ProcessingState(Plugin.Instance!.DataFolderPath, _logger);
        using var file = new FileLog(_appPaths.LogDirectoryPath);
        var ids = state.VaultedIds().ToList();
        if (ids.Count == 0)
        {
            _logger.LogInformation("Overcoat: nothing to restore.");
            file.Info("Restore — nothing to restore.");
            progress.Report(100);
            return;
        }

        file.Info($"Restore started — {ids.Count} vaulted poster(s).");

        int done = 0;
        int restored = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var bytes = await state.ReadOriginalAsync(id, cancellationToken).ConfigureAwait(false);
                var item = Guid.TryParse(id, out var g) ? _libraryManager.GetItemById(g) : null;
                if (bytes is not null && item is not null)
                {
                    using var ms = new MemoryStream(bytes);
                    await _providerManager
                        .SaveImage(item, ms, "image/png", ImageType.Primary, null, cancellationToken)
                        .ConfigureAwait(false);
                    await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
                    restored++;
                    _logger.LogInformation("Overcoat: restored '{Name}'.", item.Name);
                    file.Info("Restored " + item.Name);
                }

                // Clear the cache entry + vaulted original whether or not the item still exists.
                state.Remove(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overcoat: failed restoring item {Id}.", id);
                file.Error("Restore failed for " + id + " — " + ex.Message);
            }
            finally
            {
                done++;
                progress.Report(100.0 * done / ids.Count);
            }
        }

        state.Flush();
        _logger.LogInformation("Overcoat: restore done. {Restored}/{Count} poster(s) restored.", restored, ids.Count);
        file.Info($"Restore done — {restored}/{ids.Count} poster(s) restored.");
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
}
