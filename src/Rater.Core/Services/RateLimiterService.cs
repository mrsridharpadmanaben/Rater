using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rater.Core.Algorithms;
using Rater.Core.Configuration;
using Rater.Core.Contracts;
using Rater.Core.KeyExtraction;
using Rater.Core.Rules;
using Rater.Core.Storage;

namespace Rater.Core.Services;

public class RateLimiterService
{
    private readonly RuleResolver _ruleResolver;
    private readonly AlgorithmFactory _algorithmFactory;
    private readonly KeyExtractorFactory _keyExtractorFactory;
    private readonly IStorageProvider _storage;
    private readonly IOptionsMonitor<RateLimiterConfiguration> _configMonitor;
    private readonly ILogger<RateLimiterService> _logger;

    public RateLimiterService(
        RuleResolver ruleResolver,
        AlgorithmFactory algorithmFactory,
        KeyExtractorFactory keyExtractorFactory,
        IStorageProvider storage,
        IOptionsMonitor<RateLimiterConfiguration> configMonitor,
        ILogger<RateLimiterService> logger)
    {
        _ruleResolver = ruleResolver;
        _algorithmFactory = algorithmFactory;
        _keyExtractorFactory = keyExtractorFactory;
        _storage = storage;
        _configMonitor = configMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Main entry point. Given a request, returns allow or deny.
    /// </summary>
    public async Task<RateLimitDecision> CheckAsync(RateLimitRequest request)
    {
        // 01. find rule
        var rule = _ruleResolver.Resolve(request);

        if (rule is null)
        {
            // No rule matched — allow by default, log
            _logger.LogDebug("No rule matched for endpoint {Endpoint}. Allowing by default.", request.Endpoint);

            return RateLimitDecision.Allow(
                remaining: int.MaxValue,
                resetAt: DateTimeOffset.UtcNow.AddDays(1),
                ruleName: "no-match-allow");
        }

        // 02. Build storage key
        var keyExtractor = _keyExtractorFactory.Resolve(rule.KeyStrategy);
        var key = keyExtractor.Extract(request, rule);


        if (key is null)
        {
            // Strategy required a field (e.g. ClientId) that wasn't in request
            _logger.LogWarning(
                "Key extraction returned null for rule {Rule} using strategy {Strategy}. " +
                "Request is missing required field. Allowing by default.",
                rule.Name, rule.KeyStrategy);

            return RateLimitDecision.Allow(
                remaining: int.MaxValue,
                resetAt: DateTimeOffset.UtcNow.AddDays(1),
                ruleName: rule.Name);
        }

        // 03. Algorithm
        var algorithm = _algorithmFactory.Resolve(rule.Algorithm);
        var decision = await algorithm.IsAllowedAsync(key, rule, _storage);

        // 04. log it
        if (decision.Allowed)
        {
            _logger.LogDebug(
                "ALLOW key={Key} rule={Rule} remaining={Remaining}",
                key, rule.Name, decision.Remaining);
        }
        else
        {
            _logger.LogInformation(
                "DENY key={Key} rule={Rule} retryAfter={RetryAfter}s",
                key, rule.Name, decision.RetryAfterSeconds);
        }

        return decision;
    }

    /// <summary>
    /// Returns current rate limit state for a client across ALL matching rules.
    /// Used by GET /status/{clientId}.
    /// </summary>
    public async Task<StatusResponse> GetStatusAsync(string clientId, string? endpoint = null)
    {
        // Build a synthetic request to resolve which rules apply to this client
        var syntheticRequest = new RateLimitRequest
        {
            ClientId = clientId,
            Endpoint = endpoint,
            IpAddress = null,
        };

        var matchingRules = _ruleResolver.ResolveAll(syntheticRequest).ToList();

        var ruleStatuses = new List<RuleStatus>();

        foreach (var rule in matchingRules)
        {
            var keyExtractor = _keyExtractorFactory.Resolve(rule.KeyStrategy);
            var key = keyExtractor.Extract(syntheticRequest, rule);

            if (key is null) continue;

            var (count, ttl) = await _storage.GetWithTtlAsync(key);

            var resetAt = ttl.HasValue
                ? DateTimeOffset.UtcNow.Add(ttl.Value)
                : DateTimeOffset.UtcNow.AddSeconds(rule.WindowSeconds);

            ruleStatuses.Add(new RuleStatus
            {
                RuleName = rule.Name,
                StorageKey = key,
                Algorithm = rule.Algorithm.ToString(),
                Limit = rule.Limit,
                CurrentCount = (int)count,
                Remaining = Math.Max(0, rule.Limit - (int)count),
                ResetAt = resetAt,
            });
        }

        return new StatusResponse
        {
            ClientKey = clientId,
            ActiveRules = ruleStatuses,
        };
    }

    /// <summary>
    /// Checks storage backend is reachable.
    /// Used by GET /health.
    /// </summary>
    public async Task<bool> IsHealthyAsync() => await _storage.IsHealthyAsync();
}
