using Jellyfin.Plugin.Overcoat.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Overcoat.Tests;

/// <summary>
/// Regression tests for the skip cache and originals vault.
///
/// Every overlay-loss incident in this project so far has lived in this state machine, and each of
/// the cases below reproduces a bug that actually shipped. They are written as "the situation that
/// went wrong", not as coverage for its own sake — if one fails, a real poster is at risk.
/// </summary>
public sealed class ProcessingStateTests : IDisposable
{
    private readonly string _dir;

    public ProcessingStateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "overcoat-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ProcessingState New() => new(_dir, NullLogger.Instance);

    private static void Mark(ProcessingState s, string id, string? status = "Returning Series",
        string? text = "RETURNING", string[]? badges = null, long sig = 1000, string appearance = "a", string hash = "HASH")
        => s.MarkProcessed(id, "Test", status, text, badges ?? Array.Empty<string>(), sig, appearance, hash);

    // --- skip cache ---

    [Fact]
    public void NewItem_NeedsProcessing()
    {
        var s = New();
        Assert.True(s.NeedsProcessing("id1", "x", Array.Empty<string>(), "T", 1, "a", cacheEnabled: true));
    }

    [Fact]
    public void UnchangedItem_IsSkipped()
    {
        var s = New();
        Mark(s, "id1");
        Assert.False(s.NeedsProcessing("id1", "Returning Series", Array.Empty<string>(), "RETURNING", 1000, "a", true));
    }

    [Fact]
    public void DisablingCache_ForcesProcessing()
    {
        var s = New();
        Mark(s, "id1");
        Assert.True(s.NeedsProcessing("id1", "Returning Series", Array.Empty<string>(), "RETURNING", 1000, "a", cacheEnabled: false));
    }

    [Theory]
    // status, banner text, badge set and appearance each independently force a re-render.
    [InlineData("Ended", "RETURNING", "a")]
    [InlineData("Returning Series", "ENDED", "a")]
    [InlineData("Returning Series", "RETURNING", "different-appearance")]
    public void AnyMeaningfulChange_ForcesProcessing(string status, string text, string appearance)
    {
        var s = New();
        Mark(s, "id1");
        Assert.True(s.NeedsProcessing("id1", status, Array.Empty<string>(), text, 1000, appearance, true));
    }

    [Fact]
    public void BadgeSetChange_ForcesProcessing()
    {
        var s = New();
        Mark(s, "id1", badges: new[] { "watch_history" });
        Assert.True(s.NeedsProcessing("id1", "Returning Series", new[] { "tmdb_trending" }, "RETURNING", 1000, "a", true));
    }

    // --- A-14: a poster that vanished must not read as "unchanged" ---

    [Fact]
    public void SignatureChanged_IsFalse_WhenCurrentSignatureIsZero()
    {
        // Jellyfin reports 0 when there is no primary image. The comparison deliberately cannot
        // distinguish that case, which is exactly why OverlayTask has to detect it separately —
        // this test pins the behaviour so the caller-side guard is never assumed redundant.
        var s = New();
        Mark(s, "id1", sig: 1000);
        Assert.False(s.SignatureChanged("id1", 0));
        Assert.Equal(1000, s.CachedSignature("id1"));
    }

    [Fact]
    public void SignatureChanged_IsTrue_OnDifferentNonZeroSignature()
    {
        var s = New();
        Mark(s, "id1", sig: 1000);
        Assert.True(s.SignatureChanged("id1", 2000));
    }

    // --- v0.6.1 regression: hashes must backfill on upgrade ---

    [Fact]
    public void HashBackfill_IsNeeded_ForPre061Entry_WithMatchingSignature()
    {
        var s = New();
        Mark(s, "id1", sig: 1000, hash: string.Empty); // written by <= 0.6.0
        Assert.True(s.NeedsHashBackfill("id1", 1000));

        s.SetProducedHash("id1", "ABC");
        Assert.False(s.NeedsHashBackfill("id1", 1000));
        Assert.Equal("ABC", s.ProducedHashFor("id1"));
    }

    [Fact]
    public void HashBackfill_IsNotOffered_WhenTheFileHasMoved()
    {
        // The signature must still match, since that is the only proof the bytes are still ours.
        var s = New();
        Mark(s, "id1", sig: 1000, hash: string.Empty);
        Assert.False(s.NeedsHashBackfill("id1", 9999));
    }

    [Fact]
    public void RefreshSignature_AdoptsNewMtime_SoTheCheapCheckPassesNextRun()
    {
        var s = New();
        Mark(s, "id1", sig: 1000);
        s.RefreshSignature("id1", 2000);
        Assert.False(s.SignatureChanged("id1", 2000));
    }

    // --- A-13: a failed badge source must not read as "badge lost" ---

