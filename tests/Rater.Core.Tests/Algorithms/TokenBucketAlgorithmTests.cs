using FluentAssertions;
using Rater.Core.Algorithms;
using Rater.Core.Configuration;
using Rater.Core.Contracts;
using Rater.Core.Storage;

namespace Rater.Core.Tests.Algorithms;

public class TokenBucketAlgorithmTests
{
    private static TokenBucketAlgorithm Sut() => new();

    private static InMemoryStorageProvider Storage() => new();

    private static RateLimitRule Rule(int limit = 10, int windowSeconds = 60) => new()
    {
        Name = "token-bucket-rule",
        Algorithm = RaterAlgorithm.TokenBucket,
        Limit = limit,
        WindowSeconds = windowSeconds,
        KeyStrategy = RaterKeyStrategy.ClientId,
    };

    // Burst behaviour

    [Fact]
    public async Task Full_Bucket_On_First_Request_Is_Allowed()
    {
        var decision = await Sut().IsAllowedAsync("tb:1", Rule(), Storage());

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Can_Burst_Up_To_Full_Capacity_Immediately()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 5);

        // All 5 should fire immediately — bucket starts full
        for (var i = 0; i < 5; i++)
        {
            var d = await sut.IsAllowedAsync("tb:2", rule, storage);
            d.Allowed.Should().BeTrue(because: $"burst request {i + 1} should be allowed");
        }
    }

    [Fact]
    public async Task Request_Beyond_Capacity_After_Burst_Is_Denied()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 3);

        // Drain bucket
        for (var i = 0; i < 3; i++)
            await sut.IsAllowedAsync("tb:3", rule, storage);

        // One more — bucket empty
        var denied = await sut.IsAllowedAsync("tb:3", rule, storage);

        denied.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Remaining_After_Full_Burst_Is_Zero()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 3);

        RateLimitDecision last = null!;
        for (var i = 0; i < 3; i++)
            last = await sut.IsAllowedAsync("tb:4", rule, storage);

        last.Remaining.Should().Be(0);
    }

    // Refill behaviour

    [Fact]
    public async Task Token_Refills_After_Waiting()
    {
        var storage = Storage();
        var sut = Sut();

        // limit=1, window=1s → refill rate = 1 token per second
        var rule = Rule(limit: 1, windowSeconds: 1);

        var first = await sut.IsAllowedAsync("tb:5", rule, storage);
        var denied = await sut.IsAllowedAsync("tb:5", rule, storage);

        // Wait for 1 token to refill
        await Task.Delay(TimeSpan.FromSeconds(1.1));

        var afterRefill = await sut.IsAllowedAsync("tb:5", rule, storage);

        first.Allowed.Should().BeTrue();
        denied.Allowed.Should().BeFalse();
        afterRefill.Allowed.Should().BeTrue(because: "one token should have refilled");
    }

    [Fact]
    public async Task Tokens_Never_Exceed_Capacity_After_Long_Wait()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 5, windowSeconds: 1);

        // Fire one request to initialize bucket
        await sut.IsAllowedAsync("tb:6", rule, storage);

        // Wait much longer than window — bucket should cap at limit, not overflow
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Should still only be able to fire limit=5 requests, not 15
        var results = new List<bool>();
        for (var i = 0; i < 6; i++)
        {
            var d = await sut.IsAllowedAsync("tb:6", rule, storage);
            results.Add(d.Allowed);
        }

        results.Count(r => r).Should().Be(5, because: "bucket caps at capacity even after long wait");
        results.Last().Should().BeFalse(because: "6th request should be denied");
    }

    [Fact]
    public async Task Partial_Refill_Allows_Proportional_Requests()
    {
        var storage = Storage();
        var sut = Sut();

        // limit=10, window=1s → 10 tokens/sec refill
        var rule = Rule(limit: 10, windowSeconds: 1);

        // Drain bucket
        for (var i = 0; i < 10; i++)
            await sut.IsAllowedAsync("tb:7", rule, storage);

        // Wait for ~half the window to refill ~5 tokens
        await Task.Delay(TimeSpan.FromMilliseconds(600));

        // Should be able to fire at least a few but not all
        var allowCount = 0;
        for (var i = 0; i < 10; i++)
        {
            var d = await sut.IsAllowedAsync("tb:7", rule, storage);
            if (d.Allowed) allowCount++;
        }

        allowCount.Should().BeGreaterThan(0, because: "some tokens should have refilled");
        allowCount.Should().BeLessThan(10, because: "bucket was not fully refilled");
    }

    // Retry-After

    [Fact]
    public async Task Denied_Decision_RetryAfter_Reflects_Refill_Wait()
    {
        var storage = Storage();
        var sut = Sut();

        // limit=1, window=10s → 1 token per 10 seconds
        var rule = Rule(limit: 1, windowSeconds: 10);

        await sut.IsAllowedAsync("tb:8", rule, storage);
        var denied = await sut.IsAllowedAsync("tb:8", rule, storage);

        denied.Allowed.Should().BeFalse();
        // Next token arrives in ~10 seconds
        denied.RetryAfterSeconds.Should().BeGreaterThan(0);
        denied.RetryAfterSeconds.Should().BeLessThanOrEqualTo(10);
    }

    // Different keys

    [Fact]
    public async Task Different_Keys_Have_Independent_Buckets()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 1);

        // Drain key A
        await sut.IsAllowedAsync("tb:A", rule, storage);
        var denyA = await sut.IsAllowedAsync("tb:A", rule, storage);

        // Key B untouched
        var allowB = await sut.IsAllowedAsync("tb:B", rule, storage);

        denyA.Allowed.Should().BeFalse();
        allowB.Allowed.Should().BeTrue();
    }
}
