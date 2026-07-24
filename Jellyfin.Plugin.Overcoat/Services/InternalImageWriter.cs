using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// Saves Overcoat artwork through Jellyfin while explicitly forbidding a media-folder destination.
///
/// Jellyfin's stream overload follows the library's <c>SaveLocalMetadata</c> setting. That means a
/// normal <c>SaveImage(stream)</c> call can replace <c>poster.*</c>/<c>landscape.*</c> beside the
/// user's media. The filesystem-source overload exposes Jellyfin's safety override; passing
/// <c>false</c> stores the image in Jellyfin's internal metadata area regardless of that setting.
/// </summary>
public static class InternalImageWriter
{
    /// <summary>Saves bytes as item artwork without ever selecting the media folder.</summary>
    public static async Task SaveAsync(
        IProviderManager providerManager,
        BaseItem item,
        byte[] bytes,
        string mimeType,
        ImageType imageType,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDirectory);
        var sourcePath = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ExtensionFor(mimeType));
        await File.WriteAllBytesAsync(sourcePath, bytes, cancellationToken).ConfigureAwait(false);

        try
        {
            // Jellyfin removes sourcePath after a successful save. false is the hard media-folder
            // boundary; do not replace this with the stream overload, which has no such parameter.
            await providerManager
                .SaveImage(item, sourcePath, mimeType, imageType, null, false, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // Keep failures retry-safe without accumulating abandoned staging files.
            try
            {
                File.Delete(sourcePath);
            }
            catch (FileNotFoundException)
            {
                // Expected after a successful Jellyfin save.
            }
        }
    }

    private static string ExtensionFor(string mimeType)
        => mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png",
        };
}
