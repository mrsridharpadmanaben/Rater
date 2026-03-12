using Microsoft.AspNetCore.Mvc;
using Rater.Core.Contracts;
using Rater.Core.Services;

namespace Rater.API;

[Route("[controller]")]
[ApiController]
public class CheckController : ControllerBase
{

    private readonly RateLimiterService _rateLimiterService;
    private readonly ILogger<CheckController> _logger;

    public CheckController(RateLimiterService rateLimiterService, ILogger<CheckController> logger)
    {
        _rateLimiterService = rateLimiterService;
        _logger = logger;
    }

    /// <summary>
    /// Core decision endpoint.
    /// Returns allow/deny for an incoming request descriptor.
    ///
    /// POST /check
    /// {
    ///   "clientId":   "user:abc123",
    ///   "ipAddress":  "192.168.1.1",
    ///   "endpoint":   "/api/search",
    ///   "httpMethod": "GET"
    /// }
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RateLimitDecision), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RateLimitDecision), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Check([FromBody] RateLimitRequest request)
    {
        // At least one identifier must be present — we can't rate limit a ghost
        if (string.IsNullOrWhiteSpace(request.ClientId) &&
            string.IsNullOrWhiteSpace(request.IpAddress) &&
            string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest(new
            {
                error = "At least one identifier is required: ClientId, IpAddress, or ApiKey."
            });
        }

        var decision = await _rateLimiterService.CheckAsync(request);

        // Attach standard rate limit headers to every response
        Response.Headers["X-RateLimit-Remaining"] = decision.Remaining.ToString();
        Response.Headers["X-RateLimit-Reset"] = decision.ResetAt.ToUnixTimeSeconds().ToString();
        Response.Headers["X-RateLimit-Rule"] = decision.RuleName;

        if (!decision.Allowed)
        {
            Response.Headers["Retry-After"] = decision.RetryAfterSeconds?.ToString();

            // Return 429 but with full decision body so caller knows exactly
            // when to retry and which rule fired
            return StatusCode(StatusCodes.Status429TooManyRequests, decision);
        }

        return Ok(decision);
    }
}
