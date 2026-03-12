using Rater.Core.Configuration;
using Rater.Core.Contracts;
using Rater.Core.Storage;

namespace Rater.Core.Algorithms;

public interface IRateLimitAlgorithm
{
    /// <summary>
    /// decision method: Given a storage key and rule, decide allow or deny.
    /// All state is read/written through storageProvider — algorithm is stateless.
    /// </summary>
    Task<RateLimitDecision> IsAllowedAsync(
        string key,
        RateLimitRule rule,
        IStorageProvider storage);
}
