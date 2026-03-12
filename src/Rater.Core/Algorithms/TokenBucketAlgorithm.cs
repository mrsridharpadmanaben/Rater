using Rater.Core.Configuration;
using Rater.Core.Contracts;
using Rater.Core.Storage;

namespace Rater.Core.Algorithms;


/// <summary>
/// Allows bursting up to the bucket capacity, then refills at a steady rate.
///
/// Mental model:
///   Bucket holds max N tokens.
///   Each request consumes 1 token.
///   Tokens refill continuously based on elapsed time.
///
///   Limit=10, WindowSeconds=60 means:
///     → refill rate = 10 tokens per 60 seconds = 1 token per 6 seconds
///     → a fresh client can fire 10 requests instantly (burst)
///     → then gets 1 new token every 6 seconds
///
/// Storage shape (one JSON blob per client):
///   rl:client:abc:/api/search:search-rule → {"Tokens":7,"LastRefillTicks":638...} 
/// </summary>
public class TokenBucketAlgorithm : IRateLimitAlgorithm
{
    private record BucketState(double Tokens, DateTimeOffset LastRefill);

    public async Task<RateLimitDecision> IsAllowedAsync(string key, RateLimitRule rule, IStorageProvider storage)
    {
        // Token bucket needs read-modify-write atomically.
        // store the bucket state as JSON in a single key.
        // AtomicUpdateAsync ensures no race conditions.

        var window = TimeSpan.FromSeconds(rule.WindowSeconds);
        var capacity = rule.Limit;

        // tokens per seconds
        double refillRate = (double)capacity / rule.WindowSeconds;

        BucketState state = await LoadStateAsync(key, storage, capacity);

        // Refill tokens based on elapsed time since last refill
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - state.LastRefill).TotalSeconds;
        var tokensToAdd = elapsed * refillRate;

        var newTokens = Math.Min(capacity, state.Tokens + tokensToAdd);

        if (newTokens >= 1)
        {
            // Consume one token
            var updatedState = new BucketState(newTokens - 1, now);
            await SaveStateAsync(key, updatedState, storage, window);

            var remaining = (int)Math.Floor(newTokens - 1);
            var resetAt = now.AddSeconds((capacity - remaining) / refillRate);

            return RateLimitDecision.Allow(remaining, resetAt, rule.Name);
        }

        // No tokens — calculate when the next token arrives
        var secondsUntilNextToken = (1 - newTokens) / refillRate;
        var retryAfter = (int)Math.Ceiling(secondsUntilNextToken);
        var bucketResetAt = now.AddSeconds(secondsUntilNextToken);

        // Save updated refill state even on deny — time has passed
        await SaveStateAsync(key, new BucketState(newTokens, now), storage, window);

        return RateLimitDecision.Deny(bucketResetAt, retryAfter, rule.Name);
    }

    private async Task<BucketState> LoadStateAsync(string key, IStorageProvider storage, int capacity)
    {
        // store JSON-encoded bucket state in a
        // separate meta-key alongside the main counter key.
        var metaKey = $"{key}:bucket_meta";
        var raw = await storage.GetAsync(metaKey);

        if (raw == 0)
        {
            // first request - full bucket
            return new BucketState(capacity, DateTimeOffset.UtcNow);
        }

        // encode the state as two separate keys
        // key:bucket_tokens  → tokens * 1000 (stored as long, 3 decimal precision)
        // key:bucket_refill  → LastRefill as Unix ticks
        var tokensKey = $"{key}:bucket_tokens";
        var refillKey = $"{key}:bucket_refill";

        var tokensRaw = await storage.GetAsync(tokensKey);
        var refillRaw = await storage.GetAsync(refillKey);

        var tokens = tokensRaw / 1000.0;   // restore precision
        var lastRefill = refillRaw == 0
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.FromUnixTimeMilliseconds(refillRaw);

        return new BucketState(tokens, lastRefill);
    }

    private async Task SaveStateAsync(string key, BucketState state, IStorageProvider storage, TimeSpan expiry)
    {
        var metaKey = $"{key}:bucket_meta";
        var tokensKey = $"{key}:bucket_tokens";
        var refillKey = $"{key}:bucket_refill";

        // Store tokens with 3 decimal precision by multiplying by 1000
        var tokensAsLong = (long)(state.Tokens * 1000);
        var refillTicks = state.LastRefill.ToUnixTimeMilliseconds();

        await storage.SetAsync(metaKey, 1, expiry);
        await storage.SetAsync(tokensKey, tokensAsLong, expiry);
        await storage.SetAsync(refillKey, refillTicks, expiry);
    }
}
