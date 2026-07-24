using Jellyfin.Plugin.Overcoat.Services;
using SkiaSharp;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.Overcoat.Tests;

public sealed class OverlayRendererTests
{
    [Theory]
    [InlineData("RETURNING 21d", "RETURNING 21d")]
    [InlineData("airing 3d", "AIRING 3d")]
    [InlineData("returning 7/14", "RETURNING 7/14")]
    public void BannerDisplayText_PreservesLowercaseCountdownUnit(string input, string expected)
    {
        var method = typeof(OverlayRenderer).GetMethod("BannerDisplayText", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, new object[] { input }));
    }

    [Theory]
    [InlineData("default", 1f)]
    [InlineData("sans", 0.6f)]
    [InlineData("serif", 0.6f)]
    [InlineData("mono", 0.6f)]
    public void TypefaceScale_NormalizesSystemFontMetrics(string font, float expected)
    {
        var method = typeof(OverlayRenderer).GetMethod("TypefaceScale", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, new object?[] { font }));
    }

    [Fact]
    public void WideCardEncoding_DownscalesWithoutUpscaling()
    {
        using var large = new SKBitmap(3840, 2160);
        var bytes = OverlayRenderer.EncodeWideCardWebp(large);
        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(1920, decoded.Width);
        Assert.Equal(1080, decoded.Height);

        using var small = new SKBitmap(1280, 720);
        bytes = OverlayRenderer.EncodeWideCardWebp(small);
        using var smallDecoded = SKBitmap.Decode(bytes);
        Assert.NotNull(smallDecoded);
        Assert.Equal(1280, smallDecoded.Width);
        Assert.Equal(720, smallDecoded.Height);
    }
}
