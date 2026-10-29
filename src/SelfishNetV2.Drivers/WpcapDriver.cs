using System.Runtime.InteropServices;

namespace SelfishNetV2.Drivers;

/// <summary>IPcapDriver over a dynamically loaded wpcap.dll (Npcap or WinPcap).</summary>
public sealed class WpcapDriver : IPcapDriver
{
    private readonly WpcapNative.Api _api;
    public string BackendName { get; }

    internal WpcapDriver(WpcapNative.Api api, string backendName)
    {
        _api = api;
        BackendName = backendName;
    }

    public unsafe IReadOnlyList<PcapDeviceInfo> EnumerateDevices()
    {
        var result = new List<PcapDeviceInfo>();
        IntPtr all = IntPtr.Zero;
        byte* err = stackalloc byte[256];
        try
        {
            if (_api.FindAllDevs(&all, err) != 0 || all == IntPtr.Zero)
                return result;
            IntPtr cur = all;
            while (cur != IntPtr.Zero)
            {
                // pcap_if_t: next | name* | description* | addresses* | flags (offsets scale with IntPtr.Size)
                IntPtr next = Marshal.ReadIntPtr(cur, 0);
                IntPtr namePtr = Marshal.ReadIntPtr(cur, IntPtr.Size);
                string name = Marshal.PtrToStringAnsi(namePtr) ?? "";
                if (name.Length > 0) result.Add(new PcapDeviceInfo(name));
                cur = next;
            }
        }
        finally
        {
            if (all != IntPtr.Zero) _api.FreeAllDevs(all);
        }
        return result;
    }

    public IPcapCapture OpenLive(string deviceName, string? filter, int snaplen = 65535, int promisc = 0, int timeoutMs = 20)
        => WpcapCapture.Open(_api, deviceName, filter, snaplen, promisc, timeoutMs);
}

internal sealed unsafe class WpcapCapture : IPcapCapture
{
    private readonly WpcapNative.Api _api;
    private IntPtr _handle;
    private readonly object _sendGate = new();
    private string _lastError = "";
    private bool _disposed;

    private WpcapCapture(WpcapNative.Api api, IntPtr handle, string deviceName)
    {
        _api = api;
        _handle = handle;
        DeviceName = deviceName;
    }

    public string DeviceName { get; }
    public string LastError => _lastError;

    internal static WpcapCapture Open(WpcapNative.Api api, string deviceName, string? filter,
        int snaplen, int promisc, int timeoutMs)
    {
        byte* err = stackalloc byte[256];
        byte[] nameBytes = WpcapNative.ToAnsi(deviceName);
        IntPtr handle;
        fixed (byte* name = nameBytes)
            handle = api.OpenLive(name, snaplen, promisc, timeoutMs, err);
        if (handle == IntPtr.Zero)
            throw new PcapException($"pcap_open_live({deviceName}) failed: {WpcapNative.PtrToStringSafe(err)}");

        var cap = new WpcapCapture(api, handle, deviceName);
        if (!string.IsNullOrWhiteSpace(filter) && !cap.ApplyFilter(filter))
        {
            string msg = cap.LastError;
            cap.Dispose();
            throw new PcapException($"pcap_setfilter(\"{filter}\") failed: {msg}");
        }
        return cap;
    }

    private bool ApplyFilter(string filter)
    {
        byte[] filterBytes = WpcapNative.ToAnsi(filter);
        BpfProgram prog = default;
        fixed (byte* f = filterBytes)
        {
            if (_api.Compile(_handle, &prog, f, 1, 0xFFFFFF00) != 0)
            {
                _lastError = ReadError();
                return false;
            }
        }
        try
        {
            if (_api.SetFilter(_handle, &prog) != 0)
            {
                _lastError = ReadError();
                return false;
            }
            return true;
        }
        finally
        {
            _api.FreeCode(&prog);
        }
    }

    public int Send(ReadOnlySpan<byte> frame)
    {
        if (_disposed || _handle == IntPtr.Zero) return -1;
        int rc;
        fixed (byte* p = frame)
        {
            lock (_sendGate) // serialize concurrent sends on this handle
                rc = _api.SendPacket(_handle, p, frame.Length);
        }
        if (rc != 0) _lastError = ReadError(); // keep the reason for diagnostics
        return rc;
    }

    public ReceiveResult Receive(out PacketView view)
    {
        view = default;
        if (_disposed || _handle == IntPtr.Zero) return ReceiveResult.Broken;
        IntPtr hdr = IntPtr.Zero, data = IntPtr.Zero;
        int rc = _api.NextEx(_handle, &hdr, &data);
        switch (rc)
        {
            case 1:
                var h = new ReadOnlySpan<byte>((void*)hdr, 16);
                uint capLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(8, 4));
                uint len = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(12, 4));
                int n = (int)Math.Min(capLen, len);
                if (n < 0 || data == IntPtr.Zero) { _lastError = "malformed pcap header"; return ReceiveResult.Error; }
                view = new PacketView(new ReadOnlySpan<byte>((void*)data, n));
                return ReceiveResult.Packet;
            case 0:
                return ReceiveResult.Timeout;
            case -2:
                return ReceiveResult.Broken; // pcap_breakloop
            default:
                _lastError = ReadError();
                return ReceiveResult.Error;
        }
    }

    public void BreakLoop()
    {
        if (!_disposed && _handle != IntPtr.Zero)
            _api.BreakLoop(_handle);
    }

    public void TrySetMinToCopy(int bytes)
    {
        if (_api.SetMinToCopy != null && !_disposed && _handle != IntPtr.Zero)
            _ = _api.SetMinToCopy(_handle, bytes);
    }

    private string ReadError()
    {
        try { return WpcapNative.PtrToStringSafe(_api.GetErr(_handle)); }
        catch { return "unknown pcap error"; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Callers (PcapCaptureSession) guarantee the receive thread has been joined BEFORE
        // this runs (breakloop → join → close). Never close under a blocked pcap_next_ex.
        if (_handle != IntPtr.Zero)
        {
            _api.Close(_handle);
            _handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}
