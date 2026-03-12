using Rater.Core.Configuration;
using Rater.Core.Contracts;
using Rater.Core.Storage;

namespace Rater.Core.Algorithms;

public class FixedWindowAlgorithm : IRateLimitAlgorithm
{
    /// <summary>
    /// One counter per time window.
    /// Window resets hard at the boundary — no smoothing.
    ///
    /// Timeline:
    ///   Window 1: [00:00 ──────── 01:00]  counter resets
    ///   Window 2: [01:00 ──────── 02:00]  counter resets
    ///
    /// Weakness: boundary attack.
    ///   99 requests at 00:59 + 99 requests at 01:01 = 198 in 2 seconds
    ///   despite limit of 100/min. Use SlidingWindow if this matters.
    ///
    /// Storage shape (one key per client per window):
    ///   rl:client:abc:/api/search:login-strict → 47  (TTL: 30s)
    /// </summary>
    public async Task<RateLimitDecision> IsAllowedAsync(string key, RateLimitRule rule, IStorageProvider storage)
    {
        var window = TimeSpan.FromSeconds(rule.WindowSeconds);

        // Atomically increment.If key didn't exist, it's created with TTL.
        // If it existed, TTL is preserved (not reset) — see InMemoryStorageProvider
        var count = await storage.IncrementAsync(key, window);

        // Get TTL to know when this window resets
        var (_, ttl) = await storage.GetWithTtlAsync(key);

        var resetAt = DateTimeOffset.UtcNow.Add(ttl ?? window);

        if (count <= rule.Limit)
        {
            var remaining = rule.Limit - count;

            return RateLimitDecision.Allow(remaining, resetAt, rule.Name);
        }

        var retryAfter = (int)Math.Ceiling((ttl ?? window).TotalSeconds);

        return RateLimitDecision.Deny(resetAt, retryAfter, rule.Name);
    }
}
