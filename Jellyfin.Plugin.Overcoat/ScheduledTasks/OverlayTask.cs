using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Overcoat.Configuration;
using Jellyfin.Plugin.Overcoat.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Overcoat.ScheduledTasks;

/// <summary>
/// Overcoat's main run loop (the in-process port of the Python <c>main()</c>). Per enabled library
/// it resolves each item's TMDB id, draws the status banner (TV only) and the qualifying badges
/// (watch-history / TMDB-trending / IMDB Top 250), and saves the poster in-process — always from the
/// vaulted clean original, with the skip cache + self-heal in <see cref="ProcessingState"/>.
/// Movies are badges-only (no status banner), mirroring the reference.
/// </summary>
public class OverlayTask : IScheduledTask
{
    private readonly ILogger<OverlayTask> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IApplicationPaths _appPaths;

    public OverlayTask(
        ILogger<OverlayTask> logger,
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IHttpClientFactory httpClientFactory,
        IUserManager userManager,
        IUserDataManager userDataManager,
        IApplicationPaths appPaths)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _httpClientFactory = httpClientFactory;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _appPaths = appPaths;
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
        var badges = new BadgeCompositor();
        var state = new ProcessingState(Plugin.Instance!.DataFolderPath, _logger);
        using var file = new FileLog(_appPaths.LogDirectoryPath);

        var ignore = new HashSet<string>(config.IgnoreTitles, StringComparer.OrdinalIgnoreCase);
        var limit = new HashSet<string>(config.LimitToTitles, StringComparer.OrdinalIgnoreCase);

        // Resolve the enabled libraries to process (TV or movie).
        var plans = new List<(LibraryConfig Lib, Guid ParentId, string Type)>();
        foreach (var lib in config.Libraries.Where(l => l.Enabled))
        {
            var vf = _libraryManager.GetVirtualFolders()
                .FirstOrDefault(v => string.Equals(v.Name, lib.Name, StringComparison.OrdinalIgnoreCase));
            if (vf is null)
            {
                _logger.LogWarning("Overcoat: library '{Name}' not found in Jellyfin; skipping.", lib.Name);
                continue;
            }

            var type = ResolveType(lib.Type, vf.CollectionType?.ToString());
            if (type is not ("tv" or "movie"))
            {
                _logger.LogInformation("Overcoat: library '{Name}' has unsupported type '{Type}'; skipping.", lib.Name, type);
                continue;
            }

            plans.Add((lib, Guid.Parse(vf.ItemId), type));
        }

        // Fetch the badge data sets once, only those some enabled library actually needs.
        var b = config.BadgesEnabled;
        bool NeedTv(Func<LibraryConfig, bool> f) => b && plans.Any(p => p.Type == "tv" && f(p.Lib));
        bool NeedMovie(Func<LibraryConfig, bool> f) => b && plans.Any(p => p.Type == "movie" && f(p.Lib));
        var watch = new WatchHistory(_libraryManager, _userManager, _userDataManager, _logger);

        var trendingTv = NeedTv(l => l.TrendingBadge) ? await tmdb.GetTrendingIdsAsync("tv", config.TrendingTimeWindow, cancellationToken).ConfigureAwait(false) : new HashSet<int>();
        var top250Tv = NeedTv(l => l.ImdbTop250Badge) ? await tmdb.GetListIdsAsync(config.ImdbTop250TvListId, cancellationToken).ConfigureAwait(false) : new HashSet<int>();
        var watchedSeries = NeedTv(l => l.WatchHistoryBadge) ? watch.RecentlyWatchedSeriesIds(config.WatchHistoryDays, config.WatchHistoryAllUsers, config.WatchHistoryUserId, config.WatchHistoryMaxScan) : new HashSet<Guid>();
        var trendingMovie = NeedMovie(l => l.TrendingBadge) ? await tmdb.GetTrendingIdsAsync("movie", config.TrendingTimeWindow, cancellationToken).ConfigureAwait(false) : new HashSet<int>();
        var top250Movie = NeedMovie(l => l.ImdbTop250Badge) ? await tmdb.GetListIdsAsync(config.ImdbTop250MovieListId, cancellationToken).ConfigureAwait(false) : new HashSet<int>();
        var watchedMovies = NeedMovie(l => l.WatchHistoryBadge) ? watch.RecentlyWatchedMovieIds(config.WatchHistoryDays, config.WatchHistoryAllUsers, config.WatchHistoryUserId, config.WatchHistoryMaxScan) : new HashSet<Guid>();

