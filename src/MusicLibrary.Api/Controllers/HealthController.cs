using Microsoft.AspNetCore.Mvc;
using MusicLibrary.Contracts.Responses;

namespace MusicLibrary.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class HealthController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Get()
    {
        return Ok(new { status = "ok" });
    }

    [HttpGet("client-logging")]
    public ActionResult<ClientLoggingConfiguration> GetClientLoggingConfiguration()
    {
        var sourceToken = configuration["BetterStack:SourceToken"]
            ?? throw new InvalidOperationException("BetterStack:SourceToken is required.");
        var ingestingHost = configuration["BetterStack:IngestingHost"]
            ?? throw new InvalidOperationException("BetterStack:IngestingHost is required.");
        return Ok(new ClientLoggingConfiguration(sourceToken, ingestingHost));
    }
}
