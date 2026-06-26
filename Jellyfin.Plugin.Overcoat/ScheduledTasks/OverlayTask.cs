using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Overcoat.Configuration;
using Jellyfin.Plugin.Overcoat.Services;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Overcoat.ScheduledTasks;

/// <summary>
/// The in-process equivalent of the old script's <c>main()</c> run loop. Enumerates configured
/// libraries, resolves each show's TMDB status, renders the status banner with
/// <see cref="OverlayRenderer"/>, and saves the poster back **in-process** via
/// <see cref="IProviderManager.SaveImage(BaseItem, Stream, string, ImageType, int?, CancellationToken)"/>
/// — no callback HTTP server.
///
/// Scope (Phase 4 MVP): TV libraries + status banners only. Badges, the movie pipeline, the skip
/// cache and self-heal arrive in later phases; the structure leaves obvious seams for them.
/// </summary>
public class OverlayTask : IScheduledTask
{
    private readonly ILogger<OverlayTask> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IHttpClientFactory _httpClientFactory;

    public OverlayTask(
        ILogger<OverlayTask> logger,
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => "Apply Overcoat Overlays";

    /// <inheritdoc />
    public string Key => "OvercoatApply";

    /// <inheritdoc />
    public string Description => "Applies status overlays and watch/trending/IMDB Top 250 badges to posters.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            _logger.LogError("Overcoat: no configuration available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            _logger.LogError("Overcoat: TMDB API key is not set; configure it on the plugin page.");
            return;
        }

        var http = _httpClientFactory.CreateClient(NamedClient.Default);
        var tmdb = new TmdbService(http, config.TmdbApiKey, _logger);
        using var renderer = new OverlayRenderer();

        var ignore = new HashSet<string>(config.IgnoreTitles, StringComparer.OrdinalIgnoreCase);
        var limit = new HashSet<string>(config.LimitToTitles, StringComparer.OrdinalIgnoreCase);

