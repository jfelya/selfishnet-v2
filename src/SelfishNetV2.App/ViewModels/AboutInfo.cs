using SelfishNetV2.Engines;

namespace SelfishNetV2.App.ViewModels;

/// <summary>Snapshot shown in the About window (system + live traffic-control state).</summary>
public sealed record AboutInfo(
    string Version,
    string EngineName,
    string DriverBackend,
    string SystemText,
    string RuntimeText,
    string AdapterName,
    string LocalText,
    string GatewayText,
    string GatewayMac,
    EngineRunState State,
    int Devices,
    int Targets,
    long ForwardedDown,
    long ForwardedUp,
    long Dropped,
    long SendFailures,
    bool OsForwarding);
