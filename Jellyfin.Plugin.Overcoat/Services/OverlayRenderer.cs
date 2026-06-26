using System.Globalization;
using System.Reflection;
using SkiaSharp;

namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// Image rendering, ported 1:1 from the Python reference's <c>OverlayProcessor</c> (PIL → SkiaSharp).
///
/// Every calibration constant is preserved verbatim so output matches the script:
/// font size = 5.3% of poster height × 1.105 size-multiplier (1.3 × 0.85), pill fill alpha 220
/// (86%), white text, letters spaced with a single space ("A I R I N G"), corner radius = 56% of
/// pill height, vertical offset 1.5% of poster height, badge stack offset scaled by height/1500,
/// mid-left/right badges at 20% from top, and full-canvas badges resized to the poster's own size.
///
/// Methods mutate the supplied <see cref="SKBitmap"/> in place so the pipeline can chain a banner
/// and several badges, then encode once.
/// </summary>
public sealed class OverlayRenderer : IDisposable
{
    /// <summary>Status → pill colour. Mirrors <c>OverlayProcessor.BANNER_COLORS</c>.</summary>
    public static readonly IReadOnlyDictionary<string, string> BannerColors = new Dictionary<string, string>
    {
        ["NEW"] = "#5EBD3E",       // green
        ["AIRING"] = "#149BDA",    // blue
        ["RETURNING"] = "#A020F0", // purple
        ["ENDED"] = "#424242",     // dark gray
        ["CANCELED"] = "#D32F2F",  // red
        ["DEFAULT"] = "#262626",   // default dark gray
    };

    // size_multiplier = 1.3 (test) * 0.85 (consistency reduction). Kept exactly as the script.
    private const float SizeMultiplier = 1.3f * 0.85f;
    private const float FontHeightFraction = 0.053f; // 5.3% of poster height
    private const byte PillAlpha = 220;              // 86% opacity
    private const float CornerRadiusFraction = 0.56f;
    private const float DefaultOffsetFraction = 0.015f; // pill 1.5% from top
    private const int BadgeDesignHeight = 1500;      // badges authored for a 1500px-tall poster

    private const string FontResource =
        "Jellyfin.Plugin.Overcoat.Resources.Fonts.Juventus-Fans-Bold.ttf";

    private readonly SKTypeface _typeface;

    public OverlayRenderer()
    {
        using var stream = typeof(OverlayRenderer).Assembly.GetManifestResourceStream(FontResource)
            ?? throw new InvalidOperationException($"Embedded font not found: {FontResource}");
        // SKTypeface.FromStream consumes the stream; copy to a seekable buffer first.
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        _typeface = SKTypeface.FromStream(ms)
            ?? throw new InvalidOperationException("Failed to load embedded font into SkiaSharp.");
    }

    /// <summary>Reads an embedded resource (e.g. a badge PNG) as bytes.</summary>
    public static byte[] ReadEmbeddedResource(string logicalName)
    {
        using var stream = typeof(OverlayRenderer).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {logicalName}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Decodes poster bytes into a mutable bitmap. Returns null on undecodable input.</summary>
    public static SKBitmap? Decode(byte[] imageBytes) => SKBitmap.Decode(imageBytes);

    /// <summary>Encodes the finished poster as PNG (crisp text, like the script's PNG output).</summary>
    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Picks the pill colour for a status string. Mirrors <c>get_banner_color</c>: first
    /// keyword found in the (upper-cased) text wins; falls back to DEFAULT.
    /// </summary>
    public static string GetBannerColor(string text)
    {
        var upper = text.ToUpperInvariant();
        foreach (var (status, color) in BannerColors)
        {
            if (status != "DEFAULT" && upper.Contains(status, StringComparison.Ordinal))
            {
                return color;
            }
        }

        return BannerColors["DEFAULT"];
    }

    /// <summary>
    /// Draws the Jellyfin-style status pill (centered near the top). Mutates <paramref name="poster"/>.
    /// Port of <c>create_corner_banner</c> with position "top-left" (which the script centers).
    /// </summary>
    /// <param name="poster">The poster bitmap (drawn on in place).</param>
    /// <param name="text">Status text, e.g. "AIRING" (upper-cased + letter-spaced internally).</param>
    /// <param name="hexColorOverride">Optional explicit pill colour; otherwise derived from text.</param>
    /// <param name="positionOffsetFraction">Optional vertical offset fraction (default 0.015).</param>
    public void DrawStatusBanner(
        SKBitmap poster,
        string text,
        string? hexColorOverride = null,
        double? positionOffsetFraction = null)
    {
        int posterWidth = poster.Width;
        int posterHeight = poster.Height;

        float dynamicFontSize = (int)(posterHeight * FontHeightFraction * SizeMultiplier);

        var spaced = string.Join(" ", text.ToUpperInvariant().ToCharArray());
        var pillHex = hexColorOverride ?? GetBannerColor(text);
        var (r, g, b) = HexToRgb(pillHex);

        using var pillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(r, g, b, PillAlpha),
        };

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            SubpixelText = true,
            Typeface = _typeface,
            TextSize = dynamicFontSize,
            Color = new SKColor(255, 255, 255, 255),
        };

