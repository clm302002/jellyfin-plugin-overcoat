using Jellyfin.Plugin.Overcoat.Services;
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, byte[]> PreviewPosters = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PreviewLocks = new(StringComparer.Ordinal);
    private const int MaxPreviewPosters = 48;
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
    private async Task<SKBitmap> ResolveCanvasAsync(string? source, string? previewKey, bool landscape, CancellationToken ct)
    {
        if (!string.Equals(source, "random", StringComparison.OrdinalIgnoreCase))
        {
            return BuildSamplePoster(landscape);
        }

        // The browser creates a new opaque key only when Random is clicked. Every subsequent
        // banner/badge render reuses the clean poster cached under that key, so changing controls or
        // switching studios cannot silently reroll the artwork.
        if (string.IsNullOrWhiteSpace(previewKey)
            || previewKey.Length > 64
            || previewKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
        {
            return BuildSamplePoster(landscape);
        }

        previewKey += landscape ? "_wide" : "_portrait";
        if (PreviewPosters.TryGetValue(previewKey, out var cached))
        {
            return OverlayRenderer.Decode(cached) ?? BuildSamplePoster(landscape);
        }

        var gate = PreviewLocks.GetOrAdd(previewKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (PreviewPosters.TryGetValue(previewKey, out cached))
            {
                return OverlayRenderer.Decode(cached) ?? BuildSamplePoster(landscape);
            }

            var picker = new PreviewPosterSource(_libraryManager, _logger);
            var selected = await picker.TryGetRandomAsync(landscape, ct).ConfigureAwait(false);
            if (selected is null)
            {
                return BuildSamplePoster(landscape);
            }

            PreviewPosters[previewKey] = OverlayRenderer.EncodePng(selected);
            while (PreviewPosters.Count > MaxPreviewPosters)
            {
                var oldest = PreviewPosters.Keys.FirstOrDefault(k => !string.Equals(k, previewKey, StringComparison.Ordinal));
                if (oldest is null || !PreviewPosters.TryRemove(oldest, out _))
                {
                    break;
                }
            }

            return selected;
        }
        finally
        {
            gate.Release();
            PreviewLocks.TryRemove(previewKey, out _);
        }
    }

    /// <summary>Renders the sample poster + banner (and any toggled badges) as a PNG — one composite so
    /// a single live preview shows the full result.</summary>
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
        [FromQuery] string? previewKey = null,
        [FromQuery] string? layout = null,
        [FromQuery] string? badges = null,
        [FromQuery] string? side = null,
        [FromQuery] string? vertical = null,
        [FromQuery] int badgeScale = 100,
        [FromQuery] int badgeGap = 1,
        CancellationToken cancellationToken = default)
    {
        var landscape = string.Equals(layout, "landscape", StringComparison.OrdinalIgnoreCase);
        using var bmp = await ResolveCanvasAsync(source, previewKey, landscape, cancellationToken).ConfigureAwait(false);
        using var renderer = new OverlayRenderer();

        var hasBanner = !string.IsNullOrWhiteSpace(status);
        var bannerPosition = string.IsNullOrWhiteSpace(position) ? "top" : position;
        if (hasBanner)
        {
            renderer.DrawStatusBanner(bmp, status, new OverlayRenderer.BannerOptions
            {
                Style = string.IsNullOrWhiteSpace(style) ? "solid" : style,
                Shape = string.IsNullOrWhiteSpace(shape) ? "pill" : shape,
                Position = bannerPosition,
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
        }

        // Composite the toggled badges on top so a single preview shows the full banner + badges result.
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in (badges ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(b);
        }

        if (set.Count > 0)
        {
            var reservedTop = landscape && hasBanner
                && !string.Equals(bannerPosition, "bottom", StringComparison.OrdinalIgnoreCase) ? 18 : 0;
            new BadgeCompositor().Apply(renderer, bmp, set, new BadgeCompositor.BadgeLayout(
                string.Equals(side, "right", StringComparison.OrdinalIgnoreCase),
                string.IsNullOrWhiteSpace(vertical) ? "top" : vertical,
                badgeScale,
                badgeGap,
                reservedTop));
        }

        return File(OverlayRenderer.EncodePng(bmp), "image/png");
    }

    private static SKBitmap BuildSamplePoster(bool landscape)
    {
        int w = landscape ? 960 : 600;
        int h = landscape ? 540 : 900;
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
