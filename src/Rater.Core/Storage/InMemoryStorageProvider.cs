
using System.Collections.Concurrent;

namespace Rater.Core.Storage;

public class InMemoryStorageProvider : IStorageProvider
{
    // value + expiry time
    private record Entry(long Value, DateTimeOffset ExpiresAt);

    // key + entry 
    private readonly ConcurrentDictionary<string, Entry> _store = new();

    // One global lock — user A blocks user B even though different keys
    // Per-key locks — user A and user B never block each other

    // One lock object per key — more granular than a single global lock.
    // Prevents two threads updating DIFFERENT keys from blocking each other.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();


    public Task<long> GetAsync(string key)
    {
        var entry = GetLive(key);

        return Task.FromResult(entry?.Value ?? 0L);
    }

    public Task<(long Count, TimeSpan? Ttl)> GetWithTtlAsync(string key)
    {
        var entry = GetLive(key);

        if (entry is null)
            return Task.FromResult((0L, (TimeSpan?)null));

        var ttl = entry.ExpiresAt - DateTimeOffset.UtcNow;

        return Task.FromResult((entry.Value, (TimeSpan?)ttl));
    }

    public async Task<long> IncrementAsync(string key, TimeSpan expiry)
    {
        // Uses AtomicUpdate internally — single source of truth for locking
        return await AtomicUpdateAsync(key, expiry, current => current + 1);
    }

    public async Task<long> AtomicUpdateAsync(string key, TimeSpan expiry, Func<long, long> update)
    {
        var semaphore = GetLock(key);

        await semaphore.WaitAsync();

        try
        {
            var currentEntryValue = GetLive(key)?.Value ?? 0L;
            var newValue = update(currentEntryValue);

            // Only set expiry on first write — preserve original window
            // Window opened at T=0, expires at T=60
            // Request at T=30 should NOT reset the window to T=90
            // It should still expire at T=60
            var expiresAt = GetLive(key) is { } existing
                ? existing.ExpiresAt
                : DateTimeOffset.UtcNow.Add(expiry);

            _store[key] = new Entry(newValue, expiresAt);

            return newValue;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task SetAsync(string key, long value, TimeSpan expiry)
    {
        var semaphore = GetLock(key);

        await semaphore.WaitAsync();

        try
        {
            _store[key] = new Entry(value, DateTimeOffset.UtcNow.Add(expiry));
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Task DeleteAsync(string key)
    {
        _store.TryRemove(key, out _);
        
        _locks.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    public Task<bool> IsHealthyAsync() => Task.FromResult(true);

    private SemaphoreSlim GetLock(string key) => _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    private bool IsExpired(Entry entry) => DateTimeOffset.UtcNow >= entry.ExpiresAt;
    private Entry? GetLive(string key)
    {
        if (_store.TryGetValue(key, out var entry) && !IsExpired(entry)) return entry;

        // Lazy cleanup — remove expired entry if found
        if (entry is not null)
            _store.TryRemove(key, out _);

        return null;
    }
}
