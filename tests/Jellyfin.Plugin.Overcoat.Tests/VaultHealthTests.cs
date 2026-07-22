using Jellyfin.Plugin.Overcoat.Services;
using Xunit;

namespace Jellyfin.Plugin.Overcoat.Tests;

/// <summary>
/// The Recovery panel exists so someone can trust an answer to "can I get my posters back?".
/// A wrong number there is worse than no number — it would tell a user they're covered when they
/// aren't — so the arithmetic behind it is pinned here.
/// </summary>
public sealed class VaultHealthTests
{
    private static VaultHealth Build(
        string[] tracked,
        string[] vaulted,
        string[]? missingFromLibrary = null,
        long sizeEach = 1024)
    {
        var gone = new HashSet<string>(missingFromLibrary ?? Array.Empty<string>(), StringComparer.Ordinal);
        return VaultHealth.Build(
            tracked,
            vaulted,
            _ => sizeEach,
            id => !gone.Contains(id));
    }

    [Fact]
    public void HealthyLibrary_ReportsEverythingRestorable()
    {
        var h = Build(new[] { "a", "b", "c" }, new[] { "a", "b", "c" });

        Assert.Equal(3, h.TrackedItems);
        Assert.Equal(3, h.TrackedWithOriginal);
        Assert.Equal(0, h.TrackedWithoutOriginal);
        Assert.Equal(0, h.OrphanedOriginals);
    }

    [Fact]
    public void TrackedItemWithNoSavedCopy_IsCountedAsAtRisk()
    {
        // This is the number that actually matters: overlaid, with nothing to restore from.
        var h = Build(new[] { "a", "b", "c" }, new[] { "a" });

        Assert.Equal(1, h.TrackedWithOriginal);
        Assert.Equal(2, h.TrackedWithoutOriginal);
    }

    [Fact]
    public void SavedCopyForARemovedItem_IsCountedAsOrphaned()
    {
        var h = Build(new[] { "a" }, new[] { "a", "gone" }, missingFromLibrary: new[] { "gone" });

        Assert.Equal(1, h.OrphanedOriginals);
        // An orphan is not "at risk" — there is no item left to restore onto.
        Assert.Equal(0, h.TrackedWithoutOriginal);
        Assert.Equal(2, h.VaultFiles);
    }

    [Fact]
    public void VaultSize_SumsOnlyVaultedFiles()
    {
        var h = Build(new[] { "a", "b" }, new[] { "a", "b" }, sizeEach: 2048);
        Assert.Equal(4096, h.VaultBytes);
    }

    [Fact]
    public void EmptyState_DoesNotMisreport()
    {
        var h = Build(Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(0, h.TrackedItems);
        Assert.Equal(0, h.TrackedWithOriginal);
        Assert.Equal(0, h.TrackedWithoutOriginal);
        Assert.Equal(0, h.VaultBytes);
    }

    [Fact]
    public void MissingVaultFile_DoesNotBreakTheSizeTotal()
    {
        // originalSize returns null for a file that vanished between listing and stat.
        var h = VaultHealth.Build(new[] { "a" }, new[] { "a" }, _ => null, _ => true);
        Assert.Equal(0, h.VaultBytes);
        Assert.Equal(1, h.TrackedWithOriginal);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")]
    [InlineData(130L * 1024 * 1024, "130 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
    public void SizeFormatting_IsReadable(long bytes, string expected)
        => Assert.Equal(expected, VaultHealth.FormatSize(bytes));
}
