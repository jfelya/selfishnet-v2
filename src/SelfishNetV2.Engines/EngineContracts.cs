using SelfishNetV2.Core;
using SelfishNetV2.Drivers;

namespace SelfishNetV2.Engines;

public enum EngineKind { Legacy, Modern }

public enum EngineRunState { Closed, Idle, Shaping }

/// <summary>Engines never localize; they raise codes that the app layer maps to strings.</summary>
public enum EngineErrorKind
{
    DriverOpen,
    /// <summary>The packet driver denied access (restricted to administrators).</summary>
    AccessDenied,
    GatewayMacUnknown,
    ReceiveError,
    AbiMismatch,
    Internal,
}

public sealed class EngineErrorEventArgs : EventArgs
{
    public EngineErrorKind Kind { get; }
    public string Detail { get; }
    public EngineErrorEventArgs(EngineErrorKind kind, string detail) { Kind = kind; Detail = detail; }
}

/// <summary>
/// One running engine bound to one adapter. The <see cref="DeviceCatalog"/> is injected
/// and OWNED BY THE CALLER (it survives engine switches). All operations are async-safe;
/// events are raised on background threads — UI subscribers must marshal.
/// </summary>
public interface IEngineSession : IDisposable
{
    EngineKind Kind { get; }
    EngineRunState State { get; }
    AdapterInfo? Adapter { get; }
    bool IsDiscovering { get; }

    long ForwardedDown { get; }
    long ForwardedUp { get; }
    long Dropped { get; }

    event EventHandler<EngineErrorEventArgs>? Error;
    event EventHandler? StateChanged;
    /// <summary>Raised ~1/s while open: per-device window counters were rotated into display samples.</summary>
    event EventHandler? StatsTick;

    /// <summary>Opens the adapter, starts the ARP listener + housekeeping, registers local PC/gateway.</summary>
    bool Open(AdapterInfo adapter);
    void StartDiscovery();
    void StopDiscovery();

    /// <summary>Starts spoofing + shaping. Hard-gates on a known gateway MAC (never forwards
    /// to broadcast). Fails with <see cref="EngineErrorKind.GatewayMacUnknown"/> when the
    /// gateway cannot be resolved.</summary>
    Task<bool> StartShapingAsync(CancellationToken ct = default);

    /// <summary>Stops forwarding, then heals the LAN: 35 genuine ARP frames per target.</summary>
    Task StopAndHealAsync();

    /// <summary>Full teardown. Heals first if still shaping.</summary>
    void Close();
}

public interface IEngineFactory
{
    EngineKind Kind { get; }
    IEngineSession Create(DeviceCatalog catalog, IPcapDriver driver);
}
