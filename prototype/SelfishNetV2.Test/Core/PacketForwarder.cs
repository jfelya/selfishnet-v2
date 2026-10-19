// FREE MODEL TEST PROTOTYPE — PacketForwarder (redirector + token-bucket shaping).
// Original directions preserved:
//   Download (router→victim): dst IP matches victim → rewrite dstMAC=victimMAC, srcMAC=localMAC.
//   Upload   (victim→router): src MAC matches victim → rewrite dstMAC=routerMAC, srcMAC=localMAC.
// Drops when Blocked (redirect==false) or token bucket denies (cap exceeded).

using System.Net;
using System.Runtime.InteropServices;

namespace SelfishNetV2.Test.Core;

public sealed class PacketForwarder : IDisposable
{
    private readonly DeviceManager _devices;
    private readonly ArpEngine _arp;
    private IntPtr _handle = IntPtr.Zero;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public bool Running { get; private set; }
    public event Action<string>? Error;

    // Stats for status bar.
    public long ForwardedDown { get; private set; }
    public long ForwardedUp { get; private set; }
    public long Dropped { get; private set; }

    public PacketForwarder(DeviceManager devices, ArpEngine arp)
    {
        _devices = devices;
        _arp = arp;
    }

    public bool Start(AdapterInfo adapter)
    {
        Stop();
        _handle = NpcapInterop.OpenLive(adapter.PcapName, "ip", out string err);
        if (_handle == IntPtr.Zero) { Error?.Invoke(err); return false; }
        Running = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(() => Loop(adapter, ct), ct);
        return true;
    }

    private void Loop(AdapterInfo adapter, CancellationToken ct)
    {
        IntPtr hdr = IntPtr.Zero, data = IntPtr.Zero;
        byte[] localMac = adapter.Mac.GetAddressBytes();
        byte[] localIp = adapter.LocalIp.GetAddressBytes();
        while (!ct.IsCancellationRequested && _handle != IntPtr.Zero)
        {
            int rc;
            try { rc = NpcapInterop.pcap_next_ex(_handle, ref hdr, ref data); }
            catch { break; }
            if (rc != 1) continue;
            try
            {
                var h = Marshal.PtrToStructure<NpcapInterop.PcapPkthdr>(hdr);
                int len = (int)Math.Min(h.caplen, h.len);
                if (len < 34) continue; // need eth(14) + minimal IP src/dst offsets
                var frame = new byte[len];
                Marshal.Copy(data, frame, 0, len);

                // V_4 = Ethernet source MAC (offsets 6..11). The "ip" capture
                // filter already guarantees IPv4; like the original, no
                // EtherType check is done here.
                var srcMac = new System.Net.NetworkInformation.PhysicalAddress(frame[6..12]);
                var srcIp = new IPAddress(frame[26..30]);
                var dstIp = new IPAddress(frame[30..34]);
                byte[] routerMac = RouterMacBytes(adapter);

                // Branch 1: our own transmissions (original counts them on the
                // local row instead of forwarding them back out).
                if (srcMac.ToString() == adapter.Mac.ToString())
                {
                    if (srcIp.Equals(adapter.LocalIp) &&
                        _devices.TryGetByIp(adapter.LocalIp, out var self) && self != null)
                        self.AddUp(len);
                    continue;
                }

                // Branch 2: router-sourced (Download). Matches by SOURCE MAC so
                // it works poisoned (dst MAC = us) and unpoisoned alike.
                if (srcMac.ToString() == new System.Net.NetworkInformation.PhysicalAddress(routerMac).ToString())
                {
                    if (dstIp.Equals(adapter.LocalIp))
                    {
                        if (_devices.TryGetByIp(adapter.LocalIp, out var selfDown) && selfDown != null)
                            selfDown.AddDown(len);
                        continue;
                    }
                    if (!_devices.TryGetByIp(dstIp, out var downDev) || downDev == null ||
                        downDev.IsLocalPc || downDev.IsGateway)
                        continue;
                    downDev.AddDown(len);
                    if (!downDev.Redirect) { Dropped++; continue; }
                    if (!downDev.DownBucket.TryConsume(downDev.CapDownBytesPerSec, len)) { Dropped++; continue; }
                    RewriteMacs(frame, localMac, downDev.Mac.GetAddressBytes());
                    NpcapInterop.pcap_sendpacket(_handle, frame, len);
                    ForwardedDown++;
                    continue;
                }

                // Branch 3: LAN-device-sourced (Upload). Traffic addressed to us
                // is never forwarded.
                if (dstIp.Equals(adapter.LocalIp))
                    continue;
                Device? upDev = _devices.GetByMac(srcMac);
                if (upDev == null || upDev.IsLocalPc || upDev.IsGateway)
                    continue;
                upDev.AddUp(len);
                if (!upDev.Redirect) { Dropped++; continue; }
                if (!upDev.UpBucket.TryConsume(upDev.CapUpBytesPerSec, len)) { Dropped++; continue; }
                RewriteMacs(frame, localMac, routerMac);
                NpcapInterop.pcap_sendpacket(_handle, frame, len);
                ForwardedUp++;
            }
            catch { /* per-packet errors are non-fatal */ }
        }
    }

    private byte[] RouterMacBytes(AdapterInfo adapter)
    {
        if (adapter.GatewayIp != null && _devices.TryGetByIp(adapter.GatewayIp, out var gw) && gw != null)
            return gw.Mac.GetAddressBytes();
        return new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    }

    private static void RewriteMacs(byte[] frame, byte[] srcMac, byte[] dstMac)
    {
        Buffer.BlockCopy(dstMac, 0, frame, 0, 6);
        Buffer.BlockCopy(srcMac, 0, frame, 6, 6);
    }

    public void Stop()
    {
        Running = false;
        try { _cts?.Cancel(); } catch { }
        if (_handle != IntPtr.Zero)
        {
            try { NpcapInterop.pcap_close(_handle); } catch { }
            _handle = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();
}