    [Fact]
    public void CachedBadgeSet_ReturnsWhatWasLastRendered()
    {
        var s = New();
        Mark(s, "id1", badges: new[] { "watch_history", "tmdb_trending" });
        Assert.Equal(
            new[] { "tmdb_trending", "watch_history" },
            s.CachedBadgeSet("id1").OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void CachedBadgeSet_IsEmpty_ForUnknownItem() => Assert.Empty(New().CachedBadgeSet("nope"));

    // --- A-20: writes are atomic and survivable ---

    [Fact]
    public void State_RoundTripsThroughDisk()
    {
        var s = New();
        Mark(s, "id1", badges: new[] { "watch_history" }, hash: "H1");
        s.Flush();

        var reloaded = New();
        Assert.False(reloaded.NeedsProcessing("id1", "Returning Series", new[] { "watch_history" }, "RETURNING", 1000, "a", true));
        Assert.Equal("H1", reloaded.ProducedHashFor("id1"));
    }

    [Fact]
    public void CorruptStateFile_RecoversFromBackup_RatherThanStartingEmpty()
    {
        var s = New();
        Mark(s, "id1", hash: "H1");
        s.Flush();

        // Second flush moves the first generation to .bak.
        Mark(s, "id2", hash: "H2");
        s.Flush();
        Assert.True(File.Exists(Path.Combine(_dir, "state.json.bak")));

        File.WriteAllText(Path.Combine(_dir, "state.json"), "{ this is not json");

        // Starting empty would make every item look new AND lose track of what we had overlaid.
        var recovered = New();
        Assert.Equal("H1", recovered.ProducedHashFor("id1"));
    }

    [Fact]
    public async Task VaultWrite_LeavesNoTempFileBehind()
    {
        var s = New();
        await s.SaveOriginalAsync("id1", new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.True(s.HasOriginal("id1"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "originals"), "*.tmp"));
        Assert.Equal(new byte[] { 1, 2, 3 }, await s.ReadOriginalAsync("id1", CancellationToken.None));
    }

    [Fact]
    public async Task VaultWrite_OverwritesAtomically()
    {
        var s = New();
        await s.SaveOriginalAsync("id1", new byte[] { 1 }, CancellationToken.None);
        await s.SaveOriginalAsync("id1", new byte[] { 2, 2 }, CancellationToken.None);
        Assert.Equal(new byte[] { 2, 2 }, await s.ReadOriginalAsync("id1", CancellationToken.None));
    }

    [Fact]
    public async Task ThumbChannel_UsesIndependentStateAndVault()
    {
        var primary = New();
        var thumb = new ProcessingState(_dir, NullLogger.Instance, ProcessingState.ArtworkChannel.Thumb);
        Mark(primary, "same", hash: "POSTER");
        Mark(thumb, "same", hash: "THUMB");
        await primary.SaveOriginalAsync("same", new byte[] { 1 }, CancellationToken.None);
        await thumb.SaveOriginalAsync("same", new byte[] { 2 }, CancellationToken.None);
        primary.Flush();
        thumb.Flush();
        Assert.Equal("POSTER", new ProcessingState(_dir, NullLogger.Instance).ProducedHashFor("same"));
        Assert.Equal("THUMB", new ProcessingState(_dir, NullLogger.Instance, ProcessingState.ArtworkChannel.Thumb).ProducedHashFor("same"));
        Assert.True(File.Exists(Path.Combine(_dir, "originals", "same.png")));
        Assert.True(File.Exists(Path.Combine(_dir, "thumb-originals", "same.img")));
    }

    // --- A-21: vault contents are not necessarily PNG ---

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    public void DetectMimeType_IdentifiesRealFormat(byte[] header, string expected)
        => Assert.Equal(expected, ProcessingState.DetectMimeType(header));

    [Fact]
    public void DetectMimeType_IdentifiesWebp()
    {
        // The most common format in a real vault, despite every file being named .png.
        var webp = new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 };
        Assert.Equal("image/webp", ProcessingState.DetectMimeType(webp));
    }

    [Fact]
    public void DetectMimeType_FallsBackToPng_OnUnknownBytes()
        => Assert.Equal("image/png", ProcessingState.DetectMimeType(new byte[] { 1, 2, 3, 4 }));

    // --- hashing ---

    [Fact]
    public void HashBytes_IsStableAndDistinguishing()
    {
        Assert.Equal(ProcessingState.HashBytes(new byte[] { 1, 2, 3 }), ProcessingState.HashBytes(new byte[] { 1, 2, 3 }));
        Assert.NotEqual(ProcessingState.HashBytes(new byte[] { 1, 2, 3 }), ProcessingState.HashBytes(new byte[] { 1, 2, 4 }));
    }

    [Fact]
    public void Remove_DropsBothTheEntryAndTheVaultedOriginal()
    {
        var s = New();
        Mark(s, "id1");
        File.WriteAllBytes(Path.Combine(_dir, "originals", "id1.png"), new byte[] { 1 });

        s.Remove("id1");

        Assert.False(s.HasOriginal("id1"));
        Assert.True(s.NeedsProcessing("id1", "Returning Series", Array.Empty<string>(), "RETURNING", 1000, "a", true));
    }
}
