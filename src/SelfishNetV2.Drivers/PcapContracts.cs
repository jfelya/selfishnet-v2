namespace SelfishNetV2.Drivers;

/// <summary>A capture-capable device reported by the packet driver.</summary>
public sealed record PcapDeviceInfo(string Name);

public enum ReceiveResult
{
    /// <summary>A packet is available via the out view.</summary>
    Packet,
    /// <summary>Read timeout elapsed with no packet (normal; loop again).</summary>
    Timeout,
    /// <summary>Driver-level error (see <see cref="IPcapCapture.LastError"/>).</summary>
    Error,
    /// <summary>The capture was broken via <see cref="IPcapCapture.BreakLoop"/> (shutdown path).</summary>
    Broken,
}

/// <summary>
/// A zero-copy view over a captured frame. The memory is owned by the driver and is
/// valid ONLY until the next <see cref="IPcapCapture.Receive"/> call on the same capture.
/// Copy it if you must keep it.
/// </summary>
public readonly ref struct PacketView
{
    public ReadOnlySpan<byte> Data { get; }

    public PacketView(ReadOnlySpan<byte> data) => Data = data;
}

/// <summary>One open capture handle. Threading contract: Receive is called from the owning
/// session's thread only; Send and BreakLoop may be called from any thread.</summary>
public interface IPcapCapture : IDisposable
{
    string DeviceName { get; }
    string LastError { get; }
    /// <summary>0 on success, -1 on failure (see <see cref="LastError"/>).</summary>
    int Send(ReadOnlySpan<byte> frame);
    ReceiveResult Receive(out PacketView view);
    /// <summary>Unblocks a pending/next Receive with <see cref="ReceiveResult.Broken"/>. Safe from any thread.</summary>
    void BreakLoop();
    /// <summary>Sets the kernel wakeup threshold (Npcap/WinPcap extension). No-op when unsupported.</summary>
    void TrySetMinToCopy(int bytes);
}

/// <summary>Packet driver abstraction (wpcap-compatible). Test seam: LoopbackDriver.</summary>
public interface IPcapDriver
{
    string BackendName { get; }
    IReadOnlyList<PcapDeviceInfo> EnumerateDevices();
    /// <summary>Opens a live capture with the given BPF filter. Throws <see cref="PcapException"/> on failure.</summary>
    IPcapCapture OpenLive(string deviceName, string? filter, int snaplen = 65535, int promisc = 0, int timeoutMs = 20);
}

public sealed class PcapException : Exception
{
    public PcapException(string message) : base(message) { }
}

/// <summary>Packet handler for <see cref="PcapCaptureSession"/>. Span is valid for the duration of the call only.</summary>
public delegate void PacketHandler(ReadOnlySpan<byte> frame);
