using System.Globalization;

namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// Writes Overcoat's own log file — <c>Overcoat_&lt;date&gt;.log</c> — into Jellyfin's log directory,
/// so each run shows up as a separate, readable entry under Dashboard → Logs (the way other plugins
/// surface their logs). Best-effort: any IO error is swallowed so logging can never break a run.
/// Used alongside the standard <c>ILogger</c> (which still goes to the main server log).
/// </summary>
public sealed class FileLog : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();

    public FileLog(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            var name = "Overcoat_" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";
            _writer = new StreamWriter(Path.Combine(logDirectory, name), append: true) { AutoFlush = true };
        }
        catch
        {
            _writer = null;
        }
    }

    public void Info(string message) => Write("INF", message);

    public void Warn(string message) => Write("WRN", message);

    public void Error(string message) => Write("ERR", message);

    private void Write(string level, string message)
    {
        if (_writer is null)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                _writer.WriteLine(
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] [" + level + "] " + message);
            }
        }
        catch
        {
            // never let logging break a run
        }
    }

    public void Dispose() => _writer?.Dispose();
}
