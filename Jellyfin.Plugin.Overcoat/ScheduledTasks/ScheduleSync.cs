using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Overcoat.ScheduledTasks;

/// <summary>
/// Keeps the "Apply Overcoat Overlays" task's daily trigger in step with the run time configured on
/// the plugin page.
///
/// Why this exists: Jellyfin reads <see cref="IScheduledTask.GetDefaultTriggers"/> only when it first
/// registers a task. From then on the trigger lives in the server's own scheduled-task config, so
/// changing the default in code does nothing for anyone who already has the plugin installed. This
/// service writes the trigger directly onto the live task — once at startup, and again whenever the
/// plugin configuration is saved.
///
/// Setting <c>ScheduleEnabled</c> to false hands control back to Dashboard → Scheduled Tasks: we then
/// leave the triggers completely alone, so a user who prefers to manage it there (or wants several
/// triggers, or an interval instead of a daily time) isn't fought by the plugin.
/// </summary>
public sealed class ScheduleSync : IHostedService, IDisposable
{
    private readonly ITaskManager _taskManager;
    private readonly ILogger<ScheduleSync> _logger;
    private bool _subscribed;

    public ScheduleSync(ITaskManager taskManager, ILogger<ScheduleSync> logger)
    {
        _taskManager = taskManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is { } plugin && !_subscribed)
        {
            plugin.ConfigurationChanged += OnConfigurationChanged;
            _subscribed = true;
        }

        Apply();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return Task.CompletedTask;
    }

    private void OnConfigurationChanged(object? sender, MediaBrowser.Model.Plugins.BasePluginConfiguration e) => Apply();

    /// <summary>
    /// Writes the configured run time onto the live task. Safe to call repeatedly: it no-ops when the
    /// trigger already matches, so saving unrelated settings doesn't churn the server's task config.
    /// </summary>
    private void Apply()
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config is null || !config.ScheduleEnabled)
            {
                return;
            }

            var worker = _taskManager.ScheduledTasks
                .FirstOrDefault(t => string.Equals(t.ScheduledTask.Key, "OvercoatApply", StringComparison.Ordinal));
            if (worker is null)
            {
                // Normal during early startup — the task may not be registered yet. The next config
                // save picks it up, and a fresh install gets the right time from GetDefaultTriggers.
                _logger.LogDebug("Overcoat: overlay task not registered yet; schedule not applied.");
                return;
            }

            var desired = OverlayTask.BuildDailyTrigger(config.ScheduleTimeOfDay);

            var current = worker.Triggers;
            if (current.Count == 1
                && current[0].Type == desired.Type
                && current[0].TimeOfDayTicks == desired.TimeOfDayTicks)
            {
                return;
            }

            worker.Triggers = new[] { desired };
            _logger.LogInformation(
                "Overcoat: overlay task scheduled daily at {Time}.",
                config.ScheduleTimeOfDay.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            // Never let a scheduling problem take the plugin (or server startup) down.
            _logger.LogError(ex, "Overcoat: could not apply the configured schedule.");
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed && Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged -= OnConfigurationChanged;
            _subscribed = false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Unsubscribe();
}
