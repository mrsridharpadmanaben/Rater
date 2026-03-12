namespace Rater.Core.Configuration;

public class RateLimitRule
{
    /// <summary>
    /// Unique name for this rule. Returned in decisions so callers know which rule fired.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What incoming requests this rule applies to.
    /// </summary>
    public MatchConfiguration MatchConfiguration { get; set; } = new();

    /// <summary>
    /// Which algorithm to use: "FixedWindow" | "TokenBucket" | "SlidingWindowCounter"
    /// </summary>
    public RaterAlgorithm Algorithm { get; set; } = RaterAlgorithm.FixedWindow;

    /// <summary>
    /// Max requests allowed within the window (or max tokens in bucket).
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Time window in seconds.
    /// For TokenBucket: how often the bucket fully refills.
    /// </summary>
    public int WindowSeconds { get; set; }

    /// <summary>   
    /// How to build the storage key.
    /// "IpAddress" | "ClientId" | "ApiKey" | "Composite"
    /// </summary>
    public RaterKeyStrategy KeyStrategy { get; set; } = RaterKeyStrategy.ClientId;
}