        // Build the work list (item + its library config + type).
        var work = new List<(BaseItem Item, LibraryConfig Lib, string Type)>();
        foreach (var plan in plans)
        {
            var kind = plan.Type == "tv" ? BaseItemKind.Series : BaseItemKind.Movie;
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = plan.ParentId,
                IncludeItemTypes = new[] { kind },
                Recursive = true,
            });
            foreach (var item in items)
            {
                work.Add((item, plan.Lib, plan.Type));
            }
        }

        if (work.Count == 0)
        {
            _logger.LogInformation("Overcoat: no items to process.");
            progress.Report(100);
            return;
        }

        _logger.LogInformation("Overcoat: processing {Count} item(s){DryRun}.", work.Count, config.DryRun ? " (dry run)" : string.Empty);
        file.Info($"Run started — {work.Count} item(s){(config.DryRun ? " (dry run)" : string.Empty)}.");
        file.Info($"Data sets — trendingTv={trendingTv.Count}, top250Tv={top250Tv.Count}, watchedSeries={watchedSeries.Count}, " +
                  $"trendingMovie={trendingMovie.Count}, top250Movie={top250Movie.Count}, watchedMovies={watchedMovies.Count}.");

        int done = 0;
        int updated = 0;
        foreach (var (item, lib, type) in work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var itemName = item.Name ?? string.Empty;
                if (ignore.Contains(itemName) || (limit.Count > 0 && !limit.Contains(itemName)))
                {
                    continue;
                }

                var isTv = type == "tv";
                var tmdbId = isTv
                    ? await ResolveTvTmdbIdAsync(item, tmdb, config, cancellationToken).ConfigureAwait(false)
                    : await ResolveMovieTmdbIdAsync(item, tmdb, cancellationToken).ConfigureAwait(false);
                if (tmdbId is null)
                {
                    _logger.LogDebug("Overcoat: no TMDB id for '{Name}'; skipping.", item.Name);
                    continue;
                }

                // Which badges does this item qualify for, under its library's selection?
                var badgeSet = new HashSet<string>(StringComparer.Ordinal);
                if (config.BadgesEnabled)
                {
                    var watched = isTv ? watchedSeries : watchedMovies;
                    var trending = isTv ? trendingTv : trendingMovie;
                    var top250 = isTv ? top250Tv : top250Movie;
                    if (lib.WatchHistoryBadge && watched.Contains(item.Id))
                    {
                        badgeSet.Add(BadgeCompositor.WatchHistory);
                    }

                    if (lib.TrendingBadge && trending.Contains(tmdbId.Value))
                    {
                        badgeSet.Add(BadgeCompositor.TmdbTrending);
                    }

                    if (lib.ImdbTop250Badge && top250.Contains(tmdbId.Value))
                    {
                        badgeSet.Add(BadgeCompositor.ImdbTop250);
                    }
                }

                if (await ProcessItemAsync(item, lib, type, tmdbId.Value, badgeSet, tmdb, renderer, badges, state, file, config, cancellationToken).ConfigureAwait(false))
                {
                    updated++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overcoat: failed processing '{Name}'.", item.Name);
                file.Error((item.Name ?? "?") + " — " + ex.Message);
            }
            finally
            {
                done++;
                progress.Report(100.0 * done / work.Count);
            }
        }

        state.Flush();
        _logger.LogInformation("Overcoat: done. {Updated}/{Count} poster(s) updated.", updated, work.Count);
        file.Info($"Done — {updated}/{work.Count} poster(s) updated.");
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

    private async Task<bool> ProcessItemAsync(
        BaseItem item,
        LibraryConfig lib,
        string type,
        int tmdbId,
        ISet<string> badgeSet,
        TmdbService tmdb,
        OverlayRenderer renderer,
        BadgeCompositor badges,
        ProcessingState state,
        FileLog file,
        PluginConfiguration config,
        CancellationToken ct)
    {
        // Status banner (TV only, when the library has status overlays on). Fetch the TMDB status
        // once and reuse it for the poster fallback below — avoids a second /tv/{id} call.
        // `text` is what's rendered (custom label + date); `iconKey` is the canonical identity used
        // for the icon/colour; `cacheText` is the date-free label used for change detection so the
        // returning date drifting day-to-day doesn't churn reprocessing.
        string? text = null;
        string? cacheText = null;
        string iconKey = string.Empty;
        var statusKey = type == "tv" ? "tv" : "movie";
        TmdbService.TvStatusInfo? info = null;
        if (type == "tv" && lib.StatusOverlays)
        {
            info = await tmdb.GetTvStatusAsync(tmdbId, ct).ConfigureAwait(false);
            if (info is not null)
            {
                statusKey = info.Status ?? "tv";
                if (StatusOverlayResolver.ResolveIdentity(info) is { } res && config.IsStatusShown(res.Identity))
                {
                    iconKey = res.Identity;
                    var label = config.LabelForStatus(res.Identity);
                    cacheText = label;
                    text = string.IsNullOrEmpty(res.DateSuffix) ? label : $"{label} {res.DateSuffix}";
                }
            }
        }

        var id = item.Id.ToString("N");
        var currentSig = ProcessingState.Signature(item);
        var changedExternally = state.ExternallyChanged(id, currentSig);

        // Item now qualifies for nothing. If we previously overlaid it (clean original vaulted),
        // restore that original so unticking everything reverts the poster — unless the art was
        // changed outside Overcoat, in which case leave their art and just stop tracking it.
        if (text is null && badgeSet.Count == 0)
        {
            if (state.HasOriginal(id) && !changedExternally)
            {
                if (config.DryRun)
                {
                    file.Info((item.Name ?? "?") + " → [dry run] would restore clean original (no overlays)");
                    return false;
                }

                var clean = await state.ReadOriginalAsync(id, ct).ConfigureAwait(false);
                if (clean is not null)
                {
                    using var msClean = new MemoryStream(clean);
                    await _providerManager.SaveImage(item, msClean, "image/png", ImageType.Primary, null, ct).ConfigureAwait(false);
                    await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ct).ConfigureAwait(false);
                }

                state.Remove(id);
                _logger.LogInformation("Overcoat: '{Name}' has no overlays now — restored clean original.", item.Name);
                file.Info((item.Name ?? "?") + " → no overlays now; restored clean original");
                return true;
            }

            // Never overlaid by us, or the art was changed externally — nothing to revert; drop stale tracking.
            if (changedExternally)
            {
                state.Remove(id);
            }

            return false;
        }

        if (changedExternally)
        {
            _logger.LogInformation("Overcoat: '{Name}' poster changed externally — re-baselining.", item.Name);
            file.Info((item.Name ?? "?") + " — poster changed externally, re-baselining");
            state.InvalidateOriginal(id);
        }

        // Fingerprint the banner appearance so changing any banner setting forces every banner'd item
        // to re-render. Empty for items with no banner (badge-only/movies), so an appearance tweak
        // never needlessly reprocesses them. (Label changes are caught via cacheText below.)
        var bannerColor = iconKey.Length == 0 ? string.Empty : config.ColorForIdentity(iconKey);
        var appearanceKey = text is null
            ? string.Empty
            : string.Join("|", new[]
            {
                config.BannerStyle, config.BannerShape, config.BannerPosition,
                ((int)Math.Round(config.BannerFontScale * 1000)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                config.BannerIcons.ToString(), bannerColor,
                config.BannerFullWidth.ToString(), config.BannerAlign,
                config.BannerShadow ? config.BannerShadowStrength.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0",
                config.GlassTint, config.GlassTintStrength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                config.GlassBlur.ToString(System.Globalization.CultureInfo.InvariantCulture),
                config.NeonGlow.ToString(System.Globalization.CultureInfo.InvariantCulture), config.BannerFont,
            });

        if (!state.NeedsProcessing(id, statusKey, badgeSet, cacheText, currentSig, appearanceKey, config.CacheEnabled))
        {
            _logger.LogDebug("Overcoat: '{Name}' unchanged — skipping.", item.Name ?? "?");
            return false;
        }

        // Always overlay the clean original (vault → current poster → TMDB), never the live poster.
        var original = await state.ReadOriginalAsync(id, ct).ConfigureAwait(false);
        if (original is null)
        {
            original = await AcquireSourcePosterAsync(item, type, tmdbId, info, tmdb, ct).ConfigureAwait(false);
            if (original is null)
            {
                _logger.LogDebug("Overcoat: '{Name}' has no usable poster; skipping.", item.Name ?? "?");
                return false;
            }

            await state.SaveOriginalAsync(id, original, ct).ConfigureAwait(false);
        }

        using var bmp = OverlayRenderer.Decode(original);
        if (bmp is null)
        {
            _logger.LogWarning("Overcoat: could not decode poster for '{Name}'.", item.Name);
            return false;
        }

        if (text is not null)
        {
            renderer.DrawStatusBanner(bmp, text, new OverlayRenderer.BannerOptions
            {
                Style = config.BannerStyle,
                Shape = config.BannerShape,
                Position = config.BannerPosition,
                FontScale = config.BannerFontScale,
                ShowIcons = config.BannerIcons,
                IconKey = iconKey,
                ColorOverride = bannerColor,
                FullWidth = config.BannerFullWidth,
                Align = config.BannerAlign,
                Shadow = config.BannerShadow,
                ShadowStrength = config.BannerShadowStrength,
                GlassTint = config.GlassTint,
                GlassTintStrength = config.GlassTintStrength,
                GlassBlur = config.GlassBlur,
                NeonGlow = config.NeonGlow,
                Font = config.BannerFont,
            });
        }

        badges.Apply(renderer, bmp, badgeSet);
        var png = OverlayRenderer.EncodePng(bmp);

        if (config.DryRun)
        {
            _logger.LogInformation("Overcoat: [dry run] '{Name}' → banner='{Text}' badges=[{Badges}].", item.Name, text ?? "-", string.Join(",", badgeSet));
            file.Info($"[dry run] {item.Name} → banner='{text ?? "-"}' badges=[{string.Join(",", badgeSet)}]");
            return true;
        }

        using var ms = new MemoryStream(png);
        await _providerManager.SaveImage(item, ms, "image/png", ImageType.Primary, null, ct).ConfigureAwait(false);
        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ct).ConfigureAwait(false);

        var produced = _libraryManager.GetItemById(item.Id) ?? item;
        state.MarkProcessed(id, item.Name ?? string.Empty, statusKey, cacheText, badgeSet, ProcessingState.Signature(produced), appearanceKey);

        _logger.LogInformation("Overcoat: '{Name}' → banner='{Text}' badges=[{Badges}].", item.Name, text ?? "-", string.Join(",", badgeSet));
        file.Info($"{item.Name} → banner='{text ?? "-"}' badges=[{string.Join(",", badgeSet)}]");
        return true;
    }

    /// <summary>Clean source poster: the Jellyfin file if present; for TV, else the TMDB poster (movies have no fallback).</summary>
    private async Task<byte[]?> AcquireSourcePosterAsync(BaseItem item, string type, int tmdbId, TmdbService.TvStatusInfo? info, TmdbService tmdb, CancellationToken ct)
    {
        if (item.HasImage(ImageType.Primary, 0))
        {
            var sourcePath = item.GetImagePath(ImageType.Primary, 0);
            if (File.Exists(sourcePath))
            {
                return await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
            }
        }

        if (type == "tv")
        {
            info ??= await tmdb.GetTvStatusAsync(tmdbId, ct).ConfigureAwait(false);
            if (info?.PosterPath is { Length: > 0 } posterPath
                && await tmdb.DownloadPosterAsync(posterPath, ct).ConfigureAwait(false) is { } fetched)
            {
                _logger.LogInformation("Overcoat: '{Name}' had no usable Jellyfin poster; using TMDB poster.", item.Name);
                return fetched;
            }
        }

        return null;
    }

    /// <summary>TV TMDB id: overrides → ProviderIds.Tmdb → Imdb/Tvdb via /find → title search.</summary>
    private async Task<int?> ResolveTvTmdbIdAsync(BaseItem item, TmdbService tmdb, PluginConfiguration config, CancellationToken ct)
    {
        var name = item.Name ?? string.Empty;
        var year = item.ProductionYear;

        foreach (var ov in config.TmdbOverrides)
        {
            if (string.Equals(ov.Name, name, StringComparison.OrdinalIgnoreCase) && (ov.Year == 0 || ov.Year == year))
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

    /// <summary>Movie TMDB id: ProviderIds.Tmdb → Imdb via /find. No title-search fallback (mirrors the reference).</summary>
    private async Task<int?> ResolveMovieTmdbIdAsync(BaseItem item, TmdbService tmdb, CancellationToken ct)
    {
        if (TryGetProvider(item, "Tmdb", out var raw) && int.TryParse(raw, out var direct))
        {
            return direct;
        }

        if (TryGetProvider(item, "Imdb", out var imdb) && !string.IsNullOrEmpty(imdb))
        {
            return await tmdb.FindByExternalIdAsync(imdb!, "imdb_id", movie: true, ct).ConfigureAwait(false);
        }

        return null;
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
