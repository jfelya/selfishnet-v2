using System.Net;

namespace SelfishNetV2.Core;

/// <summary>Background reverse-DNS hostname resolution. Never blocks capture threads.</summary>
public static class HostResolver
{
    public static async Task<string> ResolveAsync(IPAddress ip, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1500);
            var entry = await Dns.GetHostEntryAsync(ip).WaitAsync(cts.Token).ConfigureAwait(false);
            string name = entry.HostName.Split('.')[0];
            if (!string.IsNullOrWhiteSpace(name) && name != ip.ToString())
                return name;
        }
        catch { /* unresolved is fine */ }
        return ip.ToString();
    }
}
