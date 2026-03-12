namespace Rater.Core.Contracts;

public class RateLimitRequest
{
    /// <summary>
    /// Authenticated user/client ID if available.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Caller IP address.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// API key from header if present.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The endpoint being accessed. e.g. "/api/search"
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// HTTP method. e.g. "GET", "POST"
    /// </summary>
    public string? HttpMethod { get; set; }
}
