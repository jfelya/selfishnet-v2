// FREE MODEL TEST PROTOTYPE — async hostname resolution (DNS + NetBIOS-ish fallback).

using System.Net;
using System.Net.Sockets;

namespace SelfishNetV2.Test.Core;

public static class HostResolver
{
    public static async Task<string> ResolveAsync(IPAddress ip, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1500);
            var entry = await Dns.GetHostEntryAsync(ip).WaitAsync(cts.Token);
            string name = entry.HostName.Split('.')[0];
            if (!string.IsNullOrWhiteSpace(name) && name != ip.ToString())
                return name;
        }
        catch { /* fall through */ }
        return ip.ToString();
    }

    public static async void ResolveInBackground(Device dev)
    {
        try
        {
            string name = await ResolveAsync(dev.Ip);
            if (string.IsNullOrEmpty(dev.Name) || dev.Name == dev.Ip.ToString())
                dev.Name = name;
        }
        catch { /* keep IP as name */ }
    }
}
