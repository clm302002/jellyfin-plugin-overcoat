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

    /// <summary>Appearance options for the status banner (shape / fill style / position / size).</summary>
    public sealed class BannerOptions
    {
        /// <summary>Fill treatment: "solid" (filled pill, the calibrated default) or "glass" (frosted translucent chip).</summary>
        public string Style { get; set; } = "solid";

        /// <summary>Shape: "pill" (fully rounded), "square", or "drop" (flush to the edge, rounded inner corners).</summary>
        public string Shape { get; set; } = "pill";

        /// <summary>Edge the banner sits against: "top" or "bottom".</summary>
        public string Position { get; set; } = "top";

        /// <summary>Multiplier on the computed font size (1.0 = calibrated default).</summary>
        public double FontScale { get; set; } = 1.0;

        /// <summary>Optional explicit pill colour (hex); otherwise derived from the status text.</summary>
        public string? ColorOverride { get; set; }

        /// <summary>Vertical inset from the edge for pill/square shapes (fraction of poster height).</summary>
        public double OffsetFraction { get; set; } = DefaultOffsetFraction;
    }

    /// <summary>
    /// Draws the status banner (centered horizontally) using the default solid pill, near the top.
    /// Back-compat overload; mirrors the original <c>create_corner_banner</c> behaviour.
    /// </summary>
    public void DrawStatusBanner(
        SKBitmap poster,
        string text,
        string? hexColorOverride = null,
        double? positionOffsetFraction = null)
        => DrawStatusBanner(poster, text, new BannerOptions
        {
            ColorOverride = hexColorOverride,
            OffsetFraction = positionOffsetFraction ?? DefaultOffsetFraction,
        });

    /// <summary>
    /// Draws the status banner with the supplied appearance options. Mutates <paramref name="poster"/>.
    /// "solid" keeps the calibrated filled pill; "glass" frosts the poster behind a translucent tint.
    /// </summary>
    public void DrawStatusBanner(SKBitmap poster, string text, BannerOptions options)
    {
        int posterWidth = poster.Width;
        int posterHeight = poster.Height;

        float fontScale = options.FontScale <= 0 ? 1f : (float)options.FontScale;
        float dynamicFontSize = (int)(posterHeight * FontHeightFraction * SizeMultiplier * fontScale);

        var spaced = string.Join(" ", text.ToUpperInvariant().ToCharArray());
        var pillHex = options.ColorOverride ?? GetBannerColor(text);
        var (r, g, b) = HexToRgb(pillHex);

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

        bool atBottom = string.Equals(options.Position, "bottom", StringComparison.OrdinalIgnoreCase);
        bool drop = string.Equals(options.Shape, "drop", StringComparison.OrdinalIgnoreCase);
        bool square = string.Equals(options.Shape, "square", StringComparison.OrdinalIgnoreCase);
        bool glass = string.Equals(options.Style, "glass", StringComparison.OrdinalIgnoreCase);
        bool neon = string.Equals(options.Style, "neon", StringComparison.OrdinalIgnoreCase);

        float offsetY = (int)(posterHeight * options.OffsetFraction);
        float bannerX = (posterWidth - bannerWidth) / 2f;
        float bannerY = drop
            ? (atBottom ? posterHeight - bannerHeight : 0f)                 // flush to the edge
            : (atBottom ? posterHeight - bannerHeight - offsetY : offsetY); // inset pill/square

        var rect = new SKRect(bannerX, bannerY, bannerX + bannerWidth, bannerY + bannerHeight);

        // Per-corner radii (UL, UR, LR, LL).
        SKPoint[] radii;
        if (drop)
        {
            float rr = bannerHeight * 0.30f;
            radii = atBottom
                ? new[] { new SKPoint(rr, rr), new SKPoint(rr, rr), new SKPoint(0, 0), new SKPoint(0, 0) } // round the top corners
                : new[] { new SKPoint(0, 0), new SKPoint(0, 0), new SKPoint(rr, rr), new SKPoint(rr, rr) }; // round the bottom corners
        }
        else
        {
            float cr = square ? 0f : (int)(bannerHeight * CornerRadiusFraction);
            var p = new SKPoint(cr, cr);
            radii = new[] { p, p, p, p };
        }

        using var rrect = new SKRoundRect();
        rrect.SetRectRadii(rect, radii);

        float penX = bannerX + paddingX;
        float baselineY = bannerY + paddingY - inkBounds.Top;

        // For glass, snapshot the untouched poster first so the backdrop blur isn't sampling the chip.
        using var snapBmp = glass ? poster.Copy() : null;
        using var backdrop = snapBmp is null ? null : SKImage.FromBitmap(snapBmp);

        using var canvas = new SKCanvas(poster);

        if (glass && backdrop is not null)
        {
            // Frosted backdrop: blurred copy of the poster, clipped to the chip.
            canvas.Save();
            canvas.ClipRoundRect(rrect, SKClipOperation.Intersect, true);
            float sigma = Math.Max(2f, dynamicFontSize * 0.30f);
            using (var blur = SKImageFilter.CreateBlur(sigma, sigma))
            using (var blurPaint = new SKPaint { ImageFilter = blur, IsAntialias = true })
            {
                canvas.DrawImage(backdrop, 0, 0, blurPaint);
            }

            canvas.Restore();

            // Translucent colour tint over the frost.
            using (var tint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(r, g, b, 130) })
            {
                canvas.DrawRoundRect(rrect, tint);
            }

            // Subtle light border for the glass edge.
            float bw = Math.Max(1f, dynamicFontSize * 0.05f);
            var inset = rect;
            inset.Inflate(-bw / 2f, -bw / 2f);
            using (var innerR = new SKRoundRect())
            using (var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = bw, Color = new SKColor(255, 255, 255, 90) })
            {
                innerR.SetRectRadii(inset, radii);
                canvas.DrawRoundRect(innerR, border);
            }

            // Drop shadow on the text so it stays legible over a bright frosted patch.
            float sh = Math.Max(1f, bw * 0.5f);
            using (var shadow = new SKPaint { IsAntialias = true, SubpixelText = true, Typeface = _typeface, TextSize = dynamicFontSize, Color = new SKColor(0, 0, 0, 130) })
            {
                canvas.DrawText(spaced, penX + sh, baselineY + sh, shadow);
            }
        }
        else if (neon)
        {
            // Dark pill with a coloured outer glow + a crisp bright edge in the status colour.
            float bw = Math.Max(2f, dynamicFontSize * 0.06f);
            float glowSigma = Math.Max(3f, dynamicFontSize * 0.22f);
            using (var glow = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = bw,
                Color = new SKColor(r, g, b, 235),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, glowSigma),
            })
            {
                canvas.DrawRoundRect(rrect, glow); // two passes for a denser glow
                canvas.DrawRoundRect(rrect, glow);
            }

            using (var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(8, 12, 24, 235) })
            {
                canvas.DrawRoundRect(rrect, fill);
            }

            var inset = rect;
            inset.Inflate(-bw / 2f, -bw / 2f);
            using (var innerR = new SKRoundRect())
            using (var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.5f, bw * 0.5f), Color = new SKColor(r, g, b, 255) })
            {
                innerR.SetRectRadii(inset, radii);
                canvas.DrawRoundRect(innerR, border);
            }
        }
        else
        {
            using var pillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(r, g, b, PillAlpha) };
            canvas.DrawRoundRect(rrect, pillPaint);
        }

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

    /// <summary>
    /// Draws a **cropped** ribbon badge (just the ribbon graphic, not a full-poster canvas) on one
    /// side at an explicit top-Y, scaled uniformly by <c>height/1500</c> (preserves aspect — no
    /// distortion on non-2:3 posters). Returns the placed height so the caller can stack the next
    /// badge flush directly beneath it. Used by <see cref="BadgeCompositor"/> for the side ribbons.
    /// </summary>
    public int DrawRibbonBadge(SKBitmap poster, byte[] badgeBytes, bool rightSide, int topY)
    {
        using var badge = SKBitmap.Decode(badgeBytes);
        if (badge is null)
        {
            return 0;
        }

        var scale = poster.Height / (float)BadgeDesignHeight;
        int w = Math.Max(1, (int)(badge.Width * scale));
        int h = Math.Max(1, (int)(badge.Height * scale));
        using var resized = badge.Resize(new SKImageInfo(w, h), SKFilterQuality.High);
        if (resized is null)
        {
            return 0;
        }

        int x = rightSide ? poster.Width - w : 0;
        using var canvas = new SKCanvas(poster);
        canvas.DrawBitmap(resized, x, topY);
        canvas.Flush();
        return h;
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
