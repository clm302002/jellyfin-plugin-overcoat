using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// Supplies the poster the settings-page preview draws on: either the built-in placeholder or a real
/// one from the user's library.
///
/// The whole point of previewing on real art is that a banner looks different over a dark poster than
/// a bright one — so the art has to be **clean**. Handing back a poster Overcoat already overlaid
/// would draw a banner on top of a banner and make the preview a lie, which is why the search order
/// below prefers the originals vault (clean by definition) and otherwise only considers items
/// Overcoat has never touched.
/// </summary>
public sealed class PreviewPosterSource
{
    /// <summary>Widest/tallest source accepted, to bound decode cost from a click on a settings page.</summary>
    private const int MaxSourceDimension = 4000;

    /// <summary>The preview canvas. Matches the placeholder so the page layout doesn't jump.</summary>
    private const int CanvasWidth = 600;
    private const int CanvasHeight = 900;

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;

    public PreviewPosterSource(ILibraryManager libraryManager, ILogger logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// A random clean poster from the library, normalised to the preview canvas, or null when none
    /// can be found — callers fall back to the placeholder rather than failing the request.
    /// </summary>
    public async Task<SKBitmap?> TryGetRandomAsync(CancellationToken ct)
    {
        try
        {
            var state = new ProcessingState(Plugin.Instance!.DataFolderPath, _logger);

            // 1. The originals vault. Guaranteed un-overlaid, and already on disk.
            var vaulted = state.VaultedIds().ToList();
            Shuffle(vaulted);
            foreach (var id in vaulted.Take(8))
            {
                var bytes = await state.ReadOriginalAsync(id, ct).ConfigureAwait(false);
                var bmp = Decode(bytes);
                if (bmp is not null)
                {
                    return Fit(bmp);
                }
            }

            // 2. No usable vault entry. Fall back to items Overcoat has never processed — their art
            //    cannot be carrying one of our banners. Anything in the cache is skipped precisely
            //    because it probably is.
            var tracked = new HashSet<string>(state.CachedIds, StringComparer.Ordinal);
            var candidates = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series, BaseItemKind.Movie },
                Recursive = true,
            }).Where(i => !tracked.Contains(i.Id.ToString("N")) && i.HasImage(ImageType.Primary, 0)).ToList();

            Shuffle(candidates);
            foreach (var item in candidates.Take(8))
            {
                var bytes = await ProcessingState.ReadPrimaryImageAsync(item, _logger, ct).ConfigureAwait(false);
                var bmp = Decode(bytes);
                if (bmp is not null)
                {
                    return Fit(bmp);
                }
            }

            _logger.LogDebug("Overcoat: no clean library poster available for the preview.");
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A preview is cosmetic — never let it surface as an error on the settings page.
            _logger.LogDebug(ex, "Overcoat: random preview poster lookup failed; using the placeholder.");
            return null;
        }
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private SKBitmap? Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        var bmp = OverlayRenderer.Decode(bytes);
        if (bmp is null)
        {
            return null;
        }

        if (bmp.Width > MaxSourceDimension || bmp.Height > MaxSourceDimension || bmp.Width == 0 || bmp.Height == 0)
        {
            _logger.LogDebug("Overcoat: preview candidate {W}x{H} rejected as out of bounds.", bmp.Width, bmp.Height);
            bmp.Dispose();
            return null;
        }

        return bmp;
    }

    /// <summary>
    /// Scales the poster to the preview canvas, cropping to centre if its aspect differs. Banner
    /// geometry is derived from the canvas height, so a preview on a 2000x3000 poster and one on a
    /// 500x750 poster must end up the same size or the preview stops representing the real output.
    /// </summary>
    private static SKBitmap Fit(SKBitmap source)
    {
        using (source)
        {
            var scale = Math.Max(CanvasWidth / (double)source.Width, CanvasHeight / (double)source.Height);
            var scaledW = (int)Math.Ceiling(source.Width * scale);
            var scaledH = (int)Math.Ceiling(source.Height * scale);

            using var scaled = source.Resize(new SKImageInfo(scaledW, scaledH), SKFilterQuality.High);
            if (scaled is null)
            {
                return new SKBitmap(CanvasWidth, CanvasHeight);
            }

            var canvas = new SKBitmap(CanvasWidth, CanvasHeight);
            using var surface = new SKCanvas(canvas);
            surface.DrawBitmap(scaled, (CanvasWidth - scaledW) / 2f, (CanvasHeight - scaledH) / 2f);
            return canvas;
        }
    }
}
