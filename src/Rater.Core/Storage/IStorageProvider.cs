namespace Rater.Core.Storage;

/// <summary>
/// Abstraction over the state store.
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// Get current value of a counter. 
    /// Returns 0 if key doesn't exist.
    /// </summary>
    Task<long> GetAsync(string key);

    /// <summary>
    /// Get current value AND remaining TTL on a key.
    /// Used by status endpoint and algorithms that need reset time.
    /// </summary>
    Task<(long Count, TimeSpan? Ttl)> GetWithTtlAsync(string key);

    /// <summary>
    /// Set a value with expiry.
    /// Used by TokenBucket to save full state.
    /// </summary>
    Task SetAsync(string key, long value, TimeSpan expiry);

    /// <summary>
    /// Atomically increment a counter. 
    /// Creates it with TTL if it doesn't exist.
    /// Returns the new count after increment.
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan expiry);


    /// <summary>
    /// Read-modify-write atomically.
    /// Callback receives current value, returns new value.
    /// This is where InMemory uses lock{} and Redis will use Lua.
    /// </summary>
    Task<long> AtomicUpdateAsync(string key, TimeSpan expiry, Func<long, long> update);

    /// <summary>
    /// Delete a key. Useful for testing and reset scenarios.
    /// </summary>
    Task DeleteAsync(string key);

    /// <summary>
    /// Check if storage backend is reachable. Used by health endpoint.
    /// </summary>
    Task<bool> IsHealthyAsync();
}
