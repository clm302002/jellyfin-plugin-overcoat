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
    // Shown on the plugin's page in the dashboard. Kept to a couple of plain sentences: this sits
    // above the revision history in a narrow column, so anything longer becomes a wall of text, and
    // shouting in it (the previous version used "!!!") reads as broken rather than important.
    // The uninstall note stays because losing it costs users their original posters — but as a
    // normal sentence, and the Maintenance tab repeats it where the button actually is.
    public override string Description =>
        "Adds status banners (NEW, AIRING, RETURNING, ENDED and CANCELED) and badges for trending, " +
        "IMDb Top 250 and recently watched titles directly onto your TV and movie posters. " +
        "Before uninstalling, run Settings → Maintenance → Restore original posters: uninstalling on " +
        "its own leaves the overlays in place.";

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

            // Surface Overcoat directly in the dashboard's left sidebar (not just the plugin list).
            EnableInMainMenu = true,
            MenuIcon = "layers",
        };
    }
}
