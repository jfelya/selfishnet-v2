using System.Globalization;
using SelfishNetV2.Services;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>Worked examples straight from docs/UNIT_NOTATIONS.md §4.</summary>
public class SpeedFormatterTests
{
    public SpeedFormatterTests()
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(102_400, "100.0 KByte/s")]
    [InlineData(1_048_576, "1.0 MByte/s")]
    [InlineData(12_500_000, "11.9 MByte/s")]
    public void BytesMode_Formats(double bytesPerSec, string expected) =>
        Assert.Equal(expected, SpeedFormatter.FormatBytes(bytesPerSec, UnitMode.Bytes));

    [Theory]
    [InlineData(0, "0 b/s")]
    [InlineData(512, "4.1 KBit/s")]
    [InlineData(102_400, "819.2 KBit/s")]
    [InlineData(1_048_576, "8.4 MBit/s")]
    [InlineData(12_500_000, "100.0 MBit/s")]
    public void BitsMode_Formats(double bytesPerSec, string expected) =>
        Assert.Equal(expected, SpeedFormatter.FormatBytes(bytesPerSec, UnitMode.Bits));

    [Theory]
    [InlineData(0, UnitMode.Bytes, 0)]
    [InlineData(102_400, UnitMode.Bytes, 100)]      // cap 100 KByte/s
    [InlineData(1_048_576, UnitMode.Bytes, 1024)]   // cap 1024 KByte/s
    public void ToCapDisplay_Works(int bytesPerSec, UnitMode mode, decimal expected) =>
        Assert.Equal(expected, SpeedFormatter.ToCapDisplay(bytesPerSec, mode));

    [Fact]
    public void BitsCapDisplay_UsesDecimalNetworkingConvention() =>
        Assert.Equal(819.2m, SpeedFormatter.ToCapDisplay(102_400, UnitMode.Bits));

    [Theory]
    [InlineData(0, UnitMode.Bytes, 0)]
    [InlineData(100, UnitMode.Bytes, 102_400)]
    [InlineData(819, UnitMode.Bits, 102_375)]       // 819 KBit/s → 819*1000/8
    public void FromCapDisplay_Works(decimal display, UnitMode mode, int expected) =>
        Assert.Equal(expected, SpeedFormatter.FromCapDisplay(display, mode));

    [Fact]
    public void FromCapDisplay_ClampsOverflow()
    {
        Assert.Equal(int.MaxValue, SpeedFormatter.FromCapDisplay(decimal.MaxValue, UnitMode.Bytes));
        Assert.Equal(int.MaxValue, SpeedFormatter.FromCapDisplay(decimal.MaxValue, UnitMode.Bits));
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1.0 KByte")]
    [InlineData(1_048_576L, "1.0 MByte")]
    [InlineData(1_073_741_824L, "1.00 GByte")]
    public void FormatTotal_ForCumulativeBytes(long bytes, string expected) =>
        Assert.Equal(expected, SpeedFormatter.FormatTotal(bytes));

    [Fact]
    public void UnitToggle_RoundTrip_DriftBelowTolerance()
    {
        // 100 KByte/s → 819.2 KBit/s → shown as 819 → back to bytes: 102375/1024 ≈ 99.97 (<0.1% drift)
        int original = 102_400;
        decimal asBits = SpeedFormatter.ToCapDisplay(original, UnitMode.Bits);
        int backToBytes = SpeedFormatter.FromCapDisplay(Math.Round(asBits), UnitMode.Bits);
        Assert.InRange(Math.Abs(backToBytes - original) / (double)original, 0, 0.001);
    }
}
