using FluentAssertions;
using Rater.Core.Algorithms;
using Rater.Core.Configuration;
using Rater.Core.Storage;

namespace Rater.Core.Tests.Algorithms;

public class FixedWindowAlgorithmTests
{
    private static FixedWindowAlgorithm Sut() => new();

    private static InMemoryStorageProvider Storage() => new();

    private static RateLimitRule Rule(int limit = 5, int windowSeconds = 60) => new()
    {
        Name = "test-rule",
        Algorithm = RaterAlgorithm.FixedWindow,
        Limit = limit,
        WindowSeconds = windowSeconds,
        KeyStrategy = RaterKeyStrategy.ClientId,
    };

    // Allow
    [Fact]
    public async Task First_Request_Should_Be_Allowed()
    {
        var decision = await Sut().IsAllowedAsync("key:1", Rule(), Storage());

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Request_At_Exactly_The_Limit_Should_Be_Allowed()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 5);

        // Fire exactly 5 — all should be allowed
        for (var i = 0; i < 5; i++)
        {
            var d = await sut.IsAllowedAsync("key:2", rule, storage);
            d.Allowed.Should().BeTrue(because: $"request {i + 1} of {rule.Limit} should be allowed");
        }
    }

    [Fact]
    public async Task Remaining_Decrements_With_Each_Request()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 3);

        var first = await sut.IsAllowedAsync("key:3", rule, storage);
        var second = await sut.IsAllowedAsync("key:3", rule, storage);
        var third = await sut.IsAllowedAsync("key:3", rule, storage);

        first.Remaining.Should().Be(2);
        second.Remaining.Should().Be(1);
        third.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task Different_Keys_Have_Independent_Counters()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 1);

        // Exhaust key A
        await sut.IsAllowedAsync("key:A", rule, storage);
        var denyA = await sut.IsAllowedAsync("key:A", rule, storage);

        // Key B should be unaffected
        var allowB = await sut.IsAllowedAsync("key:B", rule, storage);

        denyA.Allowed.Should().BeFalse();
        allowB.Allowed.Should().BeTrue();
    }

    // Deny
    [Fact]
    public async Task Request_Over_Limit_Should_Be_Denied()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 3);

        // Exhaust limit
        for (var i = 0; i < 3; i++)
            await sut.IsAllowedAsync("key:4", rule, storage);

        // 4th request — over limit
        var decision = await sut.IsAllowedAsync("key:4", rule, storage);

        decision.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Denied_Decision_Has_Zero_Remaining()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 1);

        await sut.IsAllowedAsync("key:5", rule, storage);
        var denied = await sut.IsAllowedAsync("key:5", rule, storage);

        denied.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task Denied_Decision_Has_RetryAfter_Set()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 1, windowSeconds: 60);

        await sut.IsAllowedAsync("key:6", rule, storage);
        var denied = await sut.IsAllowedAsync("key:6", rule, storage);

        denied.RetryAfterSeconds.Should().BeGreaterThan(0);
        denied.RetryAfterSeconds.Should().BeLessThanOrEqualTo(60);
    }

    [Fact]
    public async Task Denied_Decision_Has_ResetAt_In_The_Future()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 1);

        await sut.IsAllowedAsync("key:7", rule, storage);
        var denied = await sut.IsAllowedAsync("key:7", rule, storage);

        denied.ResetAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    // Limit edge cases
    [Fact]
    public async Task Limit_Of_One_Only_Allows_First_Request()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 1);

        var first = await sut.IsAllowedAsync("key:8", rule, storage);
        var second = await sut.IsAllowedAsync("key:8", rule, storage);

        first.Allowed.Should().BeTrue();
        second.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Requests_After_Window_Expires_Are_Allowed_Again()
    {
        var storage = Storage();
        var sut = Sut();

        // Very short window — 1 second
        var rule = Rule(limit: 1, windowSeconds: 1);

        var first = await sut.IsAllowedAsync("key:9", rule, storage);
        var overLimit = await sut.IsAllowedAsync("key:9", rule, storage);

        // Wait for window to expire
        await Task.Delay(TimeSpan.FromSeconds(1.1));

        var afterReset = await sut.IsAllowedAsync("key:9", rule, storage);

        first.Allowed.Should().BeTrue();
        overLimit.Allowed.Should().BeFalse();
        afterReset.Allowed.Should().BeTrue(because: "window expired and counter reset");
    }

    [Fact]
    public async Task Decision_Contains_Correct_Rule_Name()
    {
        var rule = Rule();
        rule.Name = "my-special-rule";
        var decision = await Sut().IsAllowedAsync("key:10", rule, Storage());

        decision.RuleName.Should().Be("my-special-rule");
    }
}
