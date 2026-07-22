using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// Skip cache + originals vault + self-heal — the in-process port of the Python `ProcessingCache`
/// plus its `cache/originals` backup vault. Lives in the plugin data folder.
///
/// Two jobs:
/// 1. **Originals vault** (`originals/{id}.png`): the clean, un-overlaid poster, saved once per item.
///    Overlays are ALWAYS rendered from this — never from the current (possibly already-overlaid)
///    Jellyfin poster — so repeated runs never stack banners.
/// 2. **Skip cache** (`state.json`): per-item record of status / overlay text / badge set / a
///    "primary signature". An item is reprocessed only when one of those changed. The signature is
///    the Jellyfin primary image's `DateModified` ticks (replaces the REST `ImageTags.Primary`); a
///    mismatch means the art was replaced externally → re-baseline on the new art.
/// </summary>
public sealed class ProcessingState
{
    /// <summary>One cached item record. Public for System.Text.Json.</summary>
    public sealed class Entry
    {
        public string Name { get; set; } = string.Empty;
        public string? TmdbStatus { get; set; }
        public string? OverlayText { get; set; }
        public List<string> BadgeSet { get; set; } = new();
        public long PrimarySignature { get; set; }

        /// <summary>Banner appearance fingerprint (style/shape/position/size) — empty for items with no banner.</summary>
        public string AppearanceKey { get; set; } = string.Empty;

        /// <summary>
        /// Hash of the PNG bytes we last wrote for this item. The mtime signature alone can't tell
        /// our own write from someone else's — anything that touches the file's timestamp without
        /// changing its content (a library scan, a metadata write, a copy) looks like a replacement.
        /// Empty for entries written by v0.6.0 and earlier; those fall back to mtime-only.
        /// </summary>
        public string ProducedHash { get; set; } = string.Empty;

        public string LastProcessed { get; set; } = string.Empty;
    }

    private readonly string _originalsDir;
    private readonly string _statePath;
    private readonly ILogger _logger;
    private readonly Dictionary<string, Entry> _cache;
    private readonly object _gate = new();
    private bool _dirty;

    public ProcessingState(string dataFolder, ILogger logger)
    {
        _originalsDir = Path.Combine(dataFolder, "originals");
        _statePath = Path.Combine(dataFolder, "state.json");
        _logger = logger;
        Directory.CreateDirectory(_originalsDir);
        _cache = Load();
    }

    /// <summary>Primary-image signature: its on-disk modified time (ticks). 0 if no image.</summary>
    public static long Signature(BaseItem item)
        => item.GetImageInfo(ImageType.Primary, 0)?.DateModified.Ticks ?? 0;

