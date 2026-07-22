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

        var emptyIds = new TmdbService.IdSetResult(new HashSet<int>(), false);
        var emptyWatched = new WatchHistory.WatchedResult(new HashSet<Guid>(), false);

        var trendingTvR = NeedTv(l => l.TrendingBadge) ? await tmdb.GetTrendingIdsAsync("tv", config.TrendingTimeWindow, cancellationToken).ConfigureAwait(false) : emptyIds;
        var top250TvR = NeedTv(l => l.ImdbTop250Badge) ? await tmdb.GetListIdsAsync(config.ImdbTop250TvListId, cancellationToken).ConfigureAwait(false) : emptyIds;
        var watchedSeriesR = NeedTv(l => l.WatchHistoryBadge) ? watch.RecentlyWatchedSeriesIds(config.WatchHistoryDays, config.WatchHistoryAllUsers, config.WatchHistoryUserId, config.WatchHistoryMaxScan) : emptyWatched;
        var trendingMovieR = NeedMovie(l => l.TrendingBadge) ? await tmdb.GetTrendingIdsAsync("movie", config.TrendingTimeWindow, cancellationToken).ConfigureAwait(false) : emptyIds;
        var top250MovieR = NeedMovie(l => l.ImdbTop250Badge) ? await tmdb.GetListIdsAsync(config.ImdbTop250MovieListId, cancellationToken).ConfigureAwait(false) : emptyIds;
        var watchedMoviesR = NeedMovie(l => l.WatchHistoryBadge) ? watch.RecentlyWatchedMovieIds(config.WatchHistoryDays, config.WatchHistoryAllUsers, config.WatchHistoryUserId, config.WatchHistoryMaxScan) : emptyWatched;

        var trendingTv = trendingTvR.Ids;
        var top250Tv = top250TvR.Ids;
        var watchedSeries = watchedSeriesR.Ids;
        var trendingMovie = trendingMovieR.Ids;
        var top250Movie = top250MovieR.Ids;
        var watchedMovies = watchedMoviesR.Ids;

        // If ANY badge source we depend on couldn't be read, a missing badge means "unknown", not
        // "not earned". Movies are badges-only, so an empty badge set would otherwise send them down
        // the revert path below and strip perfectly good posters over a transient TMDB blip.
        var failedSets = new List<string>();
        if (trendingTvR.Failed) { failedSets.Add("TMDB trending (TV)"); }
        if (top250TvR.Failed) { failedSets.Add("TMDB list (TV top 250)"); }
        if (watchedSeriesR.Failed) { failedSets.Add("watch history (series)"); }
        if (trendingMovieR.Failed) { failedSets.Add("TMDB trending (movies)"); }
        if (top250MovieR.Failed) { failedSets.Add("TMDB list (movie top 250)"); }
        if (watchedMoviesR.Failed) { failedSets.Add("watch history (movies)"); }
        var badgeDataIncomplete = failedSets.Count > 0;
        if (badgeDataIncomplete)
        {
            _logger.LogWarning(
                "Overcoat: badge data incomplete this run ({Sets}) — items that would otherwise lose all overlays will be left untouched.",
                string.Join(", ", failedSets));
        }

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
        // Mark failed sets explicitly — a bare "0" can't be told apart from "badge disabled".
        string N(TmdbService.IdSetResult r) => r.Failed ? "FAILED" : r.Ids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string W(WatchHistory.WatchedResult r) => r.Failed ? "FAILED" : r.Ids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        file.Info($"Data sets — trendingTv={N(trendingTvR)}, top250Tv={N(top250TvR)}, watchedSeries={W(watchedSeriesR)}, " +
                  $"trendingMovie={N(trendingMovieR)}, top250Movie={N(top250MovieR)}, watchedMovies={W(watchedMoviesR)}.");
        if (badgeDataIncomplete)
        {
            file.Error("Badge data incomplete (" + string.Join(", ", failedSets) + ") — posters that would lose all overlays are being left untouched.");
        }

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

                if (await ProcessItemAsync(item, lib, type, tmdbId.Value, badgeSet, tmdb, renderer, badges, state, file, config, badgeDataIncomplete, cancellationToken).ConfigureAwait(false))
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

        // Surface TMDB trouble loudly. A run that couldn't reach TMDB leaves posters untouched rather
        // than stripping them, but the user still needs to know why nothing changed.
        if (tmdb.FailedRequests > 0)
        {
            _logger.LogWarning(
                "Overcoat: {Failed} TMDB request(s) failed this run — affected items were left untouched. "
                + "Check your TMDB API key and the server's connectivity to api.themoviedb.org.",
                tmdb.FailedRequests);
            file.Error($"{tmdb.FailedRequests} TMDB request(s) failed this run — affected posters left untouched. Check the TMDB API key / network.");
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// The trigger used the first time Jellyfin registers this task. Jellyfin never consults this
    /// again afterwards, so the run time configured on the plugin page is applied to the live task by
    /// <see cref="ScheduleSync"/> instead. Both read the same config, so a fresh install and a
    /// reconfigured one agree.
    /// </summary>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return BuildDailyTrigger(Plugin.Instance?.Configuration.ScheduleTimeOfDay ?? TimeSpan.FromHours(3));
    }

    /// <summary>Builds the daily trigger for a given time of day.</summary>
    internal static TaskTriggerInfo BuildDailyTrigger(TimeSpan timeOfDay) => new()
    {
        Type = TaskTriggerInfoType.DailyTrigger,
        TimeOfDayTicks = timeOfDay.Ticks,
    };

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
        bool badgeDataIncomplete,
        CancellationToken ct)
    {
        // Status banner (TV only, when the library has status overlays on). Fetch the TMDB status
        // once and reuse it for the poster fallback below — avoids a second /tv/{id} call.
        // `text` is what's rendered (custom label + optional next-air date); `iconKey` is the canonical
        // identity used for the icon/colour; `cacheText` carries the full text so a changed date
        // re-renders (kept current).
        string? text = null;
        string? cacheText = null;
        string iconKey = string.Empty;
        var statusKey = type == "tv" ? "tv" : "movie";
        TmdbService.TvStatusInfo? info = null;
        if (type == "tv" && lib.StatusOverlays)
        {
            var status = await tmdb.GetTvStatusAsync(tmdbId, ct).ConfigureAwait(false);

            // TMDB gave us no answer (rate limit / bad key / outage). We therefore do NOT know whether
            // this show still warrants a banner — and "no banner" is destructive here: the branch below
            // would restore the clean original and wipe a perfectly good overlay. Leave the poster
            // exactly as it is and retry next run. (This is what caused overlays to vanish overnight.)
            if (status.Failed)
            {
                _logger.LogWarning(
                    "Overcoat: TMDB lookup failed for '{Name}' — leaving its poster untouched this run.",
                    item.Name ?? "?");
                file.Info((item.Name ?? "?") + " → TMDB unavailable; poster left untouched (will retry next run)");
                return false;
            }

            info = status.Info;
            if (info is not null)
            {
                statusKey = info.Status ?? "tv";
                var banner = ResolveBanner(StatusOverlayResolver.ResolveIdentity(info), info, config);
                iconKey = banner.IconKey;
                text = banner.Text;
                cacheText = text;
            }
        }

        var id = item.Id.ToString("N");
        var currentSig = ProcessingState.Signature(item);

        // The mtime signature only tells us the file was touched, not that its bytes changed. Confirm
        // against the hash of what we last wrote before believing it — otherwise a library scan or a
        // metadata write is mistaken for someone replacing the art, and the re-baseline below
        // re-overlays an already-overlaid poster.
        var contentConfirmedDifferent = false;

        // Upgrade path: an entry from <=0.6.0 whose file is demonstrably untouched since we wrote it
        // (its recorded mtime still matches) gets its hash backfilled here. Nothing else in the run
        // would do it — a settled library re-renders nothing, so no hash would ever be written and
        // the content check below would stay permanently inert. One cheap read per item, once.
        if (state.NeedsHashBackfill(id, currentSig))
        {
            var settledBytes = await ReadPrimaryImageAsync(item, ct).ConfigureAwait(false);
            if (settledBytes is not null)
            {
                state.SetProducedHash(id, ProcessingState.HashBytes(settledBytes));
                _logger.LogDebug("Overcoat: recorded a content hash for '{Name}' (upgraded entry).", item.Name ?? "?");
            }
        }

        if (state.SignatureChanged(id, currentSig))
        {
            var knownHash = state.ProducedHashFor(id);
            if (knownHash.Length == 0)
            {
                // Written by <=0.6.0, so there is no hash to compare against. Nothing to decide here:
                // the moved signature alone makes NeedsProcessing re-render below, which records a
                // hash and makes every later run decidable. Crucially we do NOT treat it as a
                // confirmed replacement, so the render sources from the vaulted clean original rather
                // than the live (possibly already-overlaid) poster. Self-heals after a single run.
            }
            else
            {
                var currentBytes = await ReadPrimaryImageAsync(item, ct).ConfigureAwait(false);
                if (currentBytes is not null && string.Equals(ProcessingState.HashBytes(currentBytes), knownHash, StringComparison.Ordinal))
                {
                    // Same bytes we wrote — only the timestamp moved. Adopt the new signature so the
                    // cheap check passes next run, and carry on as unchanged.
                    state.RefreshSignature(id, currentSig);
                    _logger.LogDebug("Overcoat: '{Name}' timestamp changed but content is unchanged; not re-baselining.", item.Name ?? "?");
                }
                else
                {
                    // Confirmed: the bytes on disk are not ours. Only now is it right to abandon the
                    // vaulted original and re-baseline on whatever replaced it.
                    contentConfirmedDifferent = true;
                }
            }
        }

        // Item now qualifies for nothing. If we previously overlaid it (clean original vaulted),
        // restore that original so unticking everything reverts the poster — unless the art was
        // changed outside Overcoat, in which case leave their art and just stop tracking it.
        if (text is null && badgeSet.Count == 0)
        {
            // ...but only when we actually know it earns nothing. With a badge source unread, an empty
            // badge set means "unknown", and reverting on that would strip a good poster.
            if (badgeDataIncomplete)
            {
                _logger.LogWarning(
                    "Overcoat: '{Name}' would lose all overlays, but badge data is incomplete this run — leaving its poster untouched.",
                    item.Name ?? "?");
                file.Info((item.Name ?? "?") + " → badge data incomplete; poster left untouched (will retry next run)");
                return false;
            }

            // Only a *confirmed* replacement should block the revert. A bare timestamp move we
            // couldn't verify must not — otherwise we skip the restore and fall through to dropping
            // the vault below, destroying the clean original we were about to put back.
            if (state.HasOriginal(id) && !contentConfirmedDifferent)
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

            // Never overlaid by us, or the art was confirmed replaced — nothing to revert onto, so
            // drop the stale tracking. Deliberately NOT done for an unverified timestamp move: that
            // would discard a clean original we may still need.
            if (contentConfirmedDifferent)
            {
                state.Remove(id);
            }

            return false;
        }

        // Re-baseline on the replacement art only when we actually confirmed the bytes differ. Do NOT
        // delete the vaulted original here either — if acquiring the new poster then fails, that
        // deletion would have destroyed the only clean copy with nothing to put in its place. Skip
        // the vault *read* instead, and let the successful write below overwrite it.
        var forceReacquire = contentConfirmedDifferent;
        if (contentConfirmedDifferent)
        {
            _logger.LogInformation("Overcoat: '{Name}' poster changed externally — re-baselining.", item.Name);
            file.Info((item.Name ?? "?") + " — poster changed externally, re-baselining");
        }

        // Fingerprint the banner appearance so changing any banner setting forces every banner'd item
        // to re-render. Empty for items with no banner (badge-only/movies), so an appearance tweak
        // never needlessly reprocesses them. (Label changes are caught via cacheText below.)
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var bannerColor = iconKey.Length == 0 ? string.Empty : config.ColorForIdentity(iconKey);
        var keyParts = new List<string>();
        if (text is not null)
        {
            keyParts.Add(string.Join("|", new[]
            {
                config.BannerStyle, config.BannerShape, config.BannerPosition,
                ((int)Math.Round(config.BannerFontScale * 1000)).ToString(inv),
                config.BannerIcons.ToString(), bannerColor,
                config.BannerFullWidth.ToString(), config.BannerAlign,
                config.BannerShadow ? config.BannerShadowStrength.ToString(inv) : "0",
                config.GlassTint, config.GlassTintStrength.ToString(inv),
                config.GlassBlur.ToString(inv),
                config.NeonGlow.ToString(inv), config.BannerFont,
            }));
        }

        // Include badge layout when the item carries badges, so changing placement/size/gap re-renders.
        if (badgeSet.Count > 0)
        {
            keyParts.Add(string.Join("|", new[]
            {
                "badges", config.BadgeSide, config.BadgeVertical,
                config.BadgeScale.ToString(inv), config.BadgeGapPercent.ToString(inv),
            }));
        }

        var appearanceKey = string.Join("||", keyParts);

        if (!state.NeedsProcessing(id, statusKey, badgeSet, cacheText, currentSig, appearanceKey, config.CacheEnabled))
        {
            _logger.LogDebug("Overcoat: '{Name}' unchanged — skipping.", item.Name ?? "?");
            return false;
        }

        // Always overlay the clean original (vault → current poster → TMDB), never the live poster.
        // On a genuine external replacement the vault is deliberately bypassed so we re-baseline on
        // the new art — but the old vault file stays on disk until the new one overwrites it.
        var original = forceReacquire ? null : await state.ReadOriginalAsync(id, ct).ConfigureAwait(false);
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

        badges.Apply(renderer, bmp, badgeSet, new BadgeCompositor.BadgeLayout(
            string.Equals(config.BadgeSide, "right", StringComparison.OrdinalIgnoreCase),
            config.BadgeVertical,
            config.BadgeScale,
            config.BadgeGapPercent));
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

        // Hash what actually landed on disk, not the bytes we handed to SaveImage — if Jellyfin
        // re-encodes or rewrites the file, hashing our intended bytes would never match on read-back
        // and the whole content check would silently degrade to the old mtime-only behaviour. An
        // empty hash (read-back failed) does exactly that, deliberately, rather than lying.
        var writtenBytes = await ReadPrimaryImageAsync(produced, ct).ConfigureAwait(false);
        var producedHash = writtenBytes is not null ? ProcessingState.HashBytes(writtenBytes) : string.Empty;

        state.MarkProcessed(
            id,
            item.Name ?? string.Empty,
            statusKey,
            cacheText,
            badgeSet,
            ProcessingState.Signature(produced),
            appearanceKey,
            producedHash);

        _logger.LogInformation("Overcoat: '{Name}' → banner='{Text}' badges=[{Badges}].", item.Name, text ?? "-", string.Join(",", badgeSet));
        file.Info($"{item.Name} → banner='{text ?? "-"}' badges=[{string.Join(",", badgeSet)}]");
        return true;
    }

    /// <summary>
    /// Resolves the banner (icon identity + rendered text) for a TV show. AIRING (actively airing)
    /// shows the next episode in its chosen format, always (no window). RETURNING shows the upcoming
    /// episode/season date per its format + window. If AIRING is opted out, the show falls back to
    /// RETURNING using the upcoming-episode data already in hand (no extra TMDB call). Statuses turned
    /// off → no banner.
    /// </summary>
    private static (string IconKey, string? Text) ResolveBanner(
        string? identity,
        TmdbService.TvStatusInfo info,
        PluginConfiguration config)
    {
        if (identity is null)
        {
            return (string.Empty, null);
        }

        if (identity == "AIRING")
        {
            if (config.ShowAiring)
            {
                // Always show the next episode in the chosen format (no window).
                return ("AIRING", WithSuffix(config.LabelForStatus("AIRING"),
                    FormatAirSuffix(config.AiringDateFormat, int.MaxValue, InfoToAir(info))));
            }

            // Opted out of AIRING → fall back to RETURNING with the upcoming episode we already have.
            if (!config.ShowReturning)
            {
                return (string.Empty, null);
            }

            return ("RETURNING", WithSuffix(config.LabelForStatus("RETURNING"),
                FormatAirSuffix(config.ReturningDateFormat, config.ReturningDateWindowDays, InfoToAir(info))));
        }

        if (identity == "RETURNING")
        {
            if (!config.ShowReturning)
            {
                return (string.Empty, null);
            }

            return ("RETURNING", WithSuffix(config.LabelForStatus("RETURNING"),
                FormatAirSuffix(config.ReturningDateFormat, config.ReturningDateWindowDays, InfoToAir(info))));
        }

        // NEW / ENDED / CANCELED — label only, no date.
        if (!config.IsStatusShown(identity))
        {
            return (string.Empty, null);
        }

        return (identity, config.LabelForStatus(identity));
    }

    private static string WithSuffix(string label, string? suffix)
        => string.IsNullOrEmpty(suffix) ? label : $"{label} {suffix}";

    private static TmdbService.EpisodeAir? InfoToAir(TmdbService.TvStatusInfo info)
        => info.NextAirDate is { } d && info.DaysUntilAir is { } du
            ? new TmdbService.EpisodeAir(d, info.NextAirDay ?? string.Empty, du)
            : null;

    /// <summary>Formats an episode's air info per the status's format + window. Null when out of window or unavailable.</summary>
    private static string? FormatAirSuffix(string? format, int window, TmdbService.EpisodeAir? air)
    {
        if (air is null || air.DaysUntil < 0 || air.DaysUntil > window)
        {
            return null;
        }

        return format?.ToLowerInvariant() switch
        {
            "day" => string.IsNullOrEmpty(air.Day) ? null : air.Day,
            "countdown" => $"{air.DaysUntil}d",
            _ => string.IsNullOrEmpty(air.Date) ? null : air.Date,
        };
    }

    /// <summary>Clean source poster: the Jellyfin file if present; for TV, else the TMDB poster (movies have no fallback).</summary>
    /// <summary>
    /// Reads the item's current primary image bytes straight off disk, or null if there isn't one.
    /// Used to confirm whether a changed timestamp actually means changed content.
    /// </summary>
    private async Task<byte[]?> ReadPrimaryImageAsync(BaseItem item, CancellationToken ct)
    {
        try
        {
            if (!item.HasImage(ImageType.Primary, 0))
            {
                return null;
            }

            var path = item.GetImagePath(ImageType.Primary, 0);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false) : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Unreadable — fall back to treating the item as externally changed (the old behaviour).
            _logger.LogDebug(ex, "Overcoat: could not read the current poster for '{Name}'.", item.Name ?? "?");
            return null;
        }
    }

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
            // Poster fallback only — a failure here just means no TMDB poster to fall back to.
            info ??= (await tmdb.GetTvStatusAsync(tmdbId, ct).ConfigureAwait(false)).Info;
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
