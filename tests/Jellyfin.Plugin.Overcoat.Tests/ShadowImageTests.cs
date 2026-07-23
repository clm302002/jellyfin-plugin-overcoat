using Jellyfin.Plugin.Overcoat.ScheduledTasks;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Overcoat.Tests;

/// <summary>
/// Guards the decision behind deleting a stale original image. Overcoat saves overlays as .png/.webp;
/// when the item's existing image was a different extension, SaveImage writes a new file beside the
/// old one and Jellyfin keeps serving the un-overlaid original, so the overlay is invisible. The fix
/// deletes that shadow — and because a wrong "delete" removes a user's file, every guard is pinned.
/// </summary>
public sealed class ShadowImageTests
{
    private const string Dir = "/config/metadata/library/04/04425c19";

    [Fact]
    public void JpgShadowingPng_IsRemoved()
        => Assert.True(OverlayTask.IsShadowToRemove($"{Dir}/poster.jpg", $"{Dir}/poster.png", ImageType.Primary));

    [Fact]
    public void LandscapeJpgShadowingWebp_IsRemoved()
        => Assert.True(OverlayTask.IsShadowToRemove($"{Dir}/landscape.jpg", $"{Dir}/landscape.webp", ImageType.Thumb));

    [Fact]
    public void SamePathOverwrite_IsLeftAlone()
        => Assert.False(OverlayTask.IsShadowToRemove($"{Dir}/poster.png", $"{Dir}/poster.png", ImageType.Primary));

    [Theory]
    [InlineData(null, "/x/poster.png")]
    [InlineData("/x/poster.jpg", null)]
    [InlineData("", "/x/poster.png")]
    public void MissingEitherPath_IsLeftAlone(string? prev, string? now)
        => Assert.False(OverlayTask.IsShadowToRemove(prev, now, ImageType.Primary));

    [Fact]
    public void DifferentDirectory_IsNeverTouched()
    {
        // The new image is in this item's folder; the "previous" path points somewhere else entirely.
        // Must never delete across directories, whatever the filename.
        Assert.False(OverlayTask.IsShadowToRemove("/data/movies/The Godfather/poster.jpg", $"{Dir}/poster.png", ImageType.Primary));
    }

    [Theory]
    [InlineData("backdrop.jpg")]
    [InlineData("logo.png")]
    [InlineData("banner.jpg")]
    [InlineData("fanart.jpg")]
    public void NonPosterSibling_IsNeverDeleted(string other)
    {
        // Only the poster/landscape original may be removed. A stray backdrop/logo in the same folder
        // is not ours to touch, even though it shares the directory.
        Assert.False(OverlayTask.IsShadowToRemove($"{Dir}/{other}", $"{Dir}/poster.png", ImageType.Primary));
    }

    [Fact]
    public void ThumbChannelDoesNotDeleteAPosterOriginal()
    {
        // On the Thumb channel the expected original is "landscape"; a "poster" original is off-limits.
        Assert.False(OverlayTask.IsShadowToRemove($"{Dir}/poster.jpg", $"{Dir}/landscape.webp", ImageType.Thumb));
    }
}
