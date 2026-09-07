using Microsoft.AspNetCore.Mvc;

namespace MusicLibrary.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Get()
    {
        return Ok(new { status = "ok" });
    }
}
