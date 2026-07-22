using Jellyfin.Plugin.Overcoat.Services;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.Overcoat.Tests;

public sealed class OverlayRendererTests
{
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
