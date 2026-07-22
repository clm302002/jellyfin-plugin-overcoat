using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// TMDB v3 client. Replaces the Python <c>TMDBClient</c> (which used the <c>tmdbv3api</c> wrapper) —
/// here it's plain REST over an injected <see cref="HttpClient"/>. Only the endpoints the pipeline
/// needs are implemented; trending / list endpoints arrive with the badge phase.
/// </summary>
public sealed class TmdbService
{
    private const string Base = "https://api.themoviedb.org/3";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger _logger;

    public TmdbService(HttpClient http, string apiKey, ILogger logger)
    {
        _http = http;
        _apiKey = apiKey;
        _logger = logger;
    }

    /// <summary>
    /// How a TMDB request ended. The distinction matters: <see cref="NotFound"/> is a real answer
    /// ("TMDB has nothing for this id"), while <see cref="Failed"/> means we never got an answer
    /// (rate limit, bad key, 5xx, DNS/timeout). Callers must not treat the second as the first —
    /// doing so is what let a transient outage strip overlays off already-overlaid posters.
    /// </summary>
    public enum FetchOutcome
    {
        /// <summary>TMDB answered.</summary>
        Ok,

        /// <summary>TMDB answered "no such item" (404).</summary>
        NotFound,

        /// <summary>TMDB could not be reached or refused the request. The answer is unknown.</summary>
        Failed,
    }

    /// <summary>
    /// A TV status lookup. <see cref="Info"/> is non-null only when <see cref="Outcome"/> is
    /// <see cref="FetchOutcome.Ok"/>; check <see cref="Failed"/> before concluding a show has no status.
    /// </summary>
    public readonly record struct TvStatusResult(TvStatusInfo? Info, FetchOutcome Outcome)
    {
        /// <summary>Gets a value indicating whether TMDB could not be reached (answer unknown).</summary>
        public bool Failed => Outcome == FetchOutcome.Failed;
    }

    /// <summary>
    /// Gets the number of TMDB requests that failed outright during this instance's lifetime (one run).
    /// Lets the task log a run summary instead of burying per-item failures.
    /// </summary>
    public int FailedRequests { get; private set; }

    /// <summary>
    /// A set of TMDB ids plus whether the fetch that produced it failed. An empty set with
    /// <see cref="Failed"/> set means "we don't know what's in this set", which is very different
    /// from "this set is empty" — the latter legitimately strips badges, the former must not.
    /// </summary>
    public readonly record struct IdSetResult(HashSet<int> Ids, bool Failed);

    /// <summary>Air-date/status snapshot for a show. Mirrors what <c>process_show</c> gathers from TMDB.</summary>
    public sealed record TvStatusInfo(
        string? Status,
        int? DaysSinceFirstAir,
        string? NextAirDate, // "M/D"
        string? NextAirDay,  // abbreviated weekday, e.g. "Tue"
        int? DaysUntilAir,
        int? DaysSinceLastAir,
        string? PosterPath); // TMDB poster_path, for the no-Jellyfin-poster fallback

    /// <summary>An episode's air info: "M/D" date, abbreviated weekday, and days until it airs.</summary>
    public sealed record EpisodeAir(string Date, string Day, int DaysUntil);

