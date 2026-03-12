namespace Rater.Core.Configuration;

public class RateLimiterConfiguration
{
    /// <summary>
    /// Config section key — matches appsettings.json top-level key.
    /// </summary>
    public const string SectionName = "RateLimiter";

    /// <summary>
    /// Which storage backend to use: "InMemory" | "Redis"
    /// </summary>
    public RaterStorage Storage { get; set; } = RaterStorage.InMemory;

    /// <summary>
    /// Redis config — only used when Storage = "Redis"
    /// </summary>
    public RedisConfiguration Redis { get; set; } = new();

    /// <summary>
    /// Ordered list of rules. First match wins.
    /// </summary>
    public List<RateLimitRule> Rules { get; set; } = new();
}

public class RedisConfiguration
{
    public string ConnectionString { get; set; } = "localhost:6379";
}
