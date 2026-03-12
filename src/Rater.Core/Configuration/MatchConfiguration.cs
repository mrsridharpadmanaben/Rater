namespace Rater.Core.Configuration;

/// <summary>
/// Defines what a rule matches against.
/// All properties are optional — empty = match everything.
/// </summary>
public class MatchConfiguration
{
    /// <summary>
    /// Exact path or wildcard. e.g. "/api/login" or "/api/*"
    /// Null/empty = match all endpoints.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// HTTP method filter. e.g. "POST", "GET"
    /// Null/empty = match all methods.
    /// </summary>
    public string? HttpMethod { get; set; }
}
