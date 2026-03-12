using Microsoft.AspNetCore.Mvc;
using Rater.Core.Services;

namespace Rater.API;

[Route("[controller]")]
[ApiController]
public class StatusController : ControllerBase
{

    private readonly RateLimiterService _rateLimiterService;

    public StatusController(RateLimiterService rateLimiterService)
    {
        _rateLimiterService = rateLimiterService;
    }

    /// <summary>
    /// Returns current rate limit state for a given client across all matching rules.
    /// Useful for debugging, dashboards, and client-side backoff logic.
    ///
    /// GET /status/user:abc123
    /// GET /status/user:abc123?endpoint=/api/search   (narrow to specific endpoint)
    /// </summary>
    [HttpGet("{clientId}")]
    public async Task<IActionResult> GetStatus(
        [FromRoute] string clientId,
        [FromQuery] string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return BadRequest(new { error = "clientId is required." });

        var status = await _rateLimiterService.GetStatusAsync(clientId, endpoint);
        return Ok(status);
    }

    /// <summary>
    /// Health check endpoint.
    /// Returns 200 if service + storage are healthy, 503 if storage is unreachable.
    ///
    /// GET /status/health
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var healthy = await _rateLimiterService.IsHealthyAsync();

        if (healthy)
        {
            return Ok(new
            {
                status = "healthy",
                storage = "reachable",
                timestamp = DateTimeOffset.UtcNow,
            });
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            status = "unhealthy",
            storage = "unreachable",
            timestamp = DateTimeOffset.UtcNow,
        });
    }
}
