using SelfishNetV2.Core;
using SelfishNetV2.Drivers;
using SelfishNetV2.Engines;

namespace SelfishNetV2.Services;

/// <summary>
/// Owns the ACTIVE engine session and performs runtime engine swaps. The
/// <see cref="DeviceCatalog"/> (devices, caps, names, flags) lives here at the app
/// core level — engines are interchangeable workers over it.
///
/// Swap sequence (REVAMP_BLUEPRINT §3.2): stop shaping → heal all targets (35×
/// genuine ARP each) → teardown (breakloop/join/close) → build the other engine →
/// re-open same adapter → re-apply targets → resume shaping if it was active.
/// ~1–2 s of pass-through traffic; ARP heals instantly.
/// </summary>
public sealed class EngineOrchestrator : IDisposable
{
    private readonly IPcapDriver _driver;
    private readonly DeviceCatalog _catalog = new();
    private readonly KnownDevicesStore _known;
    private readonly Dictionary<EngineKind, IEngineFactory> _factories;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private IEngineSession? _session;
    private AdapterInfo? _adapter;

    public EngineOrchestrator(IPcapDriver driver, KnownDevicesStore known, EngineKind initialKind)
    {
        _driver = driver;
        _known = known;
        CurrentKind = initialKind;
        _factories = new Dictionary<EngineKind, IEngineFactory>
        {
            [EngineKind.Legacy] = new LegacyEngineFactory(),
            [EngineKind.Modern] = new ModernEngineFactory(),
        };

        _catalog.DeviceAdded += OnDeviceAdded;
    }

    public DeviceCatalog Catalog => _catalog;
    public EngineKind CurrentKind { get; private set; }
    public IEngineSession? Session => _session;
    public EngineRunState State => _session?.State ?? EngineRunState.Closed;
    public bool IsShaping => State == EngineRunState.Shaping;
    public AdapterInfo? CurrentAdapter => _adapter;
    public string DriverBackend => _driver.BackendName;

    public event EventHandler<EngineErrorEventArgs>? Error;
    public event EventHandler? StateChanged;
    public event EventHandler? DiscoveryCompleted;
    public event EventHandler? EngineChanged;
    public event EventHandler? StatsTick;

    // ---------------- device mutation (apply + persist) ----------------

    public void SetCustomName(Device dev, string? name)
    {
        dev.CustomName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        dev.UserEdited = true;
        _known.Update(dev);
    }

    public void SetCaps(Device dev, int downBytes, int upBytes)
    {
        dev.CapDownBytesPerSec = downBytes;
        dev.CapUpBytesPerSec = upBytes;
        dev.UserEdited = true;
        _known.Update(dev);
    }

    public void SetCapsBulk(IEnumerable<Device> devices, int downBytes, int upBytes)
    {
        var list = devices as IList<Device> ?? devices.ToList();
        foreach (var dev in list)
        {
            dev.CapDownBytesPerSec = downBytes;
            dev.CapUpBytesPerSec = upBytes;
            dev.UserEdited = true;
        }
        _known.UpdateMany(list);
    }

    public void SetBlocked(Device dev, bool blocked)
    {
        dev.Redirect = !blocked;
        dev.UserEdited = true;
        _known.Update(dev);
    }

    // ---------------- adapter / session lifecycle ----------------

    /// <summary>Opens an adapter on the current engine. The catalog is cleared only when
    /// the user explicitly changes adapter (a different LAN means different devices).
    /// If shaping is active on the OLD adapter, the LAN is healed BEFORE the catalog is
    /// dropped — otherwise the target list would vanish and victims keep poisoned caches.</summary>
    public bool OpenAdapter(AdapterInfo adapter, bool clearCatalog = true)
    {
        if (_session is { State: EngineRunState.Shaping })
        {
            try { _session.StopAndHealAsync().GetAwaiter().GetResult(); }
            catch { /* best-effort heal */ }
        }
        if (clearCatalog) _catalog.Clear();
        _adapter = adapter;
        var session = EnsureSession();
        bool ok = session.Open(adapter);
        if (ok) session.StartDiscovery(); // immediate sweep; the ARP listener keeps learning passively
        return ok;
    }

    public void Discover() => _session?.StartDiscovery();

    public async Task<bool> StartShapingAsync(CancellationToken ct = default)
    {
        if (_session == null) return false;
        return await _session.StartShapingAsync(ct).ConfigureAwait(false);
    }

    public async Task StopAndHealAsync()
    {
        if (_session != null)
            await _session.StopAndHealAsync().ConfigureAwait(false);
    }

    /// <summary>The runtime engine toggle. Heals the LAN, swaps the engine, restores state.</summary>
    public async Task<bool> SwitchEngineAsync(EngineKind kind, CancellationToken ct = default)
    {
        if (kind == CurrentKind && _session != null) return true;
        await _switchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool wasShaping = IsShaping;
            var adapter = _adapter;

            if (_session != null)
            {
                await _session.StopAndHealAsync().ConfigureAwait(false); // heal before teardown
                _session.Close();
                _session.Dispose();
                _session = null;
            }

            CurrentKind = kind;
            EngineChanged?.Invoke(this, EventArgs.Empty);

            if (adapter == null) return true;
            var session = EnsureSession();
            if (!session.Open(adapter)) return false;
            if (wasShaping)
                return await session.StartShapingAsync(ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private IEngineSession EnsureSession()
    {
        if (_session != null) return _session;
        _session = _factories[CurrentKind].Create(_catalog, _driver);
        _session.Error += (s, e) => Error?.Invoke(s, e);
        _session.StateChanged += (s, e) => StateChanged?.Invoke(s, e);
        _session.DiscoveryCompleted += (s, e) => DiscoveryCompleted?.Invoke(s, e);
        _session.StatsTick += (s, e) => StatsTick?.Invoke(s, e);
        return _session;
    }

    // ---------------- catalog side-effects ----------------

    private void OnDeviceAdded(Device dev)
    {
        _known.ApplyTo(dev); // remembered name/caps/block/target by MAC
        // Background hostname resolution; catalog notifies the UI when done.
        if (dev.IsGateway || dev.IsLocalPc) return;
        _ = Task.Run(async () =>
        {
            try
            {
                string name = await HostResolver.ResolveAsync(dev.Ip).ConfigureAwait(false);
                if (string.IsNullOrEmpty(dev.Name) || dev.Name == NetKeys.FormatIp(dev.IpKey))
                {
                    dev.Name = name;
                    // Last chance to recover settings for a rotated MAC whose IP also changed.
                    _known.TryApplyByName(dev, name);
                    _catalog.NotifyUpdated(dev);
                }
            }
            catch { /* keep IP as name */ }
        });
    }

    public void Dispose()
    {
        try
        {
            _session?.Close();
            _session?.Dispose();
        }
        catch { }
        _session = null;
    }
}
