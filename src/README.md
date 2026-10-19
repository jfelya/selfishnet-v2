# src/ — Clean Rebuild

Stack confirmed 2026-10-18: **C# / .NET 8 / WPF**, self-contained single-file `win-x64`.
The solution builds clean (`dotnet build SelfishNetV2.sln` — 0 warnings / 0 errors).
Ship the portable exe with `publish.bat` → output lands in `..\publish\SelfishNetV2.exe`.

Layout:

```
src/
  SelfishNetV2.sln
  SelfishNetV2.Core/          # DTOs, DeviceCatalog, ArpFrameBuilder, TokenBucket
  SelfishNetV2.Drivers/       # IPcapDriver, WpcapDriver, LoopbackDriver, PcapHandleGuard, ABI self-test
  SelfishNetV2.Engines/       # IEngineSession, LegacyEngine, ModernEngine, EngineOrchestrator glue
  SelfishNetV2.Services/      # Localization, Settings, KnownDevices, SpeedFormatter
  SelfishNetV2.App/           # WPF shell (dark/light themes, live bandwidth charts)
  SelfishNetV2.Tests/         # parity vectors, ABI test, shutdown hammer, fault matrix
publish/                      # single-file portable .exe output
```

Ground rules for anything added here:

1. Engines never touch UI types; UI never touches pcap types — DTOs only (`REVAMP_BLUEPRINT.md` §3).
2. Frame builders must pass byte-for-byte parity tests against the original binary's vectors.
3. Every pcap handle is owned by exactly one worker; shutdown = breakloop → join → close.
4. No string identity (`ToString()` on IP/MAC) and no steady-state heap allocation in packet hot paths.
5. All user-visible strings come from `LocalizationManager` (en-US + es-419).
