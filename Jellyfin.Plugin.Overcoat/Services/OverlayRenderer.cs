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
    private readonly Dictionary<string, SKTypeface> _fontCache = new();

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

        /// <summary>Whether to draw the per-status icon beside the text.</summary>
        public bool ShowIcons { get; set; } = true;

        /// <summary>Canonical status (NEW/AIRING/RETURNING/ENDED/CANCELED) for icon selection; falls back to parsing the text.</summary>
        public string IconKey { get; set; } = string.Empty;

        /// <summary>Whether the banner spans the full poster width (a band).</summary>
        public bool FullWidth { get; set; }

        /// <summary>Horizontal alignment: "left", "center", or "right".</summary>
        public string Align { get; set; } = "center";

        /// <summary>Whether to draw a drop shadow under the banner.</summary>
        public bool Shadow { get; set; }

        /// <summary>Drop-shadow strength (0–100 → opacity).</summary>
        public int ShadowStrength { get; set; } = 60;

        /// <summary>Glass frost tint colour (hex).</summary>
        public string GlassTint { get; set; } = "#0E1018";

        /// <summary>Glass frost tint strength (0–100 → veil opacity).</summary>
        public int GlassTintStrength { get; set; } = 49;

        /// <summary>Glass frost blur amount (0–100).</summary>
        public int GlassBlur { get; set; } = 50;

        /// <summary>Neon glow intensity (0–100).</summary>
        public int NeonGlow { get; set; } = 60;

        /// <summary>Font key: "default" (embedded), "sans", "serif", or "mono".</summary>
        public string Font { get; set; } = "default";

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
        var statusColor = new SKColor(r, g, b);
        var white = new SKColor(255, 255, 255);

        bool atBottom = string.Equals(options.Position, "bottom", StringComparison.OrdinalIgnoreCase);
        bool drop = string.Equals(options.Shape, "drop", StringComparison.OrdinalIgnoreCase);
        bool square = string.Equals(options.Shape, "square", StringComparison.OrdinalIgnoreCase);
        bool glass = string.Equals(options.Style, "glass", StringComparison.OrdinalIgnoreCase);
        bool neon = string.Equals(options.Style, "neon", StringComparison.OrdinalIgnoreCase);

        // "solid" colours the fill (white text/icon); "glass"/"neon" keep a neutral panel and
        // colour the text/icon instead (the look where the background stays constant and the
        // status is shown by the text colour).
        SKColor textColor = glass ? statusColor : white;
        SKColor iconColor = (glass || neon) ? statusColor : white;

        var typeface = ResolveTypeface(options.Font);
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            SubpixelText = true,
            Typeface = typeface,
            TextSize = dynamicFontSize,
            Color = textColor,
        };

        // Tight ink bounds relative to the baseline at (0,0): Top is negative (above baseline).
        SKRect inkBounds = default;
        textPaint.MeasureText(spaced, ref inkBounds);
        float textWidth = inkBounds.Width;
        float textHeight = inkBounds.Height;

        int paddingX = (int)(dynamicFontSize * 0.5f);
        int paddingY = (int)(dynamicFontSize * 0.25f);

        // Per-status icon drawn to the left of the text. IconKey (the canonical status) wins so a
        // custom label can't break icon selection; otherwise fall back to parsing the text.
        string kw = string.IsNullOrEmpty(options.IconKey) ? StatusKeyword(text) : options.IconKey.ToUpperInvariant();
        bool hasIcon = options.ShowIcons && kw.Length > 0;
        float iconSize = textHeight * 1.08f;
        float iconGap = hasIcon ? dynamicFontSize * 0.34f : 0f;
        float iconAdvance = hasIcon ? iconSize + iconGap : 0f;
        float contentWidth = iconAdvance + textWidth;

        bool fullWidth = options.FullWidth;
        bool alignLeft = string.Equals(options.Align, "left", StringComparison.OrdinalIgnoreCase);
        bool alignRight = string.Equals(options.Align, "right", StringComparison.OrdinalIgnoreCase);

        float bannerHeight = textHeight + (paddingY * 2);
        float bannerWidth = fullWidth ? posterWidth : contentWidth + (paddingX * 2);

        float offsetY = (int)(posterHeight * options.OffsetFraction);
        float sideMargin = (int)(posterWidth * 0.03f);
        float bannerX = fullWidth
            ? 0f
            : (alignLeft ? sideMargin
                : alignRight ? posterWidth - bannerWidth - sideMargin
                : (posterWidth - bannerWidth) / 2f);
        float bannerY = drop
            ? (atBottom ? posterHeight - bannerHeight : 0f)                 // flush to the edge
            : (atBottom ? posterHeight - bannerHeight - offsetY : offsetY); // inset pill/square

        var rect = new SKRect(bannerX, bannerY, bannerX + bannerWidth, bannerY + bannerHeight);

        // Per-corner radii (UL, UR, LR, LL). A full-width band is square on the sides (flush to the
        // poster edges); otherwise pill / square / drop as chosen.
        SKPoint[] radii;
        if (fullWidth)
        {
            var z = new SKPoint(0, 0);
            radii = new[] { z, z, z, z };
        }
        else if (drop)
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

        // Content placement: a full-width band positions the icon+text per alignment; an auto-width
        // banner hugs its content (paddingX each side).
        float contentLeft = fullWidth
            ? (alignLeft ? bannerX + paddingX
                : alignRight ? bannerX + bannerWidth - paddingX - contentWidth
                : bannerX + ((bannerWidth - contentWidth) / 2f))
            : bannerX + paddingX;

        float penX = contentLeft + iconAdvance;
        float baselineY = bannerY + paddingY - inkBounds.Top;
        float iconCx = contentLeft + (iconSize / 2f);
        float iconCy = bannerY + (bannerHeight / 2f);

        bool flushTop = drop && !atBottom;
        bool flushBottom = drop && atBottom;

        // For glass, snapshot the untouched poster first so the backdrop blur isn't sampling the chip.
        using var snapBmp = glass ? poster.Copy() : null;
        using var backdrop = snapBmp is null ? null : SKImage.FromBitmap(snapBmp);

        using var canvas = new SKCanvas(poster);

        // Drop shadow under the whole banner (drawn first so the fill/frost sits on top).
        if (options.Shadow)
        {
            float so = dynamicFontSize * 0.10f;
            float ss = Math.Max(2f, dynamicFontSize * 0.18f);
            byte sa = (byte)(Math.Clamp(options.ShadowStrength, 0, 100) / 100.0 * 190);
            var shadowRect = new SKRect(rect.Left, rect.Top + so, rect.Right, rect.Bottom + so);
            using var shadowRR = new SKRoundRect();
            shadowRR.SetRectRadii(shadowRect, radii);
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(0, 0, 0, sa),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, ss),
            };
            canvas.DrawRoundRect(shadowRR, shadowPaint);
        }

        if (glass && backdrop is not null)
        {
            // Frosted backdrop: a blurred copy of the poster, clipped to the chip.
            canvas.Save();
            canvas.ClipRoundRect(rrect, SKClipOperation.Intersect, true);
            float sigma = Math.Max(2f, dynamicFontSize * (0.2f + (0.7f * (Math.Clamp(options.GlassBlur, 0, 100) / 100f))));
            using (var blur = SKImageFilter.CreateBlur(sigma, sigma))
            using (var blurPaint = new SKPaint { ImageFilter = blur, IsAntialias = true })
            {
                canvas.DrawImage(backdrop, 0, 0, blurPaint);
            }

            // Glass tint veil (configurable colour + strength — the status shows via the text, not the fill).
            var (tr, tg, tb) = HexToRgb(string.IsNullOrWhiteSpace(options.GlassTint) ? "#0E1018" : options.GlassTint);
            byte va = (byte)(Math.Clamp(options.GlassTintStrength, 0, 100) / 100.0 * 235);
            using (var veil = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(tr, tg, tb, va) })
            {
                canvas.DrawRect(rect, veil);
            }

            // Soft top sheen (no hard border) — skip when the chip is flush to the top edge.
            if (!flushTop)
            {
                using var sheen = SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Top),
                    new SKPoint(rect.Left, rect.Top + (bannerHeight * 0.55f)),
                    new[] { new SKColor(255, 255, 255, 60), new SKColor(255, 255, 255, 0) },
                    null,
                    SKShaderTileMode.Clamp);
                using var sheenPaint = new SKPaint { IsAntialias = true, Shader = sheen };
                canvas.DrawRect(rect, sheenPaint);
            }

            canvas.Restore();
        }
        else if (neon)
        {
            // Dark pill with a coloured outer glow + a crisp bright edge in the status colour.
            // Glow size, opacity, and pass count scale with the configured intensity.
            float gIntensity = Math.Clamp(options.NeonGlow, 0, 100) / 100f;
            float bw = Math.Max(2f, dynamicFontSize * 0.06f);
            float glowSigma = Math.Max(3f, dynamicFontSize * (0.12f + (0.30f * gIntensity)));
            byte glowAlpha = (byte)(150 + (90 * gIntensity));
            int glowPasses = gIntensity > 0.66f ? 3 : (gIntensity > 0.02f ? 2 : 1);
            using (var glow = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = bw,
                Color = new SKColor(r, g, b, glowAlpha),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, glowSigma),
            })
            {
                for (int p = 0; p < glowPasses; p++)
                {
                    canvas.DrawRoundRect(rrect, glow);
                }
            }

            using (var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(8, 12, 24, 235) })
            {
                canvas.DrawRoundRect(rrect, fill);
            }

            // Crisp bright edge — but not along a flush edge (keeps "drop" feeling like a true drop).
            var inset = rect;
            inset.Inflate(-bw / 2f, -bw / 2f);
            using (var innerR = new SKRoundRect())
            using (var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.5f, bw * 0.5f), Color = statusColor })
            {
                innerR.SetRectRadii(inset, radii);
                canvas.Save();
                if (flushTop)
                {
                    canvas.ClipRect(new SKRect(0, rect.Top + bw, posterWidth, posterHeight));
                }
                else if (flushBottom)
                {
                    canvas.ClipRect(new SKRect(0, 0, posterWidth, rect.Bottom - bw));
                }

                canvas.DrawRoundRect(innerR, border);
                canvas.Restore();
            }
        }
        else
        {
            using var pillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(r, g, b, PillAlpha) };
            canvas.DrawRoundRect(rrect, pillPaint);
        }

        if (hasIcon)
        {
            DrawStatusIcon(canvas, kw, iconCx, iconCy, iconSize, iconColor);
        }

        canvas.DrawText(spaced, penX, baselineY, textPaint);
        canvas.Flush();
    }

    /// <summary>Maps banner text to a status keyword used to pick the icon (empty = no icon).</summary>
    private static string StatusKeyword(string text)
    {
        var upper = text.ToUpperInvariant();
        foreach (var k in new[] { "RETURNING", "CANCELED", "AIRING", "ENDED", "NEW" })
        {
            if (upper.Contains(k, StringComparison.Ordinal))
            {
                return k;
            }
        }

        return string.Empty;
    }

    /// <summary>Draws a small vector status icon centred at (cx, cy) within a box of side <paramref name="size"/>.</summary>
    private static void DrawStatusIcon(SKCanvas canvas, string keyword, float cx, float cy, float size, SKColor color)
    {
        float r = size / 2f;
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.5f, size * 0.13f),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = color,
        };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };

        switch (keyword)
        {
            case "NEW": // five-point star
            {
                using var path = new SKPath();
                float inner = r * 0.42f;
                for (int i = 0; i < 10; i++)
                {
                    double ang = (-Math.PI / 2) + (i * Math.PI / 5);
                    float rad = (i % 2 == 0) ? r : inner;
                    float x = cx + (rad * (float)Math.Cos(ang));
                    float y = cy + (rad * (float)Math.Sin(ang));
                    if (i == 0)
                    {
                        path.MoveTo(x, y);
                    }
                    else
                    {
                        path.LineTo(x, y);
                    }
                }

                path.Close();
                canvas.DrawPath(path, fill);
                break;
            }

            case "ENDED": // checkmark
            {
                using var path = new SKPath();
                path.MoveTo(cx - (r * 0.6f), cy + (r * 0.05f));
                path.LineTo(cx - (r * 0.15f), cy + (r * 0.5f));
                path.LineTo(cx + (r * 0.65f), cy - (r * 0.5f));
                canvas.DrawPath(path, stroke);
                break;
            }

            case "CANCELED": // cross
            {
                float d = r * 0.78f;
                canvas.DrawLine(cx - d, cy - d, cx + d, cy + d, stroke);
                canvas.DrawLine(cx - d, cy + d, cx + d, cy - d, stroke);
                break;
            }

            case "AIRING": // live dot + broadcast waves
            {
                canvas.DrawCircle(cx, cy, r * 0.32f, fill);
                for (int i = 1; i <= 2; i++)
                {
                    float rr = (r * 0.32f) + (i * r * 0.34f);
                    var oval = new SKRect(cx - rr, cy - rr, cx + rr, cy + rr);
                    using var wave = new SKPath();
                    wave.AddArc(oval, -45, 90);
                    canvas.DrawPath(wave, stroke);
                }

                break;
            }

            case "RETURNING": // circular arrow
            {
                var oval = new SKRect(cx - r, cy - r, cx + r, cy + r);
                using var arc = new SKPath();
                arc.AddArc(oval, -50, 300);
                canvas.DrawPath(arc, stroke);

                // Arrowhead (filled triangle) at the arc's end.
                double a = (-50 + 300) * Math.PI / 180.0;
                float ex = cx + (r * (float)Math.Cos(a));
                float ey = cy + (r * (float)Math.Sin(a));
                float tx = -(float)Math.Sin(a);
                float ty = (float)Math.Cos(a);
                float nx = (float)Math.Cos(a);
                float ny = (float)Math.Sin(a);
                float s = r * 0.62f;
                using var head = new SKPath();
                head.MoveTo(ex + (tx * s), ey + (ty * s));
                head.LineTo(ex + (nx * s * 0.7f), ey + (ny * s * 0.7f));
                head.LineTo(ex - (nx * s * 0.7f), ey - (ny * s * 0.7f));
                head.Close();
                canvas.DrawPath(head, fill);
                break;
            }
        }
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
    public int DrawRibbonBadge(SKBitmap poster, byte[] badgeBytes, bool rightSide, int topY, float scaleMultiplier = 1f)
    {
        using var badge = SKBitmap.Decode(badgeBytes);
        if (badge is null)
        {
            return 0;
        }

        var scale = (poster.Height / (float)BadgeDesignHeight) * scaleMultiplier;
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

    /// <summary>Computes the placed height of a ribbon badge (for stack layout) without drawing it.</summary>
    public int MeasureRibbonHeight(byte[] badgeBytes, int posterHeight, float scaleMultiplier = 1f)
    {
        using var badge = SKBitmap.Decode(badgeBytes);
        if (badge is null)
        {
            return 0;
        }

        var scale = (posterHeight / (float)BadgeDesignHeight) * scaleMultiplier;
        return Math.Max(1, (int)(badge.Height * scale));
    }

    private static (byte R, byte G, byte B) HexToRgb(string hex)
    {
        var h = hex.TrimStart('#');
        return (
            byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Resolves the banner typeface for a font key. "default" is the embedded display font; the others
    /// resolve from the host's font manager (glyphs depend on the fonts installed on the server, but
    /// Jellyfin's runtime ships a sans/serif/mono set). Cached so we don't recreate per call.
    /// </summary>
    private SKTypeface ResolveTypeface(string? font)
    {
        if (string.IsNullOrEmpty(font) || string.Equals(font, "default", StringComparison.OrdinalIgnoreCase))
        {
            return _typeface;
        }

        if (_fontCache.TryGetValue(font, out var cached))
        {
            return cached ?? _typeface;
        }

        var created = font.ToLowerInvariant() switch
        {
            "sans" => SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold),
            "serif" => SKTypeface.FromFamilyName("serif", SKFontStyle.Bold),
            "mono" => SKTypeface.FromFamilyName("monospace", SKFontStyle.Bold),
            _ => null,
        };
        _fontCache[font] = created!;
        return created ?? _typeface;
    }

    public void Dispose()
    {
        foreach (var tf in _fontCache.Values)
        {
            tf?.Dispose();
        }

        _typeface.Dispose();
    }
}
