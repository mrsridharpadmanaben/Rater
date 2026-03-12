using Rater.Core.Configuration;
using Rater.Core.Contracts;

namespace Rater.Core.KeyExtraction;

/// <summary>
/// Builds the storage key from an incoming request + rule.
/// The key is what gets stored in Redis/Memory — it uniquely identifies
/// WHO is being rate limited under WHICH rule.
/// </summary>
public interface IKeyExtractor
{
    /// <summary>
    /// Extract the rate limit key from the request.
    /// Returns null if required field is missing (e.g. ClientId strategy but no ClientId in request).
    /// </summary>
    string? Extract(RateLimitRequest request, RateLimitRule rule);
}
