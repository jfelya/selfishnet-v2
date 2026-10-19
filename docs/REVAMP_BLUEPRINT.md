# SelfishNet v2 — Revamp Blueprint (Rebuild-from-Zero Plan)

> Status: **analysis complete, awaiting stack confirmation before scaffolding `src/`.**
> Sources: `docs/ARCHITECTURE_ANALYSIS.md`, `docs/ORIGINAL_FINDINGS.md` (corrects the former on 5 points),
> `docs/MODERNIZATION_ROADMAP.md`, `docs/UNIT_NOTATIONS.md`, `docs/LOCALIZATION_SPEC.md`,
> and a full static audit of `prototype/SelfishNetV2.Test`.

---

## Part 1 — Bug / Edge-Case / Limitation Synthesis & Prevention Strategy

### 1.1 What the prototype verified about the ORIGINAL binary (keep these)

These are load-bearing facts confirmed by IL disassembly (see `ORIGINAL_FINDINGS.md`). The Legacy engine
MUST reproduce them; the Modern engine must remain wire-compatible with them:

| # | Verified behavior | Consequence for the rebuild |
|---|---|---|
| F1 | Capture is **non-promiscuous** (`snaplen=65535, promisc=0`); timeout 1 ms (we relax to 20 ms for CPU) | Works on Wi-Fi where promiscuous mode is unreliable. Keep as default in both engines. |
| F2 | Forwarder classifies on **Ethernet SOURCE MAC**: `local → count-only`, `router → download (match by DST IP)`, `else → upload (match by SRC MAC)`. No EtherType check beyond the `"ip"` BPF filter | Download detection works poisoned *and* unpoisoned. Router MAC is re-read per loop iteration. |
| F3 | Spoof emits **4 frames/victim**, not 2 — the 3rd (self-refresh) re-teaches OUR listener the true router MAC every round; the 4th is an inert codegen leftover | Legacy: send all 4 (spec-locked). Modern: 3 functional frames (documented deviation, leftover dropped). |
| F4 | Router MAC learning is **active** (broadcast ARP request) + passive (listener learns from Ethernet source field) | Both engines need `ResolveMac()` with retries before/at start of spoofing. |
| F5 | Upload shaping only truly works when the victim is poisoned; unpoisoned traffic is duplicate-forwarded (inherent to the design, present in the original too) | Documented behavior, not a bug. UI must not imply otherwise. |
| F6 | Cap semantics: `if (cap != 0 && cap <= bytesThisSecond) drop` — hard 1-second window | Legacy engine keeps it byte-for-byte. Modern engine replaces it with token bucket. |

### 1.2 Defects found in the prototype (must NOT be carried into the rebuild)

#### Critical

