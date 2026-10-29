# SelfishNet v2 — Hardening Audit (2026-10-28)

> Status: **approved for implementation**. This is the audit record behind
> `docs/superpowers/plans/2026-10-28-revamp-hardening.md`. It is a snapshot; the
> living project story stays in `AI_ENVIRONMENT_NOTES.md`.

## What this repo does

SelfishNet v2 is a Windows desktop tool that discovers LAN devices by ARP, poisons
their ARP caches so their traffic passes through this PC, applies per-device
download/upload limits (or blocks them), and restores the network on stop. It ships
as two self-contained .NET 8/WPF executables, x64 and x86.

**Load assumed:** one operator, one adapter, a home/office `/24` with tens of
devices, occasional 10,000-frame bursts, and a hard requirement that
stop/close/logoff never leaves the LAN poisoned. A `/8` network and a multi-tenant
server were not assumed.

## Verified baseline

- `dotnet build src/SelfishNetV2.sln -c Release` — 0 warnings, 0 errors.
- `dotnet test src/SelfishNetV2.sln -c Release` — **96/96 passed**, ~14 s.
- No runtime NuGet dependencies. Only test packages.
- `dotnet list package --vulnerable --include-transitive` found two high-severity
  advisories in **test-only** transitive packages:
  `System.Net.Http 4.3.0` and `System.Text.RegularExpressions 4.3.0`.

## Must fix

1. **Shutdown can close a pcap handle while a receive thread is still using it**
   (`src/SelfishNetV2.Drivers/PcapCaptureSession.cs:73-101`).
   On join timeout, `Stop()` sets `_thread = null`; `Dispose()` then closes the
   handle. Fix: `_abandoned` flag; never close an abandoned session.
2. **Engine housekeeping joins have the same timeout hazard**
   (`src/SelfishNetV2.Engines/EngineSessionBase.cs:159-180, 698-701`).
   `JoinThread()` ignores its timeout, then `_arp` is disposed. Fix: join result
   must gate disposal; leak rather than close under a live thread.
3. **A capture error leaves victims disconnected while the app says “Shaping”**
   (`src/SelfishNetV2.Drivers/PcapCaptureSession.cs:43-69`,
   `EngineSessionBase.cs:124, 317`). Fix: mark the session stopped; stop shaping
   and heal once on a fatal receive error.
4. **Adapter switch leaves old rows in the UI**
   (`DeviceCatalog.cs:70-75`, `MainViewModel.cs:84-86, 349-358`).
   `Clear()` raises no removal event; new devices at old IPs are ignored. Fix:
   raise removals or a `Cleared` event and clear the UI collection.
5. **Saved caps display as 0 on first discovery**
   (`DeviceViewModel.cs:20-38, 147-153`, `MainViewModel.cs:349-373`).
   The constructor never calls `RefreshCaps()`. Fix: refresh caps in the
   constructor and in `RefreshRow()`; also refresh `Blocked`.
6. **Different device can inherit a blocked device by IP reuse**
   (`KnownDevicesStore.cs:77-113`). The same-IP fallback cannot tell MAC rotation
   from DHCP reuse. Fix: restrict inheritance to randomized MACs and/or
   hostname/recent-seen evidence.
7. **Device memory and settings are not written atomically**
   (`KnownDevicesStore.cs:38-65`, `SettingsStore.cs:30-38`). A crash during
   `File.WriteAllText` loses all saved caps/names. Fix: temp file + replace + backup.
8. **An adapter can be matched to the wrong pcap device**
   (`AdapterInfo.cs:57-61`). `MatchPcapName()` falls back to the first pcap device.
   Fix: return empty on no match and reject the adapter with a clear message.

## Should fix

9. **Discovery is unbounded by subnet size**
   (`EngineSessionBase.cs:186-209`, `AdapterInfo.cs:65-70`). A `/16` takes
   minutes; a `/8` takes hours. Fix: host-count/time budget.
10. **“Scanning…” never clears** (`MainViewModel.cs:168-173`). Fix: return a
    completion signal and reset the status.
11. **Every ARP reply refreshes the UI row**
    (`DeviceCatalog.cs:27-51`, `MainViewModel.cs:86`). Fix: raise update events
    only when identity/flags actually change; remove unused `Changed`.
12. **Chart lines use different scales than the axis**
    (`BandwidthChartControl.cs:113-120, 151-152, 163`). Fix: pass the combined
    scale into `DrawSeries()`.
13. **Reset limits performs one full JSON write per device**
    (`MainViewModel.cs:545-552`, `EngineOrchestrator.cs:57`,
    `KnownDevicesStore.cs:150-168`). Fix: bulk update and save once.
14. **Late hostname resolution can overwrite a fresh user edit**
    (`EngineOrchestrator.cs:148-168`, `KnownDevicesStore.cs:121-138`). Fix:
    `UserEdited` flag checked before applying late settings.
15. **Cap input can overflow; small caps display as 0**
    (`DeviceViewModel.cs:107-114, 147-153`, `SpeedFormatter.cs:38-43`). Fix:
    clamp/reject overflow and display sub-1 KByte caps honestly.
16. **Two instances can fight over the same LAN** (`App.xaml.cs:16-75`). Fix:
    named single-instance mutex; release before elevated relaunch.

## Nice to have

17. Upgrade old test packages; the two high advisories are test-only.
18. Remove grep-verified dead members and merge the duplicate MAC writer.
19. Update current docs: actual 96-test count, publish output, settings path,
    compatibility file name, current localization keys, and the blueprint status.

## Out of scope

- No rewrite of the engines or WPF shell.
- No new runtime dependencies.
- No changes to `reference/` or `prototype/` until the modern build is confirmed
  stable; after that the user wants them removed in a separate cleanup.
- No new exe commit until the fixes are confirmed stable.
