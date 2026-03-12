using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Rater.Core.Contracts;
using Rater.Core.Services;

namespace Rater.Middleware;

/// <summary>
/// ASP.NET Core middleware. Intercepts every HTTP request,
/// builds a RateLimitRequest from HttpContext, calls RateLimiterService,
/// either short-circuits with 429 or passes through to next middleware.
/// </summary>
public class RateLimiterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimiterMiddleware> _logger;

    public RateLimiterMiddleware(RequestDelegate next, ILogger<RateLimiterMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RateLimiterService rateLimiterService)
    {
        // 01. request
        var rateLimitRequest = ExtractRequest(context);

        // 02. check async!
        var decision = await rateLimiterService.CheckAsync(rateLimitRequest);

        // 03. reponse headers
        // Clients can read these to know their current limit state
        AttachHeaders(context, decision);

        // 04. Allow & pass
        if (decision.Allowed)
        {
            await _next(context);   // continue down the pipeline
            return;
        }

        // Denied — return 429
        _logger.LogInformation("Rate limit exceeded. Key strategy hit for {Path}. RetryAfter={RetryAfter}s", context.Request.Path, decision.RetryAfterSeconds);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";

        var body = $$$"""
        {
            "error": "Too Many Requests",
            "retryAfterSeconds": {{{decision.RetryAfterSeconds}}},
            "resetAt": "{{{decision.ResetAt:O}}}",
            "rule": "{{{decision.RuleName}}}"
        }
        """;

        await context.Response.WriteAsync(body);
    }

    private RateLimitRequest ExtractRequest(HttpContext context)
    {
        return new RateLimitRequest
        {
            // Try X-Forwarded-For first (behind load balancer/proxy)
            // Fall back to direct connection IP
            IpAddress = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString(),

            // Standard API key header
            ApiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault(),

            // JWT claims or custom header for authenticated user
            ClientId = context.Request.Headers["X-Client-Id"].FirstOrDefault() ?? context.User?.FindFirst("sub")?.Value,

            Endpoint = context.Request.Path.Value,
            HttpMethod = context.Request.Method,
        };
    }

    private void AttachHeaders(HttpContext context, RateLimitDecision decision)
    {
        // rate limit headers — clients should read these
        context.Response.Headers["X-RateLimit-Limit"] = decision.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = decision.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = decision.ResetAt.ToUnixTimeSeconds().ToString();
        context.Response.Headers["X-RateLimit-Rule"] = decision.RuleName;

        if (!decision.Allowed && decision.RetryAfterSeconds.HasValue)
        {
            // Retry-After is the HTTP standard header for 429 responses
            context.Response.Headers["Retry-After"] = decision.RetryAfterSeconds.Value.ToString();
        }
    }
}
