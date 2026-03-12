namespace Rater.Core.Contracts;

public class RateLimitDecision
{
    /// <summary>
    /// True = let the request through. False = reject with 429.
    /// </summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// How many requests remain in the current window.
    /// </summary>
    public long Remaining { get; set; }

    /// <summary>
    /// When the current window resets (UTC).
    /// </summary>
    public DateTimeOffset ResetAt { get; set; }

    /// <summary>
    /// Seconds to wait before retrying. Null if allowed.
    /// </summary>
    public int? RetryAfterSeconds { get; set; }

    /// <summary>
    /// Which rule triggered this decision.
    /// </summary>
    public string RuleName { get; set; } = string.Empty;

    public static RateLimitDecision Allow(long remaining, DateTimeOffset resetAt, string ruleName) =>
        new()
        {
            Allowed = true,
            Remaining = remaining,
            ResetAt = resetAt,
            RetryAfterSeconds = null,
            RuleName = ruleName
        };

    public static RateLimitDecision Deny(DateTimeOffset resetAt, int retryAfterSeconds, string ruleName) =>
        new()
        {
            Allowed = false,
            Remaining = 0,
            ResetAt = resetAt,
            RetryAfterSeconds = retryAfterSeconds,
            RuleName = ruleName
        };
}
