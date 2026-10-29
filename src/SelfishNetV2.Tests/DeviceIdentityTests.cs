using SelfishNetV2.Core;
using SelfishNetV2.Drivers;
using SelfishNetV2.Engines;
using SelfishNetV2.Services;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>MAC changes at one IP: rotation is the same device; a new global MAC is not.</summary>
public class DeviceIdentityTests
{
    private static EngineOrchestrator Create() =>
        new(new LoopbackDriver(), KnownDevicesStore.InMemory(), EngineKind.Modern);

    [Fact]
    public void GlobalMacReplacement_ResetsPolicy()
    {
        using var orch = Create();
        var device = orch.Catalog.AddOrUpdate(0xC0A80164, 0xA8BBCCDDEE01); // global
        device.CapDownBytesPerSec = 100_000;
        device.CapUpBytesPerSec = 50_000;
        device.Redirect = false;
        device.CustomName = "Old tablet";
        device.UserEdited = true;

        orch.Catalog.AddOrUpdate(0xC0A80164, 0x001122334455); // different global device

        Assert.Equal(0, device.CapDownBytesPerSec);
        Assert.Equal(0, device.CapUpBytesPerSec);
        Assert.True(device.Redirect);
        Assert.Null(device.CustomName);
        Assert.False(device.UserEdited);
    }

    [Fact]
    public void LocalMacRotation_KeepsPolicy()
    {
        using var orch = Create();
        var device = orch.Catalog.AddOrUpdate(0xC0A80164, 0x8E4F29BF351C); // randomized
        device.CapDownBytesPerSec = 100_000;
        device.Redirect = false;
        device.CustomName = "Phone";

        orch.Catalog.AddOrUpdate(0xC0A80164, 0x1E335421F882); // rotated randomized MAC

        Assert.Equal(100_000, device.CapDownBytesPerSec);
        Assert.False(device.Redirect);
        Assert.Equal("Phone", device.CustomName);
    }
}
