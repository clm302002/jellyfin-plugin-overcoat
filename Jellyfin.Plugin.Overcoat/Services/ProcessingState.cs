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
    public enum ArtworkChannel
    {
        Primary,
        Thumb,
    }
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
    public ArtworkChannel Channel { get; }
    public ImageType ImageType => Channel == ArtworkChannel.Thumb ? ImageType.Thumb : ImageType.Primary;

    public ProcessingState(string dataFolder, ILogger logger, ArtworkChannel channel = ArtworkChannel.Primary)
    {
        Channel = channel;
        _originalsDir = Path.Combine(dataFolder, channel == ArtworkChannel.Thumb ? "thumb-originals" : "originals");
        _statePath = Path.Combine(dataFolder, channel == ArtworkChannel.Thumb ? "thumb-state.json" : "state.json");
        _logger = logger;
        Directory.CreateDirectory(_originalsDir);
        _cache = Load();
    }

    /// <summary>Primary-image signature: its on-disk modified time (ticks). 0 if no image.</summary>
    public static long Signature(BaseItem item)
        => item.GetImageInfo(ImageType.Primary, 0)?.DateModified.Ticks ?? 0;

    public long ImageSignature(BaseItem item)
        => item.GetImageInfo(ImageType, 0)?.DateModified.Ticks ?? 0;

    /// <summary>
    /// The real MIME type of an image, from its magic bytes.
    ///
    /// Vault files are all named <c>.png</c> because that is what the vault writes them as, but the
    /// bytes are whatever the source poster was — on a real library that measured 372 WebP and 173
    /// JPEG out of 545, and not one actual PNG. Declaring them all <c>image/png</c> to
    /// <c>SaveImage</c> hands Jellyfin a content type that contradicts the payload; it currently
    /// tolerates it, but the mismatch is not something to rely on.
    /// </summary>
    public static string DetectMimeType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 12
            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "image/webp";
        }

        // Unknown: PNG is what the vault has always claimed, so keep the historical behaviour rather
        // than failing the restore outright.
        return "image/png";
    }

    /// <summary>Content hash of an image we produced, used to recognise our own output later.</summary>
    public static string HashBytes(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>
    /// Reads an item's current primary image straight off disk, or null if there is none or it can't
    /// be read. Null means <em>unknown</em>, never "different" — callers must not take a destructive
    /// action on it. Shared by the apply and restore tasks so both judge "is this still ours?" the
    /// same way.
    /// </summary>
    public static async Task<byte[]?> ReadPrimaryImageAsync(BaseItem item, ILogger logger, CancellationToken ct)
        => await ReadImageAsync(item, ImageType.Primary, logger, ct).ConfigureAwait(false);

    public async Task<byte[]?> ReadImageAsync(BaseItem item, CancellationToken ct)
        => await ReadImageAsync(item, ImageType, _logger, ct).ConfigureAwait(false);

    public static async Task<byte[]?> ReadImageAsync(BaseItem item, ImageType imageType, ILogger logger, CancellationToken ct)
    {
        try
        {
            if (!item.HasImage(imageType, 0))
            {
                return null;
            }

            var path = item.GetImagePath(imageType, 0);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false) : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Overcoat: could not read the current {ImageType} image for '{Name}'.", imageType, item.Name ?? "?");
            return null;
        }
    }

    /// <summary>The signature we last recorded for an item, or 0 if we have no entry.</summary>
    public long CachedSignature(string id)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(id, out var e) ? e.PrimarySignature : 0;
        }
    }

    /// <summary>
    /// The badge set we last rendered for an item. Used to hold membership steady for a badge source
    /// that failed this run — an outage must not read as "this item lost that badge".
    /// </summary>
    public IReadOnlyCollection<string> CachedBadgeSet(string id)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(id, out var e) ? e.BadgeSet.ToList() : Array.Empty<string>();
        }
    }

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

    private string OriginalPath(string id) => Path.Combine(_originalsDir, id + (Channel == ArtworkChannel.Thumb ? ".img" : ".png"));

    public bool HasOriginal(string id) => File.Exists(OriginalPath(id));

    /// <summary>Size on disk of a vaulted original, or null if it isn't there.</summary>
    public long? OriginalSize(string id)
    {
        try
        {
            var info = new FileInfo(OriginalPath(id));
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Overcoat: could not stat vaulted original for {Id}", id);
            return null;
        }
    }

    /// <summary>
    /// Vaults the clean original. Written to a sibling temp file and then moved into place, so an
    /// interruption mid-write cannot leave a truncated file where the only recoverable copy of the
    /// poster used to be. A same-directory move is atomic on every filesystem we care about. (A-20)
    /// </summary>
    public async Task SaveOriginalAsync(string id, byte[] bytes, CancellationToken ct)
    {
        var final = OriginalPath(id);
        var tmp = final + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, final, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Overcoat: could not remove temp file {Path}", path);
        }
    }

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
            ? Directory.EnumerateFiles(_originalsDir, Channel == ArtworkChannel.Thumb ? "*.img" : "*.png").Select(p => Path.GetFileNameWithoutExtension(p)!)
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
        // Try the live file, then the backup Flush leaves behind. Starting fresh is the worst
        // outcome available here: every item then looks new, gets re-processed, and — worse — items
        // whose overlays should be reverted are no longer known about at all. Losing one run's
        // changes to the .bak is far cheaper than losing the whole map. (A-20)
        foreach (var (path, label) in new[] { (_statePath, "state.json"), (_statePath + ".bak", "state.json.bak") })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path));
                if (loaded is not null)
                {
                    if (path != _statePath)
                    {
                        _logger.LogWarning(
                            "Overcoat: state.json was unreadable; recovered {Count} entries from the backup.",
                            loaded.Count);
                    }

                    return loaded;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Overcoat: couldn't read {File}.", label);
            }
        }

        _logger.LogWarning("Overcoat: no usable state found — starting fresh. Existing overlays will be re-evaluated.");
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

        // Temp file + atomic move, with the previous state kept as .bak. Writing state.json in place
        // meant an interruption could leave it truncated — and it is the record of which posters we
        // overlaid and which vault file belongs to which item. (A-20)
        var tmp = _statePath + ".tmp";
        try
        {
            File.WriteAllText(tmp, json);

            if (File.Exists(_statePath))
            {
                File.Replace(tmp, _statePath, _statePath + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, _statePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overcoat: failed to write state.json.");
            TryDelete(tmp);
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
