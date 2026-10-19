using SelfishNetV2.Core;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>Deterministic token-bucket tests with an injected fake clock (1 tick = 1 ms).</summary>
public class TokenBucketTests
{
    private sealed class FakeClock
    {
        public long Now;
        public long Read() => Now;
    }

    [Fact]
    public void ZeroRate_IsUnlimited()
    {
        var clock = new FakeClock();
        var b = new TokenBucket(clock.Read, 1000);
        Assert.True(b.TryConsume(0, 1_000_000));
        Assert.True(b.TryConsume(0, 1_000_000));
    }

    [Fact]
    public void EnforcesSustainedRate()
    {
        var clock = new FakeClock();
        var b = new TokenBucket(clock.Read, 1000); // 1 tick = 1 ms
        // 1000 B/s → capacity = 50 (50 ms burst). First use: full bucket.
        Assert.True(b.TryConsume(1000, 50));   // drains the 50-token bucket → 0
        Assert.True(b.TryConsume(1000, 1));    // deficit rule: non-negative bucket admits one packet → -1
        Assert.False(b.TryConsume(1000, 1));   // negative → dropped
        // advance 100 ms → +100 tokens, clamped to capacity 50
        clock.Now += 100;
        Assert.True(b.TryConsume(1000, 50));   // → 0
        Assert.True(b.TryConsume(1000, 1));    // deficit → -1
        Assert.False(b.TryConsume(1000, 1));   // dropped
    }

    [Fact]
    public void AllowsSinglePacketDeficit()
    {
        var clock = new FakeClock();
        var b = new TokenBucket(clock.Read, 1000);
        Assert.True(b.TryConsume(1000, 50));   // empty the bucket exactly
        Assert.True(b.TryConsume(1000, 1500)); // deficit: one MTU frame passes, bucket goes negative
        Assert.False(b.TryConsume(1000, 60));  // now negative: dropped
        clock.Now += 2000;                     // +2000 tokens → -1550+2000 clamped to 50
        Assert.True(b.TryConsume(1000, 50));
    }

    [Fact]
    public void CapChangeClampsButNeverRefills()
    {
        var clock = new FakeClock();
        var b = new TokenBucket(clock.Read, 1000);
        Assert.True(b.TryConsume(1000, 50));   // empty
        Assert.True(b.TryConsume(1000, 100));  // deficit → -100
        // raising the cap must NOT refill the bucket:
        Assert.False(b.TryConsume(10000, 1));  // still negative (capacity grew, tokens unchanged)
        clock.Now += 20;                        // +200 at new rate → 100 → within capacity 500
        Assert.True(b.TryConsume(10000, 60));
    }

    [Fact]
    public void RefillNeverExceedsCapacity()
    {
        var clock = new FakeClock();
        var b = new TokenBucket(clock.Read, 1000);
        Assert.True(b.TryConsume(1000, 10));   // 40 left
        clock.Now += 60_000;                    // a minute idle
        Assert.True(b.TryConsume(1000, 50));   // capped at 50, not 60k
        Assert.True(b.TryConsume(1000, 1));    // deficit → -1
        Assert.False(b.TryConsume(1000, 1));   // now negative → dropped
    }
}
