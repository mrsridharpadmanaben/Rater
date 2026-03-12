using Rater.Core.Configuration;
using Rater.Core.Contracts;

namespace Rater.Core.KeyExtraction;


/// <summary>
/// Rule name in key:
/// Without it, two rules targeting the same endpoint share the same counter — 
/// they'd bleed into each other. With the rule name, 
/// each rule gets its own isolated bucket in storage.
/// </summary>

public abstract class KeyExtractorBase : IKeyExtractor
{
    public abstract string? Extract(RateLimitRequest request, RateLimitRule rule);

    /// <summary>
    /// Normalizes endpoint so "/API/Search" and "/api/search" hit the same key.
    /// Falls back to "global" when no endpoint is present.
    /// </summary>
    protected static string NormalizeEndpoint(string? endpoint) =>
        string.IsNullOrWhiteSpace(endpoint)
            ? "global"
            : endpoint.Trim('/').ToLowerInvariant();
}

/// <summary>
/// Limits by IP address. for unauthenticated endpoints.
/// Key shape: rl:ip:{ipAddress}:{endpoint}
/// </summary>
public class IpKeyExtractor : KeyExtractorBase
{
    public override string? Extract(RateLimitRequest request, RateLimitRule rule)
    {
        if (string.IsNullOrWhiteSpace(request.IpAddress))
            return null;

        var endpoint = NormalizeEndpoint(request.Endpoint);

        return $"rl:ip:{request.IpAddress}:{endpoint}:{rule.Name}";
    }
}

/// <summary>
/// Limits by authenticated client/user ID. for authenticated APIs.
/// Key shape: rl:client:{clientId}:{endpoint}:{ruleName}
/// </summary>
public class ClientIdKeyExtractor : KeyExtractorBase
{
    public override string? Extract(RateLimitRequest request, RateLimitRule rule)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
            return null;

        var endpoint = NormalizeEndpoint(request.Endpoint);

        return $"rl:client:{request.ClientId}:{endpoint}:{rule.Name}";
    }
}

/// <summary>
/// Limits by API key. for B2B / developer portal scenarios.
/// Key shape: rl:apikey:{apiKey}:{endpoint}:{ruleName}
/// </summary>
public class ApiKeyExtractor : KeyExtractorBase
{
    public override string? Extract(RateLimitRequest request, RateLimitRule rule)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return null;

        var endpoint = NormalizeEndpoint(request.Endpoint);

        return $"rl:apikey:{request.ApiKey}:{endpoint}:{rule.Name}";
    }
}

/// <summary>
/// Combines IP + ClientId. A single request is checked against both.
/// "max 100/min per user AND max 50/min per IP".
/// Key shape: rl:composite:{ip}:{clientId}:{endpoint}:{ruleName}
/// </summary>
public class CompositeKeyExtractor : KeyExtractorBase
{
    public override string? Extract(RateLimitRequest request, RateLimitRule rule)
    {
        // Composite requires at least IP or ClientId
        if (string.IsNullOrWhiteSpace(request.IpAddress) &&
            string.IsNullOrWhiteSpace(request.ClientId))
            return null;

        var ip = request.IpAddress ?? "unknown";

        var clientId = request.ClientId ?? "anonymous";

        var endpoint = NormalizeEndpoint(request.Endpoint);

        return $"rl:composite:{ip}:{clientId}:{endpoint}:{rule.Name}";
    }
}