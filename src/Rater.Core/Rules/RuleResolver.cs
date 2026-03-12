using Microsoft.Extensions.Options;
using Rater.Core.Configuration;
using Rater.Core.Contracts;

namespace Rater.Core.Rules;

public class RuleResolver
{
    private readonly IOptionsMonitor<RateLimiterConfiguration> _configurations;

    // IOptionsMonitor — every call to .CurrentValue gets the LATEST config.
    // If appsettings.json changes at runtime, next request picks it up.
    public RuleResolver(IOptionsMonitor<RateLimiterConfiguration> configurations)
    {
        _configurations = configurations;
    }

    /// <summary>
    /// Walks rules top-to-bottom. Returns first match.
    /// Returns null if no rule matches — caller decides what to do (allow by default).
    /// </summary>
    public RateLimitRule? Resolve(RateLimitRequest request)
    {
        var rules = _configurations.CurrentValue.Rules;

        foreach (var rule in rules)
        {
            if (Matches(rule.MatchConfiguration, request))
                return rule;
        }

        return null;
    }

    /// <summary>
    /// Returns ALL rules that match the request.
    /// Used by the status endpoint to show full picture for a client.
    /// </summary>
    public IEnumerable<RateLimitRule> ResolveAll(RateLimitRequest request)
    {
        var rules = _configurations.CurrentValue.Rules;
        return rules.Where(rule => Matches(rule.MatchConfiguration, request));
    }

    private static bool Matches(MatchConfiguration match, RateLimitRequest request)
    {
        // Empty match config = wildcard, matches everything
        if (IsEmpty(match))
            return true;

        if (!MatchesEndpoint(match.Endpoint, request.Endpoint))
            return false;

        if (!MatchesMethod(match.HttpMethod, request.HttpMethod))
            return false;

        return true;
    }

    private static bool IsEmpty(MatchConfiguration match) =>
        string.IsNullOrWhiteSpace(match.Endpoint)
        && string.IsNullOrWhiteSpace(match.HttpMethod);

    private static bool MatchesEndpoint(string? ruleEndpoint, string? requestEndpoint)
    {
        // No endpoint constraint on rule = matches all endpoints
        if (string.IsNullOrWhiteSpace(ruleEndpoint))
            return true;

        // No endpoint on request but rule has one = no match
        if (string.IsNullOrWhiteSpace(requestEndpoint))
            return false;

        var normalizedRule = ruleEndpoint.Trim('/').ToLowerInvariant();
        var normalizedRequest = requestEndpoint.Trim('/').ToLowerInvariant();

        // Exact match
        if (normalizedRule == normalizedRequest)
            return true;

        // Wildcard match — rule: "api/search/*" matches "api/search/users"
        if (normalizedRule.EndsWith("/*"))
        {
            var prefix = normalizedRule[..^2]; // strip trailing /*

            return normalizedRequest.StartsWith(prefix);
        }

        return false;
    }

    private static bool MatchesMethod(string? ruleMethod, string? requestMethod)
    {
        // No method constraint on rule = matches all methods
        if (string.IsNullOrWhiteSpace(ruleMethod))
            return true;

        // No method on request but rule has one = no match
        if (string.IsNullOrWhiteSpace(requestMethod))
            return false;

        return ruleMethod.Equals(requestMethod, StringComparison.OrdinalIgnoreCase);
    }

}
