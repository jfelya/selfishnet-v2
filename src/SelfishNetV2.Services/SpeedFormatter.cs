namespace SelfishNetV2.Services;

/// <summary>Speed/limit unit system. Storage is ALWAYS bytes/sec — the mode affects
/// display and input only (docs/UNIT_NOTATIONS.md), so switching units can never
/// corrupt limits.</summary>
public enum UnitMode { Bytes, Bits }

public static class SpeedFormatter
{
    public static string FormatBytes(double bytesPerSec, UnitMode mode)
    {
        if (mode == UnitMode.Bytes)
        {
            if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
            double kb = bytesPerSec / 1024;
            if (kb < 1024) return $"{kb:F1} KByte/s";
            double mb = kb / 1024;
            if (mb < 1024) return $"{mb:F1} MByte/s";
            return $"{mb / 1024:F2} GByte/s";
        }
        else
        {
            double bps = bytesPerSec * 8;
            if (bps < 1000) return $"{bps:F0} b/s";
            double kb = bps / 1000;
            if (kb < 1000) return $"{kb:F1} KBit/s";
            double mb = kb / 1000;
            if (mb < 1000) return $"{mb:F1} MBit/s";
            return $"{mb / 1000:F2} GBit/s";
        }
    }

    /// <summary>Stored bytes/sec → value shown in the cap editor (current unit).</summary>
    public static decimal ToCapDisplay(int bytesPerSec, UnitMode mode) =>
        mode == UnitMode.Bytes ? (decimal)bytesPerSec / 1024 : (decimal)bytesPerSec * 8 / 1000;

    /// <summary>Cap editor value (current unit) → stored bytes/sec. 0 = unlimited.</summary>
    public static int FromCapDisplay(decimal display, UnitMode mode)
    {
        if (display <= 0) return 0;
        double v = mode == UnitMode.Bytes ? (double)display * 1024 : (double)display * 1000 / 8;
        return (int)Math.Round(v);
    }

    /// <summary>Short label of the current cap unit, e.g. "KByte/s" / "KBit/s".</summary>
    public static string CapUnit(UnitMode mode) => mode == UnitMode.Bytes ? "KByte/s" : "KBit/s";

    /// <summary>Formats a cumulative byte count (not a rate) for diagnostics.</summary>
    public static string FormatTotal(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:N1} KByte";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:N1} MByte";
        return $"{mb / 1024.0:N2} GByte";
    }

    public static UnitMode Parse(string? s) =>
        string.Equals(s, "Bits", StringComparison.OrdinalIgnoreCase) ? UnitMode.Bits : UnitMode.Bytes;
}