        // Build the TV work-list across all enabled, status-overlay TV libraries.
        var work = new List<BaseItem>();
        foreach (var lib in config.Libraries)
        {
            if (!lib.Enabled)
            {
                continue;
            }

            var vf = _libraryManager.GetVirtualFolders()
                .FirstOrDefault(v => string.Equals(v.Name, lib.Name, StringComparison.OrdinalIgnoreCase));
            if (vf is null)
            {
                _logger.LogWarning("Overcoat: library '{Name}' not found in Jellyfin; skipping.", lib.Name);
                continue;
            }

            var type = ResolveType(lib.Type, vf.CollectionType?.ToString());
            if (type != "tv")
            {
                _logger.LogInformation(
                    "Overcoat: library '{Name}' is type '{Type}'; movie/badge handling lands in a later phase.",
                    lib.Name,
                    type);
                continue;
            }

            if (!lib.StatusOverlays)
            {
                _logger.LogInformation("Overcoat: status banners disabled for '{Name}'; nothing to do yet (badges later).", lib.Name);
                continue;
            }

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = Guid.Parse(vf.ItemId),
                IncludeItemTypes = new[] { BaseItemKind.Series },
                Recursive = true,
            });

            work.AddRange(items);
        }

        if (work.Count == 0)
        {
            _logger.LogInformation("Overcoat: no TV items to process.");
            progress.Report(100);
            return;
        }

        _logger.LogInformation("Overcoat: processing {Count} TV item(s){DryRun}.", work.Count, config.DryRun ? " (dry run)" : string.Empty);

        int done = 0;
        int updated = 0;
        foreach (var item in work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var itemName = item.Name ?? string.Empty;
                if (ignore.Contains(itemName) || (limit.Count > 0 && !limit.Contains(itemName)))
                {
                    continue;
                }

                if (await ProcessShowAsync(item, tmdb, renderer, config, cancellationToken).ConfigureAwait(false))
                {
                    updated++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overcoat: failed processing '{Name}'.", item.Name);
            }
            finally
            {
                done++;
                progress.Report(100.0 * done / work.Count);
            }
        }

        _logger.LogInformation("Overcoat: done. {Updated}/{Count} poster(s) updated.", updated, work.Count);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
        };
    }

    private async Task<bool> ProcessShowAsync(
        BaseItem item,
        TmdbService tmdb,
        OverlayRenderer renderer,
        PluginConfiguration config,
        CancellationToken ct)
    {
        var tmdbId = await ResolveTmdbIdAsync(item, tmdb, config, ct).ConfigureAwait(false);
        if (tmdbId is null)
        {
            _logger.LogDebug("Overcoat: no TMDB id for '{Name}'; skipping.", item.Name);
            return false;
        }

        var info = await tmdb.GetTvStatusAsync(tmdbId.Value, ct).ConfigureAwait(false);
        if (info is null)
        {
            return false;
        }

        var text = StatusOverlayResolver.Resolve(info);
        if (text is null)
        {
            _logger.LogDebug("Overcoat: no overlay match for '{Name}' (status {Status}).", item.Name, info.Status);
            return false;
        }

        // Prefer the existing Jellyfin poster; fall back to TMDB when the item has none — or when
        // its registered image file is missing (e.g. deleted externally, leaving a dangling DB
        // entry). Mirrors process_show Step 4. With no source at all, there's nothing to overlay.
        byte[]? posterBytes = null;
        if (item.HasImage(ImageType.Primary, 0))
        {
            var sourcePath = item.GetImagePath(ImageType.Primary, 0);
            if (File.Exists(sourcePath))
            {
                posterBytes = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
            }
        }

        if (posterBytes is null
            && info.PosterPath is { Length: > 0 } posterPath
            && await tmdb.DownloadPosterAsync(posterPath, ct).ConfigureAwait(false) is { } fetched)
        {
            _logger.LogInformation("Overcoat: '{Name}' had no usable Jellyfin poster; using TMDB poster.", item.Name);
            posterBytes = fetched;
        }

        if (posterBytes is null)
        {
            _logger.LogDebug("Overcoat: '{Name}' has no poster (Jellyfin or TMDB); skipping.", item.Name);
            return false;
        }

        using var bmp = OverlayRenderer.Decode(posterBytes);
        if (bmp is null)
        {
            _logger.LogWarning("Overcoat: could not decode poster for '{Name}'.", item.Name);
            return false;
        }

        renderer.DrawStatusBanner(bmp, text);
        var png = OverlayRenderer.EncodePng(bmp);

        if (config.DryRun)
        {
            _logger.LogInformation("Overcoat: [dry run] would set '{Name}' → '{Text}'.", item.Name, text);
            return true;
        }

        using var ms = new MemoryStream(png);
        await _providerManager
            .SaveImage(item, ms, "image/png", ImageType.Primary, null, ct)
            .ConfigureAwait(false);

        // SaveImage updates the item's image info in memory; from a standalone scheduled task we
        // must persist it so Jellyfin's DB (and the served image tag) points at the new poster.
        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ct).ConfigureAwait(false);

        _logger.LogInformation("Overcoat: '{Name}' → '{Text}'.", item.Name, text);
        return true;
    }

    /// <summary>TMDB id resolution: overrides → ProviderIds.Tmdb → Imdb/Tvdb via /find → title search.</summary>
    private async Task<int?> ResolveTmdbIdAsync(BaseItem item, TmdbService tmdb, PluginConfiguration config, CancellationToken ct)
    {
        var name = item.Name ?? string.Empty;
        var year = item.ProductionYear;

        foreach (var ov in config.TmdbOverrides)
        {
            if (string.Equals(ov.Name, name, StringComparison.OrdinalIgnoreCase)
                && (ov.Year == 0 || ov.Year == year))
            {
                return ov.TmdbId;
            }
        }

        if (TryGetProvider(item, "Tmdb", out var raw) && int.TryParse(raw, out var direct))
        {
            return direct;
        }

        if (TryGetProvider(item, "Imdb", out var imdb) && !string.IsNullOrEmpty(imdb))
        {
            var resolved = await tmdb.FindByExternalIdAsync(imdb!, "imdb_id", movie: false, ct).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (TryGetProvider(item, "Tvdb", out var tvdb) && !string.IsNullOrEmpty(tvdb))
        {
            var resolved = await tmdb.FindByExternalIdAsync(tvdb!, "tvdb_id", movie: false, ct).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return await tmdb.SearchShowAsync(name, year, ct).ConfigureAwait(false);
    }

    private static bool TryGetProvider(BaseItem item, string key, out string? value)
    {
        value = null;
        if (item.ProviderIds is null)
        {
            return false;
        }

        foreach (var kv in item.ProviderIds)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return !string.IsNullOrEmpty(value);
            }
        }

        return false;
    }

    private static string ResolveType(string? explicitType, string? collectionType)
    {
        if (!string.IsNullOrWhiteSpace(explicitType))
        {
            return explicitType.Trim().ToLowerInvariant();
        }

        return collectionType?.ToLowerInvariant() switch
        {
            "tvshows" => "tv",
            "movies" => "movie",
            _ => "unknown",
        };
    }
}
