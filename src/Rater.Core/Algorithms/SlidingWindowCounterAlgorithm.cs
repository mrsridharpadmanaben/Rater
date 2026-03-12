using Rater.Core.Configuration;
using Rater.Core.Contracts;
using Rater.Core.Storage;

namespace Rater.Core.Algorithms;

/// <summary>
/// accuracy vs memory. Fixes the boundary attack of FixedWindow.
///
/// How it works:
///   Split the window into small sub-buckets (slots).
///   Each slot = windowSeconds / SlotCount seconds wide.
///   On each request:
///     1. Increment current slot's counter
///     2. Sum all slots that fall within the rolling window
///     3. Apply weighted partial credit to the oldest slot
///
/// Example with Limit=100, Window=60s, Slots=6 (each slot = 10s):
///
///   Time now: T=65
///   Slots:  [T=0..10]=20  [T=10..20]=15  ...  [T=60..70]=5  (current)
///
///   Rolling window covers T=5 to T=65.
///   Oldest slot [T=0..10] is partially in window (50% of it = 10 requests)
///   Total = 10 + (all middle slots) + 5 = weighted sum
///
/// Storage shape (one key per slot):
///   rl:client:abc:/api/search:rule-name:1710000000 → 45  (TTL: windowSeconds)
///   rl:client:abc:/api/search:rule-name:1710000010 → 12
/// </summary>
public class SlidingWindowCounterAlgorithm : IRateLimitAlgorithm
{
    private const int SlotCount = 10; // divide window into 10 sub-buckets

    public async Task<RateLimitDecision> IsAllowedAsync(string key, RateLimitRule rule, IStorageProvider storage)
    {
        var now = DateTimeOffset.UtcNow;
        var windowSeconds = rule.WindowSeconds;
        var slotSize = windowSeconds / (double)SlotCount; // seconds per slot
        var window = TimeSpan.FromSeconds(windowSeconds);

        // 01. Identify current slot
        var currentSlotStart = GetSlotStart(now, slotSize);
        var currentSlotKey = $"{key}:{currentSlotStart}";

        // 02. Increment current slot
        await storage.IncrementAsync(currentSlotKey, window);

        // 03. Sum all slots winthin the rolling window
        var totalCount = await CountRequestsInWindowAsync(key, now, windowSeconds, slotSize, storage);

        // 04. Decision
        var resetAt = DateTimeOffset.UtcNow.AddSeconds(slotSize);

        if (totalCount <= rule.Limit)
        {
            var remaining = rule.Limit - (int)totalCount;
            return RateLimitDecision.Allow(remaining, resetAt, rule.Name);
        }

        return RateLimitDecision.Deny(resetAt, (int)slotSize, rule.Name);
    }

    /// <summary>
    /// Floors a timestamp to the nearest slot boundary.
    /// e.g. T=65.3 with slotSize=10 → slot 60
    /// </summary>
    private long GetSlotStart(DateTimeOffset time, double slotSize)
    {
        var unixSeconds = time.ToUnixTimeSeconds();
        var slotBoundary = Math.Floor(unixSeconds / slotSize);

        return (long)(slotBoundary * slotSize);
    }

    private async Task<double> CountRequestsInWindowAsync(string key, DateTimeOffset now, int windowSeconds, double slotSize, IStorageProvider storage)
    {
        var windowStart = now.AddSeconds(-windowSeconds);
        var total = 0.0;

        // Walk through each slot boundary within the window
        for (var i = 0; i < SlotCount; i++)
        {
            var slotStart = GetSlotStart(now.AddSeconds(-slotSize * i), slotSize);
            var slotKey = $"{key}:{slotStart}";

            var count = await storage.GetAsync(slotKey);

            if (count == 0) continue;

            var slotEnd = slotStart + slotSize;
            var slotStartAbsolute = DateTimeOffset.FromUnixTimeSeconds((long)slotStart);
            var slotEndAbsolute = DateTimeOffset.FromUnixTimeSeconds((long)slotEnd);

            // Full slot inside window — count entirely
            if (slotStartAbsolute >= windowStart)
            {
                total += count;
            }
            // Partial slot — oldest slot straddles window boundary
            // Apply proportional weight: only count the portion inside window
            else if (slotEndAbsolute > windowStart)
            {
                var portionInWindow = (slotEndAbsolute - windowStart).TotalSeconds / slotSize;
                total += count * portionInWindow;
            }
            // Slot entirely outside window — skip
        }

        return total;
    }
}

