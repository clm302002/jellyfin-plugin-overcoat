using System.Globalization;
using Jellyfin.Plugin.Overcoat.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Overcoat;

/// <summary>
/// The plugin entry point. Equivalent to the old script's module bootstrap: it owns the
/// configuration and exposes the dashboard config page. All real work happens in the
/// scheduled task (<see cref="ScheduledTasks.OverlayTask"/>) and the services it drives.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance. Services that are not constructed via DI
    /// (or that need the live config) read <c>Plugin.Instance!.Configuration</c>.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Overcoat";

    /// <inheritdoc />
    public override string Description =>
        "Applies status overlays (NEW/AIRING/RETURNING/ENDED/CANCELED) and watch/trending/IMDB Top 250 " +
        "badges to TV and movie posters, in-process. Successor to the Kometa-Jellyfin (jellymeta) script. " +
        "!!! BEFORE UNINSTALLING: your posters are NOT reverted automatically. Open Overcoat's Settings " +
        "→ Maintenance and run \"Restore original posters\" first, then uninstall. !!!";

    // Stable, unique id for this plugin. Generated once; never change it.
    public override Guid Id => Guid.Parse("604f4e22-a0a1-490d-b383-d60336318eaa");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.Configuration.configPage.html",
                GetType().Namespace),
        };
    }
}
