# src/ — Clean Rebuild

Stack: **C# / .NET 8 / WPF**, self-contained single-file builds for `win-x64` and `win-x86`.
Implemented end-to-end; the solution builds clean and all 115 tests pass.

```powershell
dotnet build SelfishNetV2.sln -c Release
dotnet test  SelfishNetV2.sln -c Release
publish.bat          # → ..\publish\{x64,x86}\SelfishNetV2.exe
..\tools\clean.cmd   # reclaim build output afterwards (~1 GB per publish)
```

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
..\publish\                   # single-file portable .exe output (root-level folder)
```

Ground rules for anything added here:

1. Engines never touch UI types; UI never touches pcap types — DTOs only (`REVAMP_BLUEPRINT.md` §3).
2. Frame builders must pass byte-for-byte parity tests against the original binary's vectors.
3. Every pcap handle is owned by exactly one worker; shutdown = breakloop → join → close.
4. No string identity (`ToString()` on IP/MAC) and no steady-state heap allocation in packet hot paths.
5. All user-visible strings come from `LocalizationManager` (en-US + es-419).
