using FluentAssertions;
using Rater.Core.Algorithms;
using Rater.Core.Configuration;
using Rater.Core.Storage;

namespace Rater.Core.Tests.Algorithms;

public class SlidingWindowCounterAlgorithmTests
{
    private static SlidingWindowCounterAlgorithm Sut() => new();

    private static InMemoryStorageProvider Storage() => new();

    private static RateLimitRule Rule(int limit = 10, int windowSeconds = 60) => new()
    {
        Name = "sliding-window-rule",
        Algorithm = RaterAlgorithm.SlidingWindowCounter,
        Limit = limit,
        WindowSeconds = windowSeconds,
        KeyStrategy = RaterKeyStrategy.ClientId,
    };

    // allow / deny

    [Fact]
    public async Task First_Request_Is_Allowed()
    {
        var decision = await Sut().IsAllowedAsync("sw:1", Rule(), Storage());

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Requests_Within_Limit_Are_All_Allowed()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 5);

        for (var i = 0; i < 5; i++)
        {
            var d = await sut.IsAllowedAsync("sw:2", rule, storage);
            d.Allowed.Should().BeTrue(because: $"request {i + 1} is within limit");
        }
    }

    [Fact]
    public async Task Request_Beyond_Limit_Is_Denied()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 3);

        for (var i = 0; i < 3; i++)
            await sut.IsAllowedAsync("sw:3", rule, storage);

        var denied = await sut.IsAllowedAsync("sw:3", rule, storage);

        denied.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Remaining_Decrements_Correctly()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 3);

        var first = await sut.IsAllowedAsync("sw:4", rule, storage);
        var second = await sut.IsAllowedAsync("sw:4", rule, storage);

        first.Allowed.Should().BeTrue();
        second.Allowed.Should().BeTrue();
        second.Remaining.Should().BeLessThan(first.Remaining);
    }

    // Sliding behaviour — the key advantage over FixedWindow

    [Fact]
    public async Task Old_Requests_Outside_Window_No_Longer_Count()
    {
        var storage = Storage();
        var sut = Sut();

        // Short window for fast testing
        var rule = Rule(limit: 3, windowSeconds: 2);

        // Exhaust limit
        for (var i = 0; i < 3; i++)
            await sut.IsAllowedAsync("sw:5", rule, storage);

        var denied = await sut.IsAllowedAsync("sw:5", rule, storage);
        denied.Allowed.Should().BeFalse(because: "limit exhausted");

        // Wait for full window to pass — old slots fall out
        await Task.Delay(TimeSpan.FromSeconds(2.2));

        var afterSlide = await sut.IsAllowedAsync("sw:5", rule, storage);
        afterSlide.Allowed.Should().BeTrue(because: "old requests slid out of window");
    }

    [Fact]
    public async Task Boundary_Attack_Is_Mitigated()
    {
        // FixedWindow vulnerability:
        //   99 at T=0:59 + 99 at T=1:01 = 198 requests in 2 seconds
        //   despite limit of 100/min
        //
        // SlidingWindow defence:
        //   Requests near boundary are weighted — old slot counts partially
        //   Total weighted count stays close to limit

        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 10, windowSeconds: 2);

        // Fill half the limit at T=0
        for (var i = 0; i < 5; i++)
            await sut.IsAllowedAsync("sw:6", rule, storage);

        // Wait for almost the full window — old slot now partially weighted
        await Task.Delay(TimeSpan.FromSeconds(1.8));

        // Fire the rest — sliding window will partially count old requests
        // Total allowed should not significantly exceed limit
        var allowedAfterSlide = 0;
        for (var i = 0; i < 10; i++)
        {
            var d = await sut.IsAllowedAsync("sw:6", rule, storage);
            if (d.Allowed) allowedAfterSlide++;
        }

        // SlidingWindow won't allow the full 10 because old requests
        // still partially count in the weighted sum
        allowedAfterSlide.Should().BeLessThan(10,
            because: "sliding window partial weighting limits boundary burst");
    }

    // Window expiry

    [Fact]
    public async Task Counter_Resets_After_Full_Window_Passes()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 2, windowSeconds: 1);

        // Exhaust
        await sut.IsAllowedAsync("sw:7", rule, storage);
        await sut.IsAllowedAsync("sw:7", rule, storage);
        var denied = await sut.IsAllowedAsync("sw:7", rule, storage);

        await Task.Delay(TimeSpan.FromSeconds(1.2));

        var afterExpiry = await sut.IsAllowedAsync("sw:7", rule, storage);

        denied.Allowed.Should().BeFalse();
        afterExpiry.Allowed.Should().BeTrue();
    }

    // Independence

    [Fact]
    public async Task Different_Keys_Are_Completely_Independent()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 1);

        await sut.IsAllowedAsync("sw:A", rule, storage);
        var deniedA = await sut.IsAllowedAsync("sw:A", rule, storage);
        var allowedB = await sut.IsAllowedAsync("sw:B", rule, storage);

        deniedA.Allowed.Should().BeFalse();
        allowedB.Should().NotBeNull();
        allowedB.Allowed.Should().BeTrue();
    }

    // Decision metadata

    [Fact]
    public async Task Denied_Decision_Has_ResetAt_In_Future()
    {
        var storage = Storage();
        var sut = Sut();
        var rule = Rule(limit: 1);

        await sut.IsAllowedAsync("sw:8", rule, storage);
        var denied = await sut.IsAllowedAsync("sw:8", rule, storage);

        denied.ResetAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Allowed_Decision_Contains_Correct_Rule_Name()
    {
        var rule = Rule();
        rule.Name = "sliding-named-rule";

        var decision = await Sut().IsAllowedAsync("sw:9", rule, Storage());

        decision.RuleName.Should().Be("sliding-named-rule");
    }
}