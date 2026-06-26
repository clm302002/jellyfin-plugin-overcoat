using Jellyfin.Plugin.Overcoat.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkiaSharp;

namespace Jellyfin.Plugin.Overcoat.Api;

/// <summary>
/// Live banner preview for the dashboard settings page. Renders a synthetic sample poster with the
/// banner drawn per the supplied options, so the page can show what a setting looks like instantly —
/// without running the scheduled task. Reuses the real <see cref="OverlayRenderer"/> so the preview
/// matches production output exactly.
/// </summary>
[ApiController]
[Authorize]
[Route("Overcoat")]
[Produces("image/png")]
public class PreviewController : ControllerBase
{
    /// <summary>Renders the sample poster + banner as a PNG.</summary>
    /// <param name="style">solid | glass | neon.</param>
    /// <param name="shape">pill | square | drop.</param>
    /// <param name="position">top | bottom.</param>
    /// <param name="fontScale">Font-size multiplier.</param>
    /// <param name="status">Banner text to render (defaults to a RETURNING sample with a date).</param>
    [HttpGet("BannerPreview")]
    public ActionResult GetBannerPreview(
        [FromQuery] string? style,
        [FromQuery] string? shape,
        [FromQuery] string? position,
        [FromQuery] double fontScale = 1.0,
        [FromQuery] string status = "RETURNING 6/26")
    {
        const int w = 600;
        const int h = 900;

        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            // Sample poster backdrop: a diagonal gradient + soft shapes so the glass frost has
            // something to blur, plus a couple of faux title bars for realism.
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
        }

        using var renderer = new OverlayRenderer();
        renderer.DrawStatusBanner(bmp, status, new OverlayRenderer.BannerOptions
        {
            Style = string.IsNullOrWhiteSpace(style) ? "solid" : style,
            Shape = string.IsNullOrWhiteSpace(shape) ? "pill" : shape,
            Position = string.IsNullOrWhiteSpace(position) ? "top" : position,
            FontScale = fontScale <= 0 ? 1.0 : fontScale,
        });

        return File(OverlayRenderer.EncodePng(bmp), "image/png");
    }
}