| # | Defect | Root cause | Prevention in v2 |
|---|---|---|---|
| C1 | **`pcap_pkthdr` marshalled with wrong layout** — prototype declares `long tv_sec; long tv_usec;` (8 bytes each in C#), but native Windows `struct pcap_pkthdr` uses 32-bit `long`s: real layout is 16 bytes `{i32 sec, i32 usec, u32 caplen, u32 len}`. Managed code reads `caplen`/`len` from **out-of-bounds offsets** → wrong frame lengths → memory over-read / garbage parsing | Hand-written P/Invoke struct, never validated against the native header | Use `{int tv_sec; int tv_usec; uint caplen; uint len;}` with `LayoutKind.Sequential`; add a **startup ABI self-test** (capture one frame, assert `34 ≤ len ≤ caplen ≤ 65535`); cover with a P/Invoke layout unit test (`Marshal.SizeOf == 16`) |
| C2 | **Use-after-free on pcap handles** — `ArpEngine.Close()`/`PacketForwarder.Stop()` call `pcap_close()` while the worker thread may still be blocked inside `pcap_next_ex()` on that handle → undefined behavior, intermittent crash on Stop/adapter-switch/engine-switch | No shutdown protocol between owner thread and handle | Single-ownership rule + mandatory shutdown ritual: **`pcap_breakloop()` → join worker → `pcap_close()`**, wrapped in a `PcapHandleGuard`. Stress-tested by start/stop hammering |
| C3 | **Orphan-poisoning on eviction** — devices silent >350 s are dropped from the catalog; `SpoofAll` then stops targeting them and `UnspoofAll` never heals them → victim left with a poisoned ARP cache. The original's 20 s keep-alive unicast ARP probe was also dropped | Discovery set and spoof set are the same collection; eviction has no teardown hook | Track an explicit **`ISpoofTargetRegistry`** separate from discovery. Eviction path = unspoof → remove. Reinstate the 20 s probe / 350 s eviction pair from the original |

#### High (performance)

| # | Defect | Impact | Prevention in v2 |
|---|---|---|---|
| H1 | **Per-packet heap allocations**: `new byte[len]` + `Marshal.Copy` + `new IPAddress` + `new PhysicalAddress` per frame | Gen0/Gen2 GC storms at LAN rates → capture-buffer drops in the driver → "limits don't work" reports | Modern engine: zero-alloc `Span<byte>` parsing over the native buffer, pooled TX buffers. Legacy engine: still pooled/shared buffers. Budget enforced by allocation tests (0 B/packet in steady state) |
| H2 | **String-based identity in the hot path**: `ip.ToString()` dictionary keys, `srcMac.ToString() == mac.ToString()` per packet, `GetByMac()` = O(n) linear scan with string compare | Thousands of string allocs/compares per second; O(n) upload lookup | Numeric keys everywhere: `uint` for IPv4, `ulong` for MAC, precomputed hash maps for both directions; router MAC cached in an atomic field, refreshed on table change |
| H3 | **Token bucket clock & burst flaws**: `DateTime.UtcNow` (~15.6 ms resolution) for Δt; bucket refilled to 100% on every cap change → full-cap line-rate burst on each edit | Jittery pacing; editing a cap unleashes a burst | `Stopwatch.GetTimestamp()` hi-res clock; clamp burst capacity (e.g. ≤100 ms worth of tokens); don't refill on cap change, only clamp |

#### Medium (behavior / UX)

| # | Defect | Prevention in v2 |
|---|---|---|
| M1 | UI thread freezes up to ~750 ms — `ResolveMac()` (`Thread.Sleep(250)×3`) runs synchronously inside `StartSpoofing` | Fully async start pipeline with cancellation + progress state |
| M2 | **Broadcast-forward hazard** — unknown router MAC falls back to `FF:FF:FF:FF:FF:FF` and uploads get forwarded to broadcast → L2 storm | Hard gate: forwarding refuses to start until gateway MAC is known; per-packet guard drops instead of broadcasting |
| M3 | VLAN-tagged (802.1Q) frames invisible — fixed offsets + plain `"ip"` filter | Modern engine: filter `ip or (vlan and ip)`, EtherType-walk offset detection. Legacy: parity with original (documented limitation) |
| M4 | Single-router-MAC assumption breaks on mesh/multi-SSID gateways or mid-session gateway failover | Learn a router-identity set; re-resolve on sustained source-MAC mismatch |
| M5 | Speed readouts skewed by imprecise 1 s UI timer | Timestamped counters, speed computed over real elapsed time + EMA smoothing |
| M6 | Fragile display-name handling — `StripSuffix()` hardcodes 4 localized suffixes to undo view decoration | Strict MVVM separation: decoration applied at render time only; the model never stores decorated text |
| M7 | Windows `IPEnableRouter=1` silently bypasses the userspace shaper (prototype only warns) | Detect → offer to disable (`Set-Dhcp`… registry + reboot warning) or explicitly shape-around; surface forwarding state in adapter dialog |
| M8 | Per-device spoof checkbox from the original was dropped ("spoof ALL") | Restore per-device targeting in both engines |

#### Inherited constraints (accepted, documented)

- IPv4 only, one adapter/subnet at a time (original parity).
- **Npcap (or legacy WinPcap) remains the single external prerequisite.** Windows exposes no supported
  way to inject L2 ARP frames without a kernel packet driver (`SOCK_RAW` cannot craft Ethernet frames;
  WinDivert operates at L3+). The app itself ships as one portable .exe; the driver chain is
  detected dynamically (Npcap → WinPcap → clear error). Redistribution of a silent Npcap installer
  would require the Npcap OEM license — flag as a licensing decision, default = detect-and-prompt.

### 1.3 Process-level prevention (how the new build avoids regressions)

1. **Golden-vector parity tests** — every frame builder (request / poison pair / self-refresh / restore)
   is diffed byte-for-byte against vectors derived from `CArp::buildArpPacket`. Runs in CI.
2. **ABI self-test at startup** — validates struct sizes and performs a one-frame capture sanity check
   before the engines are considered available.
3. **Shutdown protocol test** — automated start/stop/switch hammering (1000 cycles) under load.
4. **Fault-matrix tests** — unknown gateway MAC, VLAN tags, adapter unplug mid-session, Npcap service
   stopped mid-capture, cap-change storms, eviction of an actively-spoofed device.
5. **Loopback test seam** — `IPcapDriver` has a synthetic in-memory implementation so both engines are
   integration-tested without touching real hardware (see Part 3).
6. **Allocation budget** — BenchmarkDotNet + allocation profiler gate: zero steady-state allocations
   per forwarded packet in the Modern engine.

---

## Part 2 — Language & Framework Recommendation

### 2.1 Evaluation matrix (weighted for THIS use case)

| Criterion | A. C#/.NET 8 (WPF) | B. Rust + egui/fltk | C. C++20 + ImGui/Qt | D. Go + Fyne/Wails |
|---|---|---|---|---|
| Raw injection stability (Npcap FFI) | ★★★★★ P/Invoke proven in prototype | ★★★★ needs hand-rolled `libloading` or Npcap SDK | ★★★★★ native C API, zero interop | ★★ gopacket needs cgo; pure-Go binding must be hand-written |
| Modern GUI capability (charts, animation, dark/light, grids) | ★★★★★ XAML, styles, HW-accelerated | ★★★ egui great for charts, weak native grids | ★★★ ImGui excellent charts, app UX hand-built | ★★ Fyne non-native, grids/charts custom |
| Single portable .exe | ★★★★ self-contained single-file ~60–80 MB | ★★★★★ ~8–15 MB, zero runtime | ★★★★★ ~3–8 MB | ★★★★ ~15–25 MB |
| Dev velocity for bilingual business UX | ★★★★★ | ★★★ | ★★ | ★★★ |
| Toolchain present on this machine | ✅ .NET 8.0.425 installed | ❌ rustup needed | ❌ VS Build Tools (~GBs) needed | ❌ Go needed |
| Team knowledge base (existing docs/code) | ★★★★★ all prior work is C# | ★★ | ★★ | ★ |

### 2.2 Recommendation

**Primary: C# / .NET 8, WPF, self-contained single-file publish (`win-x64`, ReadyToRun + single-file compression).**

- The engineering risk in this project concentrates at the **driver boundary** (ARP injection, capture
  filters, shutdown discipline) — and that boundary is *already proven* in C# P/Invoke by the prototype.
  Rewriting it in another language re-opens exactly the riskiest layer for zero functional gain.
- WPF delivers the required UX bar natively: hardware-accelerated rendering, restyleable controls,
  smooth animations, dark/light themes, DPI awareness (PerMonitorV2), and an easy path for custom
  real-time bandwidth charts (lightweight custom control, no heavy charting dependency).
- `dotnet publish -p:PublishSingleFile=true -p:SelfContained=true` yields **one portable .exe** —
  no .NET install, no VC++ redist, no NuGet runtime pieces. The only concession is size (~60–80 MB).
- The packet hot path gets `unsafe`/`Span<T>`/`MemoryMarshal` + `ArrayPool` — effectively native-class
  throughput for this workload (LAN forwarding is I/O-bound at our scale, not CPU-bound).
- The engine layer is UI-agnostic (see Part 3), so the shell can be swapped later without touching engines.

**Alternative (if binary size becomes a hard requirement): Rust + egui** — best-in-class size/perf,
at the cost of installing a toolchain, hand-rolling the wpcap FFI, and losing native Windows UX polish.
**Not recommended:** C++ (dev cost on grids/localization, no toolchain present) and Go (no maintained
cgo-free Windows injection binding; Wails adds a WebView2 runtime dependency).

---

## Part 3 — Architectural Blueprint: Dual-Engine Switch Architecture

### 3.1 Layered design

```
┌──────────────────────────────────────────────────────────────────┐
│ PRESENTATION  (WPF, MVVM, PerMonitorV2 DPI)                      │
│  MainWindow · DeviceGridView · BandwidthChartControl             │
│  ThemeManager (dark/light) · EngineSwitchControl                 │
│  ViewModels bind ONLY to DTO snapshots; never to pcap internals  │
├──────────────────────────────────────────────────────────────────┤
│ APPLICATION SERVICES (UI-agnostic)                               │
│  LocalizationManager (en-US / es-419 hot-swap)                   │
│  SettingsStore · KnownDevicesStore (persist by MAC)              │
│  SpeedFormatter (KByte/s ↔ KBit/s; storage in bytes/s only)      │
│  EngineOrchestrator ── owns the ACTIVE session, performs swaps   │
├─────────────────────── ENGINE CONTRACT ──────────────────────────┤
│  INetworkEngineFactory → creates IEngineSession                  │
│  IEngineSession:                                                 │
│    Open(AdapterInfo) · StartDiscovery() · SetTargets(IReadOnly)  │
│    StartShaping() · StopAndHeal() · events: DeviceSeen/Stats/Err │
│  Outputs are immutable DTOs (DeviceSnapshot, TrafficSample).     │
│  DeviceCatalog is owned by the app core, NOT by an engine —      │
│  it survives engine switches.                                    │
├──────────────────────────┬───────────────────────────────────────┤
│  LEGACY ENGINE           │  MODERN ENGINE                        │
│  Spec-locked port of the │  Same wire protocol, new machinery:   │
│  corrected original:     │  · zero-alloc Span parsing, pooled TX │
│  · 4-frame spoof (incl.  │  · uint/ulong-keyed lock-free lookups │
│    self-refresh) @5000ms │  · token bucket (hi-res clock)        │
│  · 1s hard-cap drop      │  · batched capture + immediate mode   │
│  · src-MAC classification│  · dedicated spoof scheduler thread   │
│  · 20s probe/350s evict  │  · VLAN-aware offsets (optional)      │
│  · 35× unspoof heal      │  · EMA stats over real elapsed time   │
│  Purpose: A/B baseline + │  Purpose: default daily driver        │
│  instant fallback        │                                       │
├──────────────────────────┴───────────────────────────────────────┤
│ PACKET I/O ABSTRACTION                                           │
│  IPcapDriver: enumerate/open/setfilter/send/breakloop/close      │
│  · WpcapDriver — dynamic LoadLibrary: Npcap → WinPcap-compat     │
│  · LoopbackDriver — synthetic frames for tests/CI                │
│  PcapHandleGuard enforces: breakloop → join → close              │
├──────────────────────────────────────────────────────────────────┤
│ NATIVE: Npcap wpcap.dll (preferred) · WinPcap wpcap.dll (fallback)│
└──────────────────────────────────────────────────────────────────┘
```

### 3.2 Runtime engine switch sequence (user toggles in UI)

1. UI raises `EngineSwitchRequested` → `EngineOrchestrator` locks state (UI shows "switching…").
2. Current session: stop shaping → **unspoof all active targets (35× genuine frames)** →
   `PcapHandleGuard` teardown (breakloop/join/close).
3. Keep `DeviceCatalog`, caps, blocked flags, custom names (owned by app core — untouched by the swap).
4. Construct the other engine from `INetworkEngineFactory`, open on the same adapter, re-apply targets.
5. Resume. Total switch time ~1–2 s of pass-through traffic; ARP heals instantly thanks to step 2.

### 3.3 Why this split prevents the past pitfalls

- **Both engines sit behind one contract** → the UI/services code is written once; the toggle is a
  factory call, not a fork in the codebase.
- **The catalog outlives engines** → fixes C3 (eviction/unspoof bookkeeping) and makes A/B comparison
  honest (same devices, same caps, different machinery).
- **All shutdown goes through `PcapHandleGuard`** → fixes C2 structurally; no engine can close a handle
  its worker is still using.
- **Frame construction lives in a shared, parity-tested `ArpFrameBuilder`** → Legacy and Modern emit
  byte-identical poison/restore frames (the ONLY intended behavioral differences are timing/shaping);
  fixes C1-class bugs via ABI self-tests and golden vectors.
- **`IPcapDriver` seam** → engines are testable offline; the fault matrix (M2–M4) becomes unit tests.

### 3.4 Planned `src/` layout (scaffolded after stack confirmation)

```
src/
  SelfishNetV2.sln
  SelfishNetV2.Core/          # DTOs, DeviceCatalog, ArpFrameBuilder, TokenBucket, unit logic
  SelfishNetV2.Drivers/       # IPcapDriver, WpcapDriver, LoopbackDriver, PcapHandleGuard, ABI self-test
  SelfishNetV2.Engines/       # INetworkEngineFactory, IEngineSession, LegacyEngine, ModernEngine
  SelfishNetV2.Services/      # Localization, Settings, KnownDevices, SpeedFormatter, EngineOrchestrator
  SelfishNetV2.App/           # WPF shell (Views, ViewModels, Theme, BandwidthChartControl)
  SelfishNetV2.Tests/         # parity vectors, ABI, shutdown hammer, fault matrix, token bucket
publish/                      # single-file .exe output
```

---

## Decision log (confirmed 2026-10-18)

- [x] **Stack: C# / .NET 8 / WPF**, self-contained single-file `win-x64` (ReadyToRun + compression).
      Scaffold created in `src/` and verified building (0 warnings / 0 errors).
- [x] **Npcap prerequisite: detect-and-prompt** — Npcap → WinPcap fallback → clear bilingual error.
- [ ] UI language default: keep Español (Latam) default from prototype, or English

### Scaffold state (verified)

`src/SelfishNetV2.sln` builds clean. Projects: `Core` (net8.0) · `Drivers` / `Engines` / `Services`
/ `Tests` (net8.0-windows) · `App` (WPF, `AssemblyName=SelfishNetV2`, `requireAdministrator` +
PerMonitorV2 manifest). Single-file output: run `src/publish.bat` → `publish/SelfishNetV2.exe`.
**Next implementation phase:** `Drivers` (wpcap FFI + `PcapHandleGuard` + ABI self-test) →
`Core` (catalog, frame builder, token bucket + parity tests) → `Engines` (Legacy first, then Modern)
→ `Services` → `App` shell.
