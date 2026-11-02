using SelfishNetV2.App.ViewModels;
using SelfishNetV2.Core;
using SelfishNetV2.Drivers;
using SelfishNetV2.Engines;
using SelfishNetV2.Services;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>UI state must be derived from the catalog, not from stale initial text.</summary>
public class ViewModelStateTests
{
    [Fact]
    public void DeviceViewModel_ShowsSavedCaps_OnConstruction()
    {
        var host = new MainViewModel(null, new AdapterService(null), new SettingsStore(), driverProblem: null);
        var device = new Device(0xC0A80164, 0xAABBCCDDEE01)
        {
            CapDownBytesPerSec = 102_400,
            CapUpBytesPerSec = 204_800,
        };

        var vm = new DeviceViewModel(device, host);

        Assert.Equal("100", vm.CapDownText);
        Assert.Equal("200", vm.CapUpText);
    }

    [Fact]
    public void DeviceViewModel_ShowsSubUnitCap_AndPreservesItOnUnchangedCommit()
    {
        var orchestrator = new EngineOrchestrator(
            new LoopbackDriver(), KnownDevicesStore.InMemory(), EngineKind.Modern);
        try
        {
            var host = new MainViewModel(orchestrator, new AdapterService(null),
                new SettingsStore(), driverProblem: null);
            var device = new Device(0xC0A80164, 0xAABBCCDDEE01) { CapDownBytesPerSec = 500 };
            var vm = new DeviceViewModel(device, host);

            Assert.Equal("<1", vm.CapDownText);

            vm.CapDownText = "<1"; // committing the unchanged display must not clear the cap
            Assert.Equal(500, device.CapDownBytesPerSec);
        }
        finally
        {
            orchestrator.Dispose();
        }
    }

    [Fact]
    public void HugeNumericCap_ClampsInsteadOfBecomingUnlimited()
    {
        var orchestrator = new EngineOrchestrator(
            new LoopbackDriver(), KnownDevicesStore.InMemory(), EngineKind.Modern);
        try
        {
            var host = new MainViewModel(orchestrator, new AdapterService(null),
                new SettingsStore(), driverProblem: null);
            var device = new Device(0xC0A80164, 0xAABBCCDDEE01);
            var vm = new DeviceViewModel(device, host);

            vm.CapDownText = new string('9', 40);

            Assert.Equal(int.MaxValue, device.CapDownBytesPerSec);
        }
        finally
        {
            orchestrator.Dispose();
        }
    }
}
