using SelfishNetV2.App.ViewModels;
using SelfishNetV2.Core;
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
}
