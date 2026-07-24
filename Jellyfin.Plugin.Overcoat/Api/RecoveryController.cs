using Jellyfin.Plugin.Overcoat.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Overcoat.Api;

/// <summary>
/// Reports what Overcoat could still undo, for the Recovery panel on the settings page.
///
/// Read-only by design. The panel exists to answer "can I get my posters back?" with a number
/// instead of a shrug — the vault is the only route back to un-overlaid art, and nothing previously
/// surfaced whether it was intact.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Overcoat")]
public sealed class RecoveryController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<RecoveryController> _logger;

    public RecoveryController(ILibraryManager libraryManager, ILogger<RecoveryController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>Vault and cache health.</summary>
    /// <response code="200">Summary returned.</response>
    [HttpGet("VaultHealth")]
    [Produces("application/json")]
    public ActionResult<object> GetVaultHealth()
    {
        var state = new ProcessingState(Plugin.Instance!.DataFolderPath, _logger);
        var tracked = state.CachedIds;
        var vaulted = state.VaultedIds().ToList();

        var health = VaultHealth.Build(
            tracked,
            vaulted,
            state.OriginalSize,
            id => Guid.TryParse(id, out var g) && _libraryManager.GetItemById(g) is not null);
        var thumbState = new ProcessingState(Plugin.Instance.DataFolderPath, _logger, ProcessingState.ArtworkChannel.Thumb);
        var wideHealth = VaultHealth.Build(
            thumbState.CachedIds,
            thumbState.VaultedIds().ToList(),
            thumbState.OriginalSize,
            id => Guid.TryParse(id, out var g) && _libraryManager.GetItemById(g) is MediaBrowser.Controller.Entities.TV.Series);

        return Ok(new
        {
            health.TrackedItems,
            health.TrackedWithOriginal,
            health.TrackedWithoutOriginal,
            health.OrphanedOriginals,
            health.VaultFiles,
            health.VaultBytes,
            health.VaultSizeDisplay,
            WideCards = new
            {
                wideHealth.TrackedItems,
                wideHealth.TrackedWithOriginal,
                wideHealth.TrackedWithoutOriginal,
                wideHealth.OrphanedOriginals,
                wideHealth.VaultFiles,
                wideHealth.VaultBytes,
                wideHealth.VaultSizeDisplay,
            },
        });
    }
}
