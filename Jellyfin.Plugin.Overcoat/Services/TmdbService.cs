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
    public async Task<TvStatusInfo?> GetTvStatusAsync(int tmdbId, CancellationToken ct)
    {
        try
        {
            var url = $"{Base}/tv/{tmdbId}?api_key={_apiKey}";
            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            if (doc is null)
            {
                return null;
            }

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

            return new TvStatusInfo(status, daysSinceFirst, nextDate, nextDay, daysUntil, daysSinceLast, posterPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TMDB /tv/{Id} failed", tmdbId);
            return null;
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TMDB poster download failed for {Path}", posterPath);
            return null;
        }
    }

    /// <summary>Trending TMDB ids. <paramref name="mediaType"/> is "tv" or "movie"; window "day"/"week".</summary>
    public async Task<HashSet<int>> GetTrendingIdsAsync(string mediaType, string window, CancellationToken ct)
    {
        var ids = new HashSet<int>();
        try
        {
            var w = window == "day" ? "day" : "week";
            var url = $"{Base}/trending/{mediaType}/{w}?api_key={_apiKey}";
            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            if (doc is not null && doc.RootElement.TryGetProperty("results", out var results))
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMDB trending {Type}/{Window} failed", mediaType, window);
        }

        return ids;
    }

    /// <summary>All TMDB ids in a TMDB list (paginated). Used for the IMDB Top 250 lists.</summary>
    public async Task<HashSet<int>> GetListIdsAsync(string listId, CancellationToken ct)
    {
        var ids = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(listId))
        {
            return ids;
        }

        try
        {
            var page = 1;
            while (true)
            {
                var url = $"{Base}/list/{listId}?api_key={_apiKey}&language=en-US&page={page}";
                int count = 0;
                int totalPages = 1;
                using (var doc = await GetJsonAsync(url, ct).ConfigureAwait(false))
                {
                    if (doc is null)
                    {
                        break;
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMDB list {ListId} failed", listId);
        }

        return ids;
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

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogDebug("TMDB {Status} for {Url}", (int)resp.StatusCode, url);
            return null;
        }

        var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);
    }
}