    /// <summary>Resolves a TMDB id from an IMDB/TVDB id via /find. Mirrors <c>find_by_external_id</c>.</summary>
    public async Task<int?> FindByExternalIdAsync(
        string externalId,
        string source, // "imdb_id" | "tvdb_id"
        bool movie,
        CancellationToken ct)
    {
        try
        {
            var url = $"{Base}/find/{Uri.EscapeDataString(externalId)}?api_key={_apiKey}&external_source={source}";
            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            if (doc is null)
            {
                return null;
            }

            var key = movie ? "movie_results" : "tv_results";
            if (doc.RootElement.TryGetProperty(key, out var results) && results.GetArrayLength() > 0)
            {
                return results[0].GetProperty("id").GetInt32();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TMDB find {Source}={Id} failed", source, externalId);
        }

        return null;
    }

    /// <summary>Fuzzy title search for a TV show (year-preferred). Mirrors <c>search_show</c>.</summary>
    public async Task<int?> SearchShowAsync(string name, int? year, CancellationToken ct)
    {
        try
        {
            var url = $"{Base}/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(name)}";
            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            if (doc is null || !doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                _logger.LogWarning("No TMDB results for: {Name}", name);
                return null;
            }

            if (year is not null)
            {
                foreach (var r in results.EnumerateArray())
                {
                    if (r.TryGetProperty("first_air_date", out var fad)
                        && fad.GetString() is { Length: > 0 } d
                        && d.StartsWith(year.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    {
                        return r.GetProperty("id").GetInt32();
                    }
                }
            }

            return results[0].GetProperty("id").GetInt32();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMDB search error for '{Name}'", name);
            return null;
        }
    }

    /// <summary>
    /// Fetches status + first/next/last air-date info in a single /tv/{id} call. Combines the Python
    /// <c>get_show_status</c>, <c>calculate_days_since_first_air</c> and <c>get_next_air_date</c>.
    /// </summary>
    public async Task<TvStatusResult> GetTvStatusAsync(int tmdbId, CancellationToken ct)
    {
        try
        {
            var url = $"{Base}/tv/{tmdbId}?api_key={_apiKey}";
            var (doc, outcome) = await FetchJsonAsync(url, ct).ConfigureAwait(false);
            if (doc is null)
            {
                return new TvStatusResult(null, outcome);
            }

            using var owned = doc;
            var root = doc.RootElement;
            var today = DateTime.Now.Date; // matches the script's local-time .date() semantics

            string? status = root.TryGetProperty("status", out var s) ? s.GetString() : null;

            int? daysSinceFirst = null;
            if (TryGetDate(root, "first_air_date", out var firstAir))
            {
                daysSinceFirst = (today - firstAir).Days;
            }

            string? nextDate = null;
            string? nextDay = null;
            int? daysUntil = null;
            if (root.TryGetProperty("next_episode_to_air", out var next)
                && next.ValueKind == JsonValueKind.Object
                && TryGetDate(next, "air_date", out var nextAir))
            {
                daysUntil = (nextAir - today).Days;
                nextDate = $"{nextAir.Month}/{nextAir.Day}";
                nextDay = nextAir.ToString("ddd", CultureInfo.InvariantCulture);
            }

            int? daysSinceLast = null;
            if (root.TryGetProperty("last_episode_to_air", out var last)
                && last.ValueKind == JsonValueKind.Object
                && TryGetDate(last, "air_date", out var lastAir))
            {
                daysSinceLast = (today - lastAir).Days;
            }

            string? posterPath = root.TryGetProperty("poster_path", out var pp) ? pp.GetString() : null;

            return new TvStatusResult(
                new TvStatusInfo(status, daysSinceFirst, nextDate, nextDay, daysUntil, daysSinceLast, posterPath),
                FetchOutcome.Ok);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Parsing blew up on a response we did get. Still "unknown", not "no status" — never let
            // this collapse into the caller's revert path.
            FailedRequests++;
            _logger.LogWarning(ex, "TMDB /tv/{Id} could not be parsed; leaving this item's poster untouched.", tmdbId);
            return new TvStatusResult(null, FetchOutcome.Failed);
        }
    }

    /// <summary>
    /// Downloads the original-size poster image for a TMDB <c>poster_path</c>. Mirrors the Python
    /// <c>download_poster</c> fallback used when Jellyfin has no primary image. Returns null on failure.
    /// </summary>
    public async Task<byte[]?> DownloadPosterAsync(string posterPath, CancellationToken ct)
    {
        try
        {
            var url = $"https://image.tmdb.org/t/p/original{posterPath}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TMDB poster download failed for {Path}", posterPath);
            return null;
        }
    }

    /// <summary>Trending TMDB ids. <paramref name="mediaType"/> is "tv" or "movie"; window "day"/"week".</summary>
    public async Task<IdSetResult> GetTrendingIdsAsync(string mediaType, string window, CancellationToken ct)
    {
        var ids = new HashSet<int>();
        try
        {
            var w = window == "day" ? "day" : "week";
            var url = $"{Base}/trending/{mediaType}/{w}?api_key={_apiKey}";
            var (doc, outcome) = await FetchJsonAsync(url, ct).ConfigureAwait(false);
            if (doc is null)
            {
                // Couldn't reach TMDB. An empty set here is NOT "nothing is trending" — callers must
                // not strip badges (or revert badge-only posters) on the strength of it.
                return new IdSetResult(ids, outcome == FetchOutcome.Failed);
            }

            using (doc)
            {
                if (doc.RootElement.TryGetProperty("results", out var results))
                {
                    foreach (var r in results.EnumerateArray())
                    {
                        if (r.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
                        {
                            ids.Add(id);
                        }
                    }
                }
            }

            return new IdSetResult(ids, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            FailedRequests++;
            _logger.LogWarning(ex, "TMDB trending {Type}/{Window} failed; badges from it will be treated as unknown.", mediaType, window);
            return new IdSetResult(ids, true);
        }
    }

    /// <summary>All TMDB ids in a TMDB list (paginated). Used for the IMDB Top 250 lists.</summary>
    public async Task<IdSetResult> GetListIdsAsync(string listId, CancellationToken ct)
    {
        var ids = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(listId))
        {
            // Not configured — a genuinely empty set, not a failure.
            return new IdSetResult(ids, false);
        }

        try
        {
            var page = 1;
            while (true)
            {
                var url = $"{Base}/list/{listId}?api_key={_apiKey}&language=en-US&page={page}";
                int count = 0;
                int totalPages = 1;
                var (doc, outcome) = await FetchJsonAsync(url, ct).ConfigureAwait(false);
                using (doc)
                {
                    if (doc is null)
                    {
                        // Bailing out mid-pagination would hand back a partial list that looks
                        // complete — every id on the unread pages would silently lose its badge.
                        // Report the whole set as unknown instead.
                        return new IdSetResult(ids, outcome == FetchOutcome.Failed);
                    }

                    var root = doc.RootElement;
                    if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        count = items.GetArrayLength();
                        foreach (var it in items.EnumerateArray())
                        {
                            if (it.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
                            {
                                ids.Add(id);
                            }
                        }
                    }

                    if (root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var t))
                    {
                        totalPages = t;
                    }
                }

                if (count == 0 || page >= totalPages)
                {
                    break;
                }

                page++;
            }

            return new IdSetResult(ids, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            FailedRequests++;
            _logger.LogWarning(ex, "TMDB list {ListId} failed; badges from it will be treated as unknown.", listId);
            return new IdSetResult(ids, true);
        }
    }

    private static bool TryGetDate(JsonElement obj, string prop, out DateTime date)
    {
        date = default;
        if (obj.TryGetProperty(prop, out var el)
            && el.ValueKind == JsonValueKind.String
            && DateTime.TryParseExact(el.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            date = d;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Callers that only care about the payload. A failure is indistinguishable from "no data" here,
    /// so this must not be used anywhere a null answer causes a destructive action — use
    /// <see cref="FetchJsonAsync"/> and inspect the outcome instead.
    /// </summary>
    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
        => (await FetchJsonAsync(url, ct).ConfigureAwait(false)).Doc;

    /// <summary>
    /// Performs the request and reports how it ended. A 404 is a real "not found"; anything else
    /// non-success (429 rate limit, 401 bad key, 5xx) or a transport exception is <c>Failed</c>.
    /// Failures log at Warning — they used to log at Debug, which made them invisible by default.
    /// </summary>
    private async Task<(JsonDocument? Doc, FetchOutcome Outcome)> FetchJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("TMDB 404 for {Url}", Redact(url));
                return (null, FetchOutcome.NotFound);
            }

            if (!resp.IsSuccessStatusCode)
            {
                FailedRequests++;
                _logger.LogWarning(
                    "TMDB request failed with HTTP {Status} — overlays will be left untouched for affected items. URL: {Url}",
                    (int)resp.StatusCode,
                    Redact(url));
                return (null, FetchOutcome.Failed);
            }

            var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return (await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false), FetchOutcome.Ok);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            FailedRequests++;
            _logger.LogWarning(ex, "TMDB request errored — overlays will be left untouched for affected items. URL: {Url}", Redact(url));
            return (null, FetchOutcome.Failed);
        }
    }

    /// <summary>Strips the api_key from a URL so it never reaches a log file.</summary>
    private string Redact(string url) =>
        string.IsNullOrEmpty(_apiKey) ? url : url.Replace(_apiKey, "***", StringComparison.Ordinal);
}
