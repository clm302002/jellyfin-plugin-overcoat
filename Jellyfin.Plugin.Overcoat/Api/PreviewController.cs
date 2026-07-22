using Jellyfin.Plugin.Overcoat.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.Overcoat.Api;

/// <summary>
/// Live banner preview for the dashboard settings page. Renders a synthetic sample poster with the
/// banner drawn per the supplied options, so the page can show what a setting looks like instantly —
/// without running the scheduled task. Reuses the real <see cref="OverlayRenderer"/> so the preview
/// matches production output exactly.
/// </summary>
[ApiController]
// These endpoints exist solely to drive the admin settings page, and they render images from
// caller-supplied parameters. Plain [Authorize] let any signed-in user reach them; matching
// Jellyfin's own PluginsController and requiring elevation keeps them to administrators. (A-28)
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Overcoat")]
[Produces("image/png")]
public class PreviewController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PreviewController> _logger;

    public PreviewController(ILibraryManager libraryManager, ILogger<PreviewController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// The poster the preview draws on: the built-in placeholder, or a random clean one from the
    /// library. Falls back to the placeholder whenever no clean poster can be found, so the preview
    /// always renders something rather than erroring on a settings page.
    /// </summary>
    private async Task<SKBitmap> ResolveCanvasAsync(string? source, CancellationToken ct)
    {
        if (!string.Equals(source, "random", StringComparison.OrdinalIgnoreCase))
        {
            return BuildSamplePoster();
        }

        var picker = new PreviewPosterSource(_libraryManager, _logger);
        return await picker.TryGetRandomAsync(ct).ConfigureAwait(false) ?? BuildSamplePoster();
    }

    /// <summary>Renders the sample poster + banner as a PNG.</summary>
    /// <param name="style">solid | glass | neon.</param>
    /// <param name="shape">pill | square | drop.</param>
    /// <param name="position">top | bottom.</param>
    /// <param name="fontScale">Font-size multiplier.</param>
    /// <param name="status">Banner text to render (defaults to a RETURNING sample with a date).</param>
    [HttpGet("BannerPreview")]
    public async Task<ActionResult> GetBannerPreview(
        [FromQuery] string? style,
        [FromQuery] string? shape,
        [FromQuery] string? position,
        [FromQuery] double fontScale = 1.0,
        [FromQuery] string status = "RETURNING 6/26",
        [FromQuery] bool icons = true,
        [FromQuery] string? color = null,
        [FromQuery] string? iconKey = null,
        [FromQuery] bool fullWidth = false,
        [FromQuery] string? align = null,
        [FromQuery] bool shadow = false,
        [FromQuery] int shadowStrength = 60,
        [FromQuery] string? glassTint = null,
        [FromQuery] int glassTintStrength = 49,
        [FromQuery] int glassBlur = 50,
        [FromQuery] int neonGlow = 60,
        [FromQuery] string? font = null,
        [FromQuery] string? source = null,
        CancellationToken cancellationToken = default)
    {
        using var bmp = await ResolveCanvasAsync(source, cancellationToken).ConfigureAwait(false);
        using var renderer = new OverlayRenderer();
        renderer.DrawStatusBanner(bmp, status, new OverlayRenderer.BannerOptions
        {
            Style = string.IsNullOrWhiteSpace(style) ? "solid" : style,
            Shape = string.IsNullOrWhiteSpace(shape) ? "pill" : shape,
            Position = string.IsNullOrWhiteSpace(position) ? "top" : position,
            FontScale = fontScale <= 0 ? 1.0 : fontScale,
            ShowIcons = icons,
            IconKey = iconKey ?? string.Empty,
            ColorOverride = string.IsNullOrWhiteSpace(color) ? null : color,
            FullWidth = fullWidth,
            Align = string.IsNullOrWhiteSpace(align) ? "center" : align,
            Shadow = shadow,
            ShadowStrength = shadowStrength,
            GlassTint = string.IsNullOrWhiteSpace(glassTint) ? "#0E1018" : glassTint,
            GlassTintStrength = glassTintStrength,
            GlassBlur = glassBlur,
            NeonGlow = neonGlow,
            Font = string.IsNullOrWhiteSpace(font) ? "default" : font,
        });

        return File(OverlayRenderer.EncodePng(bmp), "image/png");
    }

    /// <summary>
    /// Renders the sample poster with the **saved** banner settings plus the requested badges and
    /// badge layout — so the Badges tab shows the banner and badges composited together.
    /// </summary>
    /// <param name="badges">CSV of badge keys: watch_history, tmdb_trending, imdb_top250.</param>
    /// <param name="side">left | right.</param>
    /// <param name="vertical">top | middle | bottom.</param>
    /// <param name="scale">Badge size percent.</param>
    /// <param name="gap">Gap between stacked badges (percent of poster height).</param>
    [HttpGet("BadgePreview")]
    public async Task<ActionResult> GetBadgePreview(
        [FromQuery] string? badges = null,
        [FromQuery] string? side = null,
        [FromQuery] string? vertical = null,
        [FromQuery] int scale = 100,
        [FromQuery] int gap = 1,
        [FromQuery] string? source = null,
        CancellationToken cancellationToken = default)
    {
        using var bmp = await ResolveCanvasAsync(source, cancellationToken).ConfigureAwait(false);
        using var renderer = new OverlayRenderer();

        var config = Plugin.Instance?.Configuration;

        // Draw the banner using the saved settings (a representative RETURNING sample) so the badge
        // preview shows the full composite the way it'll look in the library.
        if (config is not null)
        {
            const string identity = "RETURNING";
            if (config.IsStatusShown(identity))
            {
                var label = config.LabelForStatus(identity);
                renderer.DrawStatusBanner(bmp, label + " 6/26", new OverlayRenderer.BannerOptions
                {
                    Style = config.BannerStyle,
                    Shape = config.BannerShape,
                    Position = config.BannerPosition,
                    FontScale = config.BannerFontScale,
                    ShowIcons = config.BannerIcons,
                    IconKey = identity,
                    ColorOverride = config.ColorForIdentity(identity),
                    FullWidth = config.BannerFullWidth,
                    Align = config.BannerAlign,
                    Shadow = config.BannerShadow,
                    ShadowStrength = config.BannerShadowStrength,
                    GlassTint = config.GlassTint,
                    GlassTintStrength = config.GlassTintStrength,
                    GlassBlur = config.GlassBlur,
                    NeonGlow = config.NeonGlow,
                    Font = config.BannerFont,
                });
            }
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in (badges ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(b);
        }

        if (set.Count > 0)
        {
            new BadgeCompositor().Apply(renderer, bmp, set, new BadgeCompositor.BadgeLayout(
                string.Equals(side, "right", StringComparison.OrdinalIgnoreCase),
                string.IsNullOrWhiteSpace(vertical) ? "top" : vertical,
                scale,
                gap));
        }

        return File(OverlayRenderer.EncodePng(bmp), "image/png");
    }

    private static SKBitmap BuildSamplePoster()
    {
        const int w = 600;
        const int h = 900;
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);

        // Diagonal gradient + soft shapes (so the glass frost has something to blur) + faux title bars.
        using (var bg = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(w, h),
                new[] { new SKColor(40, 52, 98), new SKColor(120, 46, 120), new SKColor(20, 24, 42) },
                new[] { 0f, 0.5f, 1f },
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(0, 0, w, h, bg);
        }

        using (var blob = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 28) })
        {
            canvas.DrawCircle(w * 0.32f, h * 0.42f, w * 0.34f, blob);
            canvas.DrawCircle(w * 0.78f, h * 0.70f, w * 0.22f, blob);
        }

        using (var bar = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 40) })
        {
            canvas.DrawRoundRect(new SKRect(w * 0.15f, h * 0.82f, w * 0.85f, h * 0.855f), 6, 6, bar);
            canvas.DrawRoundRect(new SKRect(w * 0.28f, h * 0.885f, w * 0.72f, h * 0.91f), 6, 6, bar);
        }

        return bmp;
    }
}
