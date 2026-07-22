namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// A read-only summary of what Overcoat could still undo.
///
/// The vault is the only route back to an un-overlaid poster, and until now nothing surfaced its
/// state — so "can I get my posters back?" was unanswerable without going and looking at the files.
/// The number that actually matters is <see cref="TrackedWithoutOriginal"/>: those items are overlaid
/// with no saved copy, so Restore cannot help them and only re-fetching art from a provider will.
/// </summary>
public sealed record VaultHealth(
    int TrackedItems,
    int TrackedWithOriginal,
    int TrackedWithoutOriginal,
    int OrphanedOriginals,
    int VaultFiles,
    long VaultBytes)
{
    /// <summary>Vault size rendered for display, e.g. "124 MB".</summary>
    public string VaultSizeDisplay => FormatSize(VaultBytes);

    /// <summary>Human-readable byte size. Deliberately coarse — this is a reassurance figure, not accounting.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }

        string[] units = { "KB", "MB", "GB", "TB" };
        double value = bytes / 1024.0;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value < 10
            ? value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit]
            : value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>
    /// Builds the summary. <paramref name="itemStillExists"/> is injected rather than taking a
    /// library manager so this stays testable without a running Jellyfin.
    /// </summary>
    public static VaultHealth Build(
        IReadOnlyCollection<string> trackedIds,
        IReadOnlyCollection<string> vaultedIds,
        Func<string, long?> originalSize,
        Func<string, bool> itemStillExists)
    {
        var vaulted = new HashSet<string>(vaultedIds, StringComparer.Ordinal);

        var withOriginal = 0;
        var withoutOriginal = 0;
        foreach (var id in trackedIds)
        {
            if (vaulted.Contains(id))
            {
                withOriginal++;
            }
            else
            {
                withoutOriginal++;
            }
        }

        // A vaulted original whose item is gone from the library. Harmless, but it is disk we could
        // reclaim and a signal that the library changed under us.
        var orphans = 0;
        long bytes = 0;
        foreach (var id in vaulted)
        {
            bytes += originalSize(id) ?? 0;
            if (!itemStillExists(id))
            {
                orphans++;
            }
        }

        return new VaultHealth(
            TrackedItems: trackedIds.Count,
            TrackedWithOriginal: withOriginal,
            TrackedWithoutOriginal: withoutOriginal,
            OrphanedOriginals: orphans,
            VaultFiles: vaulted.Count,
            VaultBytes: bytes);
    }
}
