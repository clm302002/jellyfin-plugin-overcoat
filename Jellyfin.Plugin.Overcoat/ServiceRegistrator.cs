using Jellyfin.Plugin.Overcoat.ScheduledTasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Overcoat;

/// <summary>
/// Registers Overcoat's background services with the host. Jellyfin discovers this automatically
/// (it requires a parameterless constructor).
/// </summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Applies the configured run time to the live scheduled task at startup and on every save.
        serviceCollection.AddHostedService<ScheduleSync>();

        // Re-applies overlays after a library scan reverts them (Jellyfin re-adopts media-folder art
        // on every scan and this cannot be turned off — see ScanFollowUp).
        serviceCollection.AddHostedService<ScanFollowUp>();
    }
}
