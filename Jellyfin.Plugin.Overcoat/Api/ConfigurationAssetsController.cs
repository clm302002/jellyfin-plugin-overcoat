using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.Overcoat.Api;

/// <summary>Serves the non-sensitive, embedded dashboard stylesheet.</summary>
[ApiController]
// Anonymous deliberately: the settings page pulls this with a plain <link rel="stylesheet">, which
// cannot attach an auth header. It is a stylesheet with no data in it.
[AllowAnonymous]
[Route("Overcoat/Configuration")]
public sealed class ConfigurationAssetsController : ControllerBase
{
    /// <summary>Version-derived ETag, so the cached copy is only reused while the plugin is unchanged.</summary>
    private static readonly EntityTagHeaderValue Tag = new(
        "\"" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0") + "\"",
        isWeak: false);

    /// <summary>Returns the stylesheet used by the Overcoat configuration page.</summary>
    /// <response code="200">Stylesheet returned.</response>
    /// <response code="304">Caller's cached copy is current.</response>
    /// <response code="404">Stylesheet missing from the assembly.</response>
    [HttpGet("configPage.css")]
    [Produces("text/css")]
    public ActionResult GetStylesheet()
    {
        const string resource = "Jellyfin.Plugin.Overcoat.Configuration.configPage.css";
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null)
        {
            return NotFound();
        }

        // Revalidate rather than cache blindly. A plain long max-age meant that after an update the
        // browser kept the OLD stylesheet for up to a day while loading the NEW markup — a settings
        // page that looks broken in a way that reads as "the update broke it", with no way for the
        // user to know a hard refresh would fix it.
        //
        // An ETag tied to the assembly version makes correctness automatic: unchanged plugin, and the
        // conditional request is answered 304 with no body; new version, and the ETag differs so the
        // browser fetches the new file immediately. `File(..., entityTag)` handles If-None-Match, so
        // the usual case is still a tiny revalidation rather than a full download.
        Response.Headers.CacheControl = "public,max-age=0,must-revalidate";
        return File(stream, "text/css; charset=utf-8", lastModified: null, entityTag: Tag);
    }
}