    /// <summary>Content hash of a PNG we produced, used to recognise our own output later.</summary>
    public static string HashBytes(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>The hash we recorded for an item, or empty if unknown / never processed.</summary>
    public string ProducedHashFor(string id)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(id, out var e) ? e.ProducedHash : string.Empty;
        }
    }

    /// <summary>
    /// True when we have an entry whose recorded mtime still matches the file on disk — proving it is
    /// untouched since we wrote it — but which carries no content hash (written by &lt;=0.6.0).
    ///
    /// Without this, upgrading is a no-op for a settled library: nothing re-renders, so no hash is
    /// ever recorded, so the content check never engages and every item stays vulnerable to a
    /// timestamp-only false positive. Backfilling from an unchanged file is safe precisely because
    /// the matching signature means the bytes are still ours.
    /// </summary>
    public bool NeedsHashBackfill(string id, long currentSignature)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(id, out var e)
                && e.ProducedHash.Length == 0
                && e.PrimarySignature != 0
                && currentSignature != 0
                && e.PrimarySignature == currentSignature;
        }
    }

    /// <summary>Records a content hash for an existing entry without otherwise touching it.</summary>
    public void SetProducedHash(string id, string hash)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(id, out var e) && !string.Equals(e.ProducedHash, hash, StringComparison.Ordinal))
            {
                e.ProducedHash = hash;
                _dirty = true;
            }
        }
    }

    /// <summary>
    /// Records a new mtime signature for an item whose content we've confirmed is still ours, so the
    /// cheap mtime pre-check passes on subsequent runs instead of re-hashing every time.
    /// </summary>
    public void RefreshSignature(string id, long signature)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(id, out var e) && e.PrimarySignature != signature)
            {
                e.PrimarySignature = signature;
                _dirty = true;
            }
        }
    }

    /// <summary>
    /// True if the item's primary image mtime differs from the one we last produced. This is only a
    /// cheap first pass — an mtime bump does NOT prove the bytes changed, so callers must confirm
    /// with <see cref="ProducedHashFor"/> before treating it as an external replacement. Acting on
    /// this alone destroys the vaulted original and re-overlays an already-overlaid poster.
    /// </summary>
    public bool SignatureChanged(string id, long currentSignature)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(id, out var e)
                && e.PrimarySignature != 0 && currentSignature != 0
                && e.PrimarySignature != currentSignature;
        }
    }

    /// <summary>
    /// Whether the item must be (re)processed. Mirrors the Python <c>needs_processing</c>: new item,
    /// status change, overlay-category change, badge-set change, or external poster change. When
    /// <paramref name="cacheEnabled"/> is false, always returns true (but state is still recorded).
    /// </summary>
    public bool NeedsProcessing(
        string id,
        string? status,
        IEnumerable<string> badgeSet,
        string? overlayText,
        long currentSignature,
        string appearanceKey,
        bool cacheEnabled)
    {
        if (!cacheEnabled)
        {
            return true;
        }

        lock (_gate)
        {
            if (!_cache.TryGetValue(id, out var e))
            {
                return true;
            }

            if (e.TmdbStatus != status)
            {
                return true;
            }

            if (OverlayCategory(e.OverlayText) != OverlayCategory(overlayText))
            {
                return true;
            }

            if (!string.Equals(e.AppearanceKey, appearanceKey, StringComparison.Ordinal))
            {
                return true;
            }

            var current = new SortedSet<string>(badgeSet ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            if (!current.SetEquals(e.BadgeSet))
            {
                return true;
            }

            if (e.PrimarySignature != 0 && currentSignature != 0 && e.PrimarySignature != currentSignature)
            {
                return true;
            }

            return false;
        }
    }

    public void MarkProcessed(
        string id,
        string name,
        string? status,
        string? overlayText,
        IEnumerable<string> badgeSet,
        long signature,
        string appearanceKey,
        string producedHash)
    {
        var entry = new Entry
        {
            Name = name,
            TmdbStatus = status,
            OverlayText = overlayText,
            BadgeSet = (badgeSet ?? Enumerable.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            PrimarySignature = signature,
            AppearanceKey = appearanceKey,
            ProducedHash = producedHash,
            LastProcessed = DateTime.UtcNow.ToString("o"),
        };
        lock (_gate)
        {
            _cache[id] = entry;
            _dirty = true;
        }
    }

    // --- originals vault ---

    private string OriginalPath(string id) => Path.Combine(_originalsDir, id + ".png");

    public bool HasOriginal(string id) => File.Exists(OriginalPath(id));

    public Task SaveOriginalAsync(string id, byte[] bytes, CancellationToken ct)
        => File.WriteAllBytesAsync(OriginalPath(id), bytes, ct);

    public async Task<byte[]?> ReadOriginalAsync(string id, CancellationToken ct)
    {
        var p = OriginalPath(id);
        return File.Exists(p) ? await File.ReadAllBytesAsync(p, ct).ConfigureAwait(false) : null;
    }

    /// <summary>Drop the cached clean original so the next run re-baselines on the current art.</summary>
    public void InvalidateOriginal(string id)
    {
        try
        {
            var p = OriginalPath(id);
            if (File.Exists(p))
            {
                File.Delete(p);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Overcoat: could not delete vaulted original for {Id}", id);
        }
    }

    // --- maintenance ---

    /// <summary>Ids of every vaulted original (used by the restore task / orphan prune).</summary>
    public IEnumerable<string> VaultedIds()
        => Directory.Exists(_originalsDir)
            ? Directory.EnumerateFiles(_originalsDir, "*.png").Select(p => Path.GetFileNameWithoutExtension(p)!)
            : Enumerable.Empty<string>();

    public IReadOnlyCollection<string> CachedIds
    {
        get { lock (_gate) { return _cache.Keys.ToList(); } }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_cache.Remove(id))
            {
                _dirty = true;
            }
        }

        InvalidateOriginal(id);
    }

    // --- persistence ---

    private Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_statePath))
                       ?? new Dictionary<string, Entry>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Overcoat: couldn't read state.json — starting fresh.");
        }

        return new Dictionary<string, Entry>();
    }

    public void Flush()
    {
        string json;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            _dirty = false;
        }

        try
        {
            File.WriteAllText(_statePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overcoat: failed to write state.json.");
            lock (_gate)
            {
                _dirty = true;
            }
        }
    }

    // The exact banner text (incl. any date/day/countdown suffix) drives reprocessing, so a changed
    // next-air date re-renders and stays current. (Was previously collapsed for RETURNING to avoid
    // date churn; the date is now a wanted, configurable feature.)
    private static string OverlayCategory(string? text) => (text ?? string.Empty).ToUpperInvariant();
}
