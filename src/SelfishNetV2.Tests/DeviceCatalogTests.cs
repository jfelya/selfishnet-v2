using SelfishNetV2.Core;
using Xunit;

namespace SelfishNetV2.Tests;

public class DeviceCatalogTests
{
    [Fact]
    public void Add_RaisesAdded_Once_PerIp()
    {
        var c = new DeviceCatalog();
        int added = 0;
        c.DeviceAdded += _ => added++;
        c.AddOrUpdate(0xC0A80164, 0xAABBCCDDEE01);
        c.AddOrUpdate(0xC0A80164, 0xAABBCCDDEE01); // same device again
        Assert.Equal(1, added);
        Assert.Equal(1, c.Count);
    }

    [Fact]
    public void RepeatedUnchangedDevice_DoesNotRaiseUpdated()
    {
        var c = new DeviceCatalog();
        int added = 0, updated = 0;
        c.DeviceAdded += _ => added++;
        c.DeviceUpdated += _ => updated++;

        c.AddOrUpdate(0xC0A80164, 0xAABBCCDDEE01);
        c.AddOrUpdate(0xC0A80164, 0xAABBCCDDEE01);

        Assert.Equal(1, added);
        Assert.Equal(0, updated); // liveness still updates, UI identity does not
    }

    [Fact]
    public void MacLearn_Reindexes_FromZeroPlaceholder()
    {
        var c = new DeviceCatalog();
        c.AddOrUpdate(0xC0A80101, 0, isGateway: true);            // gateway placeholder, MAC unknown
        Assert.False(c.TryGetByMac(0x5CAA00112233, out _));
        c.AddOrUpdate(0xC0A80101, 0x5CAA00112233, isGateway: true); // learned
        Assert.True(c.TryGetByMac(0x5CAA00112233, out var gw));
        Assert.True(gw.IsGateway);
        Assert.True(gw.HasKnownMac);
    }

    [Fact]
    public void MacChange_KeepsSingleIndexEntry()
    {
        var c = new DeviceCatalog();
        c.AddOrUpdate(0xC0A80164, 0xAAAAAAAAAAAA);
        c.AddOrUpdate(0xC0A80164, 0xBBBBBBBBBBBB); // device got a new NIC/MAC
        Assert.False(c.TryGetByMac(0xAAAAAAAAAAAA, out _));       // stale index purged
        Assert.True(c.TryGetByMac(0xBBBBBBBBBBBB, out _));
        Assert.Equal(1, c.Count);
    }

    [Fact]
    public void Snapshot_IsSortedByIp()
    {
        var c = new DeviceCatalog();
        c.AddOrUpdate(0xC0A80164, 1);
        c.AddOrUpdate(0xC0A80101, 2);
        c.AddOrUpdate(0xC0A80132, 3);
        var ips = c.Snapshot().Select(d => d.IpKey).ToArray();
        Assert.Equal(new uint[] { 0xC0A80101, 0xC0A80132, 0xC0A80164 }, ips);
    }

    [Fact]
    public void Clear_RaisesRemovedForEveryDevice()
    {
        var c = new DeviceCatalog();
        var removed = new List<uint>();
        c.DeviceRemoved += d => removed.Add(d.IpKey);
        c.AddOrUpdate(0xC0A80164, 0xAABBCCDDEE01);
        c.AddOrUpdate(0xC0A80165, 0xAABBCCDDEE02);

        c.Clear();

        Assert.Equal(new uint[] { 0xC0A80164, 0xC0A80165 }, removed.OrderBy(x => x).ToArray());
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void Remove_RaisesRemoved_AndPurgesBothIndexes()
    {
        var c = new DeviceCatalog();
        Device? removed = null;
        c.DeviceRemoved += d => removed = d;
        c.AddOrUpdate(0xC0A80164, 0xAABBCCDDEE01);
        Assert.True(c.Remove(0xC0A80164));
        Assert.NotNull(removed);
        Assert.False(c.TryGetByMac(0xAABBCCDDEE01, out _));
        Assert.False(c.TryGetByIp(0xC0A80164, out _));
    }

    [Fact]
    public void SpoofTargets_ExcludeLocalGatewayUnknownMacAndUntargeted()
    {
        var c = new DeviceCatalog();
        c.AddOrUpdate(0xC0A80132, 0x020000000001, isLocal: true);
        c.AddOrUpdate(0xC0A80101, 0, isGateway: true);            // unknown MAC
        c.AddOrUpdate(0xC0A80164, 0xAABBCCDDEE01);                // valid target
        var untargeted = c.AddOrUpdate(0xC0A80165, 0xAABBCCDDEE02);
        untargeted.Targeted = false;
        var targets = c.SpoofTargets();
        Assert.Single(targets);
        Assert.Equal(0xC0A80164u, targets[0].IpKey);
    }
}
