using SelfishNetV2.Core;
using SelfishNetV2.Services;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>
/// Device identity is more fragile than it looks: phones rotate their Wi-Fi MAC
/// (private address) and DHCP moves IPs. Settings must survive that, otherwise a
/// capped device looks like a brand-new, unlimited one every session.
/// </summary>
public class KnownDevicesStoreTests
{
    private static Device Make(uint ip, ulong mac, string name = "") => new(ip, mac) { Name = name };

    [Fact]
    public void ExactMac_AppliesNameCapsAndBlock()
    {
        var store = KnownDevicesStore.InMemory();
        var original = Make(0xC0A80164, 0xAABBCCDDEE01, "Kids tablet");
        original.CustomName = "Tablet";
        original.CapDownBytesPerSec = 256_000;
        original.CapUpBytesPerSec = 128_000;
        original.Redirect = false;
        store.Update(original);

        var fresh = Make(0xC0A80164, 0xAABBCCDDEE01);
        Assert.True(store.ApplyTo(fresh));
        Assert.Equal("Tablet", fresh.CustomName);
        Assert.Equal(256_000, fresh.CapDownBytesPerSec);
        Assert.Equal(128_000, fresh.CapUpBytesPerSec);
        Assert.False(fresh.Redirect);
        // Spoofing is unconditional now (the per-device column was removed).
        Assert.True(fresh.Targeted);
        Assert.True(fresh.KnownSettingsApplied);
    }

    [Fact]
    public void NewMacAtSameIp_InheritsSettings()
    {
        var store = KnownDevicesStore.InMemory();
        var phone = Make(0xC0A80164, 0x8E4F29BF351C, "POCO-X6-Pro-5G");
        phone.CapDownBytesPerSec = 256_000;
        store.Update(phone);

        // Android/iOS private address: same device, same IP, brand-new MAC.
        var fresh = Make(0xC0A80164, 0x1E335421F882);

        Assert.True(store.ApplyTo(fresh));
        Assert.Equal(256_000, fresh.CapDownBytesPerSec);
    }

    [Fact]
    public void NewMacAndNewIp_InheritsByHostname()
    {
        var store = KnownDevicesStore.InMemory();
        var phone = Make(0xC0A80164, 0x8E4F29BF351C, "POCO-X6-Pro-5G");
        phone.CapUpBytesPerSec = 128_000;
        store.Update(phone);

        var fresh = Make(0xC0A80165, 0x1E335421F882); // both MAC and IP changed

        Assert.False(store.ApplyTo(fresh));               // no MAC/IP match
        Assert.True(store.TryApplyByName(fresh, "POCO-X6-Pro-5G"));
        Assert.Equal(128_000, fresh.CapUpBytesPerSec);
    }

    [Fact]
    public void NormalMacAtSameIp_DoesNotInherit_OnDhcpReuse()
    {
        var store = KnownDevicesStore.InMemory();
        var oldDevice = Make(0xC0A80164, 0xAABBCCDDEE01);
        oldDevice.CapDownBytesPerSec = 256_000;
        store.Update(oldDevice);

        // 00:... is a globally administered hardware MAC; a different physical
        // device that receives the old IP must not inherit the old settings.
        var fresh = Make(0xC0A80164, 0x001122334455);

        Assert.False(store.ApplyTo(fresh));
        Assert.Equal(0, fresh.CapDownBytesPerSec);
    }

    [Fact]
    public void LocalMac_DoesNotInheritFromGlobalOldEntry()
    {
        var store = KnownDevicesStore.InMemory();
        var oldDevice = Make(0xC0A80164, 0xA8BBCCDDEE01); // globally administered
        oldDevice.CapDownBytesPerSec = 256_000;
        store.Update(oldDevice);

        var fresh = Make(0xC0A80164, 0x1E335421F882); // randomized new MAC, same IP

        Assert.False(store.ApplyTo(fresh));
        Assert.Equal(0, fresh.CapDownBytesPerSec);
    }

    [Fact]
    public void UserEdited_DoesNotInheritByNameLater()
    {
        var store = KnownDevicesStore.InMemory();
        var oldPhone = Make(0xC0A80164, 0x8E4F29BF351C, "POCO-X6-Pro-5G");
        oldPhone.CapUpBytesPerSec = 128_000;
        store.Update(oldPhone);

        var fresh = Make(0xC0A80165, 0x1E335421F882);
        fresh.UserEdited = true; // the user changed something before DNS returned

        Assert.False(store.TryApplyByName(fresh, "POCO-X6-Pro-5G"));
        Assert.Equal(0, fresh.CapUpBytesPerSec);
    }

    [Fact]
    public void SameIp_PrefersEntryThatCarriesSettings()
    {
        var store = KnownDevicesStore.InMemory();
        var empty = Make(0xC0A80164, 0x111111111111);           // discovered, never configured
        store.Update(empty);
        var configured = Make(0xC0A80164, 0x222222222222);
        configured.CapDownBytesPerSec = 1_000;
        store.Update(configured);

        var fresh = Make(0xC0A80164, 0x333333333333);
        Assert.True(store.ApplyTo(fresh));
        Assert.Equal(1_000, fresh.CapDownBytesPerSec);
    }

    [Fact]
    public void TryApplyByName_DoesNotOverrideAlreadyAppliedSettings()
    {
        var store = KnownDevicesStore.InMemory();
        var phone = Make(0xC0A80164, 0x8E4F29BF351C, "POCO-X6-Pro-5G");
        phone.CapDownBytesPerSec = 256_000;
        store.Update(phone);

        var fresh = Make(0xC0A80164, 0x8E4F29BF351C); // exact MAC match
        Assert.True(store.ApplyTo(fresh));
        fresh.CapDownBytesPerSec = 42_000;             // user edits this session

        Assert.False(store.TryApplyByName(fresh, "POCO-X6-Pro-5G"));
        Assert.Equal(42_000, fresh.CapDownBytesPerSec);
    }
}
