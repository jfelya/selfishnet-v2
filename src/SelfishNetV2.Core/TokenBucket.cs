using System.Diagnostics;

namespace SelfishNetV2.Core;

/// <summary>
/// Smooth rate limiter: tokens = min(capacity, tokens + rate × Δt); a packet of
/// ≤ tokens bytes may pass. 0 rate = unlimited.
/// Fixes from the prototype: high-resolution clock (Stopwatch) instead of
/// DateTime.UtcNow (~15.6 ms jitter), and changing the cap CLAMPS the bucket
/// instead of refilling it (no full-cap line-rate burst on every edit).
/// Capacity ≥ 65536 so a maximum-size frame can always eventually pass.
/// </summary>
public sealed class TokenBucket
{
    private readonly object _gate = new();
    private readonly Func<long> _now;
    private readonly double _ticksPerSecond;
    private double _tokens;
    private long _last;
    private int _rate = -1;      // -1 = never configured
    private double _capacity;

    /// <summary>Burst allowance as a fraction of a second of traffic (50 ms).</summary>
    public double BurstSeconds { get; set; } = 0.05;

    public TokenBucket(Func<long>? clock = null, double? ticksPerSecond = null)
    {
        _now = clock ?? Stopwatch.GetTimestamp;
        _ticksPerSecond = ticksPerSecond ?? Stopwatch.Frequency;
        _last = _now();
    }

    /// <summary>
    /// Single-packet deficit rule: a packet passes whenever the bucket is non-negative
    /// (the bucket may go negative and "owes" the deficit back at <c>rate</c>). This keeps
    /// very small caps from starving MTU-sized frames while still enforcing the sustained
    /// rate. 0 rate = unlimited.
    /// </summary>
    /// <returns>true when <paramref name="bytes"/> may be forwarded now.</returns>
    public bool TryConsume(int rateBytesPerSec, int bytes)
    {
        if (rateBytesPerSec <= 0) return true; // 0 = unlimited
        lock (_gate)
        {
            long now = _now();
            if (_rate != rateBytesPerSec)
            {
                bool firstUse = _rate < 0;
                _rate = rateBytesPerSec;
                _capacity = CapacityFor(rateBytesPerSec);
                if (firstUse) _tokens = _capacity;                 // full bucket on first use only
                else if (_tokens > _capacity) _tokens = _capacity; // clamp on cap change — never refill
                _last = now;
            }
            double dt = (now - _last) / _ticksPerSecond;
            _last = now;
            _tokens = Math.Min(_capacity, _tokens + _rate * dt);
            if (_tokens >= 0)
            {
                _tokens -= bytes;
                return true;
            }
            return false;
        }
    }

    private double CapacityFor(int rate) => Math.Max(rate * BurstSeconds, 1.0);
}
