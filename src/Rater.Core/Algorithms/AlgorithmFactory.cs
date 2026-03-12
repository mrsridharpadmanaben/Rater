using Rater.Core.Configuration;

namespace Rater.Core.Algorithms;

public class AlgorithmFactory
{
    private readonly Dictionary<RaterAlgorithm, IRateLimitAlgorithm> _algorithms;

    public AlgorithmFactory()
    {
        _algorithms = new Dictionary<RaterAlgorithm, IRateLimitAlgorithm>
        {
            [RaterAlgorithm.FixedWindow] = new FixedWindowAlgorithm(),
            [RaterAlgorithm.TokenBucket] = new TokenBucketAlgorithm(),
            [RaterAlgorithm.SlidingWindowCounter] = new SlidingWindowCounterAlgorithm(),
        };
    }

    /// <summary>
    /// Resolves algorithm by enum. Throws clearly on unknown value — fail fast.
    /// </summary>
    public IRateLimitAlgorithm Resolve(RaterAlgorithm algorithm)
    {
        if (_algorithms.TryGetValue(algorithm, out var instance))
            return instance;

        throw new InvalidOperationException(
            $"No algorithm registered for '{algorithm}'. " +
            $"Valid values: {string.Join(", ", Enum.GetNames<RaterAlgorithm>())}");
    }
}
