using Jellyfin.Plugin.Overcoat.Services;
using Microsoft.Extensions.Logging;

// Usage: TMDB_API_KEY=xxx dotnet run
var key = Environment.GetEnvironmentVariable("TMDB_API_KEY");
if (string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("Set TMDB_API_KEY");
    return 1;
}

using var lf = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
var logger = lf.CreateLogger("TmdbTest");
using var http = new HttpClient();
var tmdb = new TmdbService(http, key!, logger);
var ct = CancellationToken.None;

// If TMDB ids are passed as args, only run the status+resolver section over them.
var argIds = args.Select(a => int.TryParse(a, out var n) ? n : (int?)null).Where(n => n is not null).Select(n => n!.Value).ToArray();

if (argIds.Length == 0)
{
    Console.WriteLine("== external id resolution ==");
    var bb = await tmdb.FindByExternalIdAsync("tt0903747", "imdb_id", false, ct); // Breaking Bad
    Console.WriteLine($"  imdb tt0903747 -> {bb} (expect 1396)");

    Console.WriteLine("== title search ==");
    var search = await tmdb.SearchShowAsync("Breaking Bad", 2008, ct);
    Console.WriteLine($"  'Breaking Bad' (2008) -> {search} (expect 1396)");
}

Console.WriteLine("== status + resolver ==");
var ids = argIds.Length > 0 ? argIds : new[] { 1396, 456, 1399, 71912, 95557, 60625 };
foreach (var id in ids)
{
    var info = await tmdb.GetTvStatusAsync(id, ct);
    if (info is null) { Console.WriteLine($"  {id}: <no data>"); continue; }
    var text = StatusOverlayResolver.Resolve(info);
    Console.WriteLine($"  {id}: status='{info.Status}' firstAir={info.DaysSinceFirstAir}d " +
                      $"next={info.NextAirDate}({info.DaysUntilAir}) lastEp={info.DaysSinceLastAir}d  =>  banner='{text}'");
}

return 0;
