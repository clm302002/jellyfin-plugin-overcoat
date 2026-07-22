using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Overcoat.Api;

/// <summary>Serves the non-sensitive, embedded dashboard stylesheet.</summary>
[ApiController]
[AllowAnonymous]
[Route("Overcoat/Configuration")]
public sealed class ConfigurationAssetsController : ControllerBase
{
    /// <summary>Returns the cacheable stylesheet used by the Overcoat configuration page.</summary>
    [HttpGet("configPage.css")]
    [Produces("text/css")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public ActionResult GetStylesheet()
    {
        const string resource = "Jellyfin.Plugin.Overcoat.Configuration.configPage.css";
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=86400";
        return File(stream, "text/css; charset=utf-8");
    }
}
