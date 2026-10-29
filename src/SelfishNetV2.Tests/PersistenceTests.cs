using System.IO;
using SelfishNetV2.Core;
using SelfishNetV2.Services;
using Xunit;

namespace SelfishNetV2.Tests;

public class PersistenceTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "SelfishNetV2Tests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static Device Make(uint ip, ulong mac, int down = 0) =>
        new(ip, mac) { CapDownBytesPerSec = down };

    [Fact]
    public void Load_TruncatedJson_StartsEmpty_WithoutThrowing()
    {
        using var temp = new TempDir();
        string path = System.IO.Path.Combine(temp.Path, "known_devices.json");
        File.WriteAllText(path, "{\"AABBCCDDEE01\":");

        var store = KnownDevicesStore.Load(path);

        Assert.False(store.ApplyTo(Make(0xC0A80164, 0xAABBCCDDEE01)));
    }

    [Fact]
    public void Load_CorruptMain_FallsBackToBackup()
    {
        using var temp = new TempDir();
        string path = System.IO.Path.Combine(temp.Path, "known_devices.json");
        var store = KnownDevicesStore.Load(path);
        var device = Make(0xC0A80164, 0xAABBCCDDEE01, 102_400);
        store.Update(device);
        device.CapDownBytesPerSec = 204_800;
        store.Update(device); // .bak now holds the 102_400 version

        File.WriteAllText(path, "{broken");

        var reloaded = KnownDevicesStore.Load(path);
        var fresh = Make(0xC0A80164, 0xAABBCCDDEE01);
        Assert.True(reloaded.ApplyTo(fresh));
        Assert.Equal(102_400, fresh.CapDownBytesPerSec);
    }

    [Fact]
    public void Save_ReplacesAtomically_AndLeavesBackup()
    {
        using var temp = new TempDir();
        string path = System.IO.Path.Combine(temp.Path, "known_devices.json");
        var store = KnownDevicesStore.Load(path);
        var device = Make(0xC0A80164, 0xAABBCCDDEE01, 102_400);

        store.Update(device);
        device.CapDownBytesPerSec = 204_800;
        store.Update(device);

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".bak"));
        var reloaded = KnownDevicesStore.Load(path);
        Assert.True(reloaded.ApplyTo(Make(0xC0A80164, 0xAABBCCDDEE01)));
    }

    [Fact]
    public void Settings_Save_ReplacesAtomically_AndLeavesBackup()
    {
        using var temp = new TempDir();
        string path = System.IO.Path.Combine(temp.Path, "settings.json");
        var store = SettingsStore.Load(path);
        store.Theme = "Light";
        store.Save();
        store.Theme = "Dark";
        store.Save();

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal("Dark", SettingsStore.Load(path).Theme);
    }

    [Fact]
    public void UpdateMany_WritesOnce()
    {
        using var temp = new TempDir();
        string path = System.IO.Path.Combine(temp.Path, "known_devices.json");
        var store = KnownDevicesStore.Load(path);

        store.UpdateMany(new[]
        {
            Make(0xC0A80164, 0xAABBCCDDEE01, 102_400),
            Make(0xC0A80165, 0xAABBCCDDEE02, 204_800),
        });

        Assert.Equal(1, store.SaveCount);
        var reloaded = KnownDevicesStore.Load(path);
        Assert.True(reloaded.ApplyTo(Make(0xC0A80164, 0xAABBCCDDEE01)));
        Assert.True(reloaded.ApplyTo(Make(0xC0A80165, 0xAABBCCDDEE02)));
    }
}
