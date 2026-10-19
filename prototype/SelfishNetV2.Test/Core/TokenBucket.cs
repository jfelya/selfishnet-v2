// FREE MODEL TEST PROTOTYPE — TokenBucket (smooth shaping, roadmap §3.1).
// tokens = min(capacity, tokens + rate * dt). 0 rate = unlimited.

namespace SelfishNetV2.Test.Core;

public sealed class TokenBucket
{
    private readonly object _lock = new();
    private double _tokens;
    private long _lastTicks = DateTime.UtcNow.Ticks;
    private int _rateBytesPerSec = -1; // cache to detect cap changes

    /// <returns>true if <paramref name="bytes"/> may be forwarded, else drop/queue.</returns>
    public bool TryConsume(int bytesPerSecCap, int bytes)
    {
        if (bytesPerSecCap <= 0) return true; // 0 = unlimited
        lock (_lock)
        {
            long now = DateTime.UtcNow.Ticks;
            if (_rateBytesPerSec != bytesPerSecCap)
            {
                _rateBytesPerSec = bytesPerSecCap;
                _tokens = bytesPerSecCap; // full bucket on cap change
                _lastTicks = now;
            }
            double dt = (now - _lastTicks) / (double)TimeSpan.TicksPerSecond;
            _lastTicks = now;
            _tokens = Math.Min(bytesPerSecCap, _tokens + bytesPerSecCap * dt);
            if (bytes <= _tokens)
            {
                _tokens -= bytes;
                return true;
            }
            return false;
        }
    }
}