        // Tight ink bounds relative to the baseline at (0,0): Top is negative (above baseline).
        SKRect inkBounds = default;
        textPaint.MeasureText(spaced, ref inkBounds);
        float textWidth = inkBounds.Width;
        float textHeight = inkBounds.Height;

        int paddingX = (int)(dynamicFontSize * 0.5f);
        int paddingY = (int)(dynamicFontSize * 0.25f);

        float bannerWidth = textWidth + (paddingX * 2);
        float bannerHeight = textHeight + (paddingY * 2);

        double offsetFraction = positionOffsetFraction ?? DefaultOffsetFraction;
        float offsetY = (int)(posterHeight * offsetFraction);

        // "top-left" in the script centers horizontally.
        float bannerX = (posterWidth - bannerWidth) / 2f;
        float bannerY = offsetY;

        // Pen position: x = banner left + left padding; baseline placed so ink top sits at
        // bannerY + paddingY (inkBounds.Top is negative, so subtracting it moves down).
        float penX = bannerX + paddingX;
        float baselineY = bannerY + paddingY - inkBounds.Top;

        float cornerRadius = (int)(bannerHeight * CornerRadiusFraction);

        var pillRect = new SKRect(bannerX, bannerY, bannerX + bannerWidth, bannerY + bannerHeight);

        using var canvas = new SKCanvas(poster);
        canvas.DrawRoundRect(pillRect, cornerRadius, cornerRadius, pillPaint);
        canvas.DrawText(spaced, penX, baselineY, textPaint);
        canvas.Flush();
    }

    /// <summary>
    /// Composites a badge PNG onto the poster. Mutates <paramref name="poster"/>.
    /// Port of <c>add_badge_overlay</c>, including the full-canvas resize-to-poster rules.
    /// </summary>
    /// <param name="poster">The poster bitmap (drawn on in place).</param>
    /// <param name="badgeBytes">The badge PNG bytes.</param>
    /// <param name="position">top-left, top-right, mid-left, mid-right, bottom-left, bottom-right.</param>
    /// <param name="stackOffset">Vertical stacking offset (px @1500h; scaled to this poster).</param>
    /// <param name="fullOverlay">True for a full poster-sized canvas (e.g. IMDB ribbon).</param>
    public void DrawBadge(
        SKBitmap poster,
        byte[] badgeBytes,
        string position,
        int stackOffset,
        bool fullOverlay)
    {
        using var badge = SKBitmap.Decode(badgeBytes);
        if (badge is null)
        {
            return;
        }

        int posterWidth = poster.Width;
        int posterHeight = poster.Height;

        using var canvas = new SKCanvas(poster);

        // Full-canvas overlay (corner ribbon baked into a poster-sized frame): resize to the
        // exact poster size and paste at (0,0). Ignores position/stack_offset.
        if (fullOverlay)
        {
            using var full = badge.Resize(new SKImageInfo(posterWidth, posterHeight), SKFilterQuality.High);
            if (full is not null)
            {
                canvas.DrawBitmap(full, 0, 0);
                canvas.Flush();
            }

            return;
        }

        float scaleFactor = posterHeight / (float)BadgeDesignHeight;

        // Full-canvas edge ribbons (*Left.png) bake placement into a ~1000×1500 canvas, so they
        // resize to the poster's OWN width AND height (uniform height-scaling would clip them on
        // non-2:3 posters). Small icons keep proportional height-scaling.
        int newBadgeWidth;
        int newBadgeHeight;
        if (badge.Width >= 800 && badge.Height >= 1200)
        {
            newBadgeWidth = posterWidth;
            newBadgeHeight = posterHeight;
        }
        else
        {
            newBadgeWidth = (int)(badge.Width * scaleFactor);
            newBadgeHeight = (int)(badge.Height * scaleFactor);
        }

        using var resized = badge.Resize(new SKImageInfo(newBadgeWidth, newBadgeHeight), SKFilterQuality.High);
        if (resized is null)
        {
            return;
        }

        const int margin = 0;
        int scaledStack = (int)(stackOffset * scaleFactor);
        int x;
        int y;
        switch (position)
        {
            case "top-right":
                x = posterWidth - newBadgeWidth - margin;
                y = margin + scaledStack;
                break;
            case "mid-left":
                x = margin;
                y = (int)(posterHeight * 0.20) + scaledStack;
                break;
            case "mid-right":
                x = posterWidth - newBadgeWidth - margin;
                y = (int)(posterHeight * 0.20) + scaledStack;
                break;
            case "bottom-left":
                x = margin;
                y = posterHeight - newBadgeHeight - margin - scaledStack;
                break;
            case "bottom-right":
                x = posterWidth - newBadgeWidth - margin;
                y = posterHeight - newBadgeHeight - margin - scaledStack;
                break;
            case "top-left":
            default:
                x = margin;
                y = margin + scaledStack;
                break;
        }

        canvas.DrawBitmap(resized, x, y);
        canvas.Flush();
    }

    private static (byte R, byte G, byte B) HexToRgb(string hex)
    {
        var h = hex.TrimStart('#');
        return (
            byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public void Dispose() => _typeface.Dispose();
}
