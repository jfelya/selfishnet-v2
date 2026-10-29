# SelfishNet v2 — Project Context (single source of truth)

> **Read this first.** Git history was intentionally rewritten on 2026-10-19 to purge
> ~200 MB of committed binaries, so this file — not the commit log — carries the project
> story, decisions and state. It is kept current after every working session.
>
> Deeper documents: `docs/REVAMP_BLUEPRINT.md` (master plan and defect catalogue),
> `docs/PROTOTYPE_POSTMORTEM.md` (why the prototype failed), `docs/WINDOWS_COMPATIBILITY.md`
> (OS contract), `docs/PENDING.md` (deferred work), `docs/ORIGINAL_FINDINGS.md`
> (verified mechanics of the 2007 original), `tools/README.md` (maintenance scripts).

---

## 1. What this project is

A from-scratch revamp of the classic **SelfishNet** LAN bandwidth controller (2007):
ARP discovery, bidirectional ARP spoofing, traffic shaping and per-device blocking, rebuilt as a
**portable Windows desktop app** with a modern UI.

- **Owner:** Fely. **Remote:** `https://github.com/jfelya/selfishnet-v2` (branch `main`).
- **Stack:** C# / .NET 8 / WPF, self-contained single-file executables (x64, x86).
- **Bilingual:** English (US) and Latin American Spanish (es-419), hot-swappable.
- **Defaults:** Spanish UI + dark theme, window starts maximized.
- **Only external prerequisite:** Npcap (or legacy WinPcap). The app itself needs no installer.

## 2. Current state (verified 2026-10-28)

| Item | State |
|---|---|
| Build | `dotnet build src/SelfishNetV2.sln -c Release` → 0 warnings, 0 errors |
| Tests | **124/124 green** in ~15 s (no hardware, no admin) |
| Field test | Real LAN: both engines limited a phone; runtime engine switch worked; healing worked |
| Artifacts | `SelfishNetV2-x64.exe` 68.5 MB · `-x86.exe` 63.3 MB (project root) |
| OS support | Windows 10 1607 → 22H2 and all Windows 11 releases; floor declared and guard-tested |
| Elevation | Starts unelevated; offers one elevated restart only if Npcap restricts the driver |
| Repo | Source + docs + the two portable exes at the root (versioned) |

## 2b. Active workstream — hardening (started 2026-10-28)

- Branch/worktree: `revamp-hardening`.
- Audit: `docs/REVAMP_HARDENING_AUDIT.md`.
- Approved plan: `docs/superpowers/plans/2026-10-28-revamp-hardening.md`.
- Sequence: Must-fix tasks 1–2, persistence task 3, discovery/input task 4,
  display/performance task 5, hygiene/docs task 6, final verification task 7.
- Current status: **Tasks 1–7 complete; final-review fix pass applied; integration
  is the user's decision.** Capture
  shutdown is fail-safe (a stuck spoof/sweep is leaked, not closed), fatal receive
  errors stop shaping and heal, catalog clears remove UI rows, saved caps render on
  first load, and an unmatched pcap GUID is rejected. Device memory writes
  atomically with `.bak` recovery; in-session global-MAC replacement resets old
  policy; same-IP inheritance requires randomized MACs and a recent sighting; late
  DNS cannot overwrite a user edit. Discovery is capped, starts at the local
  address, reports completion, and cap entry clamps overflow/sub-unit values.
  Catalog updates fire only on identity changes; the chart shares one scale; a
  named mutex prevents two copies; adapter errors are specific; failed heals keep
  their claim for a close retry; logoff teardown is bounded. Test packages are
  current and vulnerability-free; docs/guides are current. Final verification:
  Release build 0 warnings/0 errors, **124/124 tests green**, package audit clean,
  working tree clean on branch `revamp-hardening`.
- Standing user requests: keep this file's storyline current after every
  milestone; update `GUIDE_EN.txt` / `GUIDE_ES.txt` when behavior changes; do not
  commit new root executables until the fixes are confirmed stable; remove
  `reference/` and `prototype/` only after that confirmation.

## 3. How to build, test, publish, clean

```powershell
dotnet build src/SelfishNetV2.sln -c Release      # build everything
dotnet test  src/SelfishNetV2.sln -c Release      # 124 tests, no hardware/admin needed
src\publish.bat                                    # → publish\{x64,x86}\SelfishNetV2.exe
tools\clean.cmd                                    # reclaim build output (~1 GB after publishing)
```

Verification without UAC (used throughout development): a throwaway console project referencing
`SelfishNetV2.App`, calling `new App().InitializeComponent()`, constructing `MainWindow` and then
`Measure/Arrange/UpdateLayout` + exercising theme/language/unit commands. It catches runtime XAML
and resource errors that a compile cannot. Published exes can also simply be launched (manifest is
`asInvoker`) and closed after a few seconds to prove startup.

## 4. Architecture in brief

```
WPF shell (MVVM, themes, charts)
   │  DTOs / events only — never pcap types
EngineOrchestrator (owns the ACTIVE session, performs swaps)
   │  IEngineSession contract
LegacyEngine        |  ModernEngine
   │                │
Packet I/O: IPcapDriver → WpcapDriver (Npcap → WinPcap) | LoopbackDriver (tests)
```

- **App core owns the `DeviceCatalog`** (devices, caps, names, flags) — it survives engine switches.
- **Two engines, one wire protocol.** Frame bytes come from the shared, parity-tested
  `ArpFrameBuilder`; only policy differs:

| Aspect | Legacy (spec-locked 2007) | Modern (default) |
|---|---|---|
| Spoof burst | 4 frames/victim incl. the inert codegen leftover | 3 functional frames |
| Re-poison | every 5 000 ms | every 2 000 ms + double initial burst |
| Reactive ARP defense | off | on (answers victim→gateway requests, router→victim replies, victim announcements) |
| Cap policy | hard 1-second window (`cap != 0 && cap <= bytesThisSecond`) | token bucket (hi-res clock, 50 ms burst, single-packet deficit, clamp-not-refill) |
| Capture filter | `ip` | `ip or (vlan and ip)` (802.1Q/802.1ad aware) |
| Capture latency | 20 ms timeout | `mintocopy = 1` (wake per packet) |

- **Forwarding** (original-faithful): classification by **source MAC** — own frames counted,
  router-sourced = download (matched by destination IP), otherwise upload (matched by source MAC).
  MACs are rewritten to relay. Numeric keys (`uint` IP / `ulong` MAC), zero allocation per packet,
  one reused 64 KB transmit buffer.
- **Healing** (best-effort by nature): registry `_poisoned` (ip→mac) independent of UI flags;
  35 unicast restore pairs + 3+3 broadcast gratuitous announcements per device; gateway MAC
  re-resolved once per pass; failed claims retried on Close; Windows logoff/shutdown heals
  synchronously with a 3 s bound and skips the quit dialog. Force-kill and devices that reject
  ARP corrections cannot be handled — the user guides say so.
- **Discovery**: passive ARP learning always on + a quiet 60 s background sweep; manual Discover.
- **Elevation**: `asInvoker` + `DriverAccessProbe`; the only thing that can need admin rights is
  the Npcap driver, and that is an install-time option.
- **Missing-driver UX**: without Npcap the app still opens normally, shows a warning banner and a
  **Download Npcap** button that opens `https://npcap.com/#download` (the only prerequisite).
- **Driver discipline**: one capture per handle, `breakloop → join → close`, `pcap_pkthdr` asserted
  at 16 bytes (LLP64), first-frame sanity check, ABI guard test.

## 5. Repository layout

```
SelfishNet v2/
├── AI_ENVIRONMENT_NOTES.md          # ★ this file
├── README.md                        # human-facing overview, support matrix, hygiene
├── GUIDE_EN.txt / GUIDE_ES.txt      # plain-language user guides (user-maintained, do not rewrite)
├── VISUAL_DESIGN_SYSTEM.md          # user-provided design-system reference
├── SelfishNetV2-{x64,x86}.exe        # deliverables at the root — versioned (clone-and-run)
├── docs/                            # blueprint, findings, OS support, postmortem, backlog, units, i18n spec
├── reference/                       # golden material, never modify
│   ├── original-app/                #   2007 build (Win7+ variant) + DLLs + npf.sys
│   ├── original-app-xp/             #   2007 XP build (+ its README)
│   ├── redist/                      #   era installers (WinPcap, VC++ redists, .NET 4)
│   └── tools/                       #   analyze_app.ps1, disasm.ps1
├── prototype/                       # frozen free-model prototype (reference only)
├── src/                             # the revamp
│   ├── SelfishNetV2.Core/           #   DTOs, DeviceCatalog, ArpFrameBuilder, PacketClassifier, TokenBucket
│   ├── SelfishNetV2.Drivers/        #   wpcap FFI, PcapCaptureSession, LoopbackDriver, probes, ABI self-test
│   ├── SelfishNetV2.Engines/        #   IEngineSession, EngineSessionBase, LegacyEngine, ModernEngine
│   ├── SelfishNetV2.Services/       #   Localization, Settings, KnownDevices, EngineOrchestrator, formatter
│   ├── SelfishNetV2.App/            #   WPF shell: Themes/, Theming/, Controls/, ViewModels/, Views/
│   ├── SelfishNetV2.Tests/          #   124 tests
│   ├── Directory.Build.targets      #   OS floor + WinRT projection exclusion
│   └── publish.bat                  #   2-arch self-contained publish (x64 + x86)
├── tools/                           # clean.cmd/ps1 + README
└── publish/                         # build output (git-ignored)
```

## 6. Decisions log (why things are the way they are)

| Decision | Reason |
|---|---|
| C#/.NET 8/WPF self-contained | The riskiest layer (wpcap P/Invoke) was already proven in C#; WPF delivers the UI without installers |
| One exe per architecture | A self-contained .NET app embeds an arch-specific runtime; XP→11 in one binary is impossible |
| OS floor declared as 10.0.14393 | .NET 8 minimum; `Directory.Build.targets` (not `.props` — there `$(TargetFramework)` is still empty and the condition silently fails) + a guard test |
| WinRT projection excluded from publish | Raising the target platform dragged a 24 MB unused assembly into every bundle |
| Dual engines | A/B comparison and an instant fallback; Legacy stays a faithful 2007 replica |
| Healing registry | The prototype healed only currently-ticked devices, orphaning caches |
| Reactive poison + 2 s cadence | Fixes the observed "device escaped shaping" race |
| `asInvoker` + access probe | Admin is only needed when Npcap restricts the driver; avoids a UAC prompt otherwise |
| Settings follow rotated MACs | Phones randomize their Wi-Fi MAC; caps/names match by MAC → last IP → hostname so limits follow the device |
| Spoofing is unconditional | Per-device Spoof column removed (user request); every discovered device is shaped. Legacy `Targeted` flags are ignored on load and normalised to true on save |
| 1 Hz live speeds | UI computes per-device rates from lifetime counters once per second, matching the engine's cap/display window |
| Healing is paced and quiet | Restore bursts sleep 4 ms/pair (Realtek/Npcap error 31 under dense sends); heal-path send failures are counted, never shown as dialogs |
| Fail-closed shaping | A gateway-less adapter is rejected (`MsgNoGateway`) and send failures are surfaced — never a silent "running but not spoofing" state |
| Two portable exes versioned at the root | Clone-and-run convenience; a deliberate ~130 MB-per-rebuild trade-off (build output stays ignored) |
| Spanish + dark defaults, maximized start | User preference |
| Win7/XP deferred | Documented options in `docs/PENDING.md` (net48 and net40 forks) |

## 7. Defect history and prevention

Full catalogue: `docs/REVAMP_BLUEPRINT.md` §1 (C1–C3 critical, H1–H3 performance, M1–M8 behaviour)
and `docs/PROTOTYPE_POSTMORTEM.md` (why the prototype failed: escaping poison, orphaned heal,
teardown races, pcap ABI, hot-path allocations, broadcast fallback, no retry, no engine fallback,
UI blocking, clock/refill).

Rules adopted (keep them):
1. Frame builders are parity-tested against golden byte vectors.
2. Driver-boundary ABIs are asserted in tests and sanity-checked at runtime.
3. Shutdown is always `breakloop → join → close`; cancellation precedes joining.
4. No string identity or steady-state allocation in the packet hot path.
5. Anything that modifies the network needs a registry entry and a heal path.
6. Failure paths are tested on `LoopbackDriver` — no hardware required.
7. Promises stay scoped (healing is best-effort; say so).

## 8. Test map (124 tests)

| File | Guards |
|---|---|
| `ArpFrameBuilderParityTests` | Byte-for-byte ARP frames (request/poison/self-refresh/inert/restore) + parsing |
| `PcapAbiTests`, `CompatibilityTests` | 16-byte LLP64 `pcap_pkthdr`, header sanity, declared Win10 1607 floor |
| `PacketClassifierTests` | Source-MAC flows, VLAN walk, short frames |
| `DeviceCatalogTests` | Dual index, MAC re-indexing, sorting, spoof-target filtering |
| `TokenBucketTests` | Unlimited rate, sustained rate, deficit rule, clamp-not-refill |
| `SpeedFormatterTests` | KByte/s ↔ KBit/s conversion and formatting rules |
| `EngineIntegrationTests` | Learning, burst shapes, forwarding/rewriting, blocking, caps, exact 35-pair healing, engine swap, adapter-switch healing, no-gateway hard fail |
| `EngineRobustnessTests` | Reactive poison, broadcast healing, untarget heal, close retry, 10 000-frame burst, auto-scan, close-after-stop silence |
| `KnownDevicesStoreTests` | Settings follow rotated MACs: exact MAC → last IP → hostname; no override once applied |
| `ShutdownHammerTests` | 300 start/stop/dispose cycles; engine open/close under load |
| `CaptureSafetyTests` | Stuck receive thread is leaked, not closed; fatal receive error stops shaping and heals |
| `PersistenceTests` | Atomic save, `.bak` recovery, corrupt-main fallback, one-write bulk update |
| `ViewModelStateTests`, `AdapterMatchTests` | Saved cap rendering, sub-1 display, no pcap GUID match |
| `SingleInstanceTests` | A second copy cannot acquire the named mutex |

## 9. Environment and prerequisites

- Windows 10/11 (target machine). .NET SDK 8 required only to build; the exes are standalone.
- Npcap (https://npcap.com) preferred, WinPcap fallback. Npcap's "restrict to administrators"
  install option decides whether elevation is needed.
- `%AppData%\SelfishNetV2\` holds the only persistent data: `settings.json` (language, theme,
  units, last adapter, engine) and `known_devices.json` (names, caps, blocked flag, last IP
  per MAC). No logging, no traffic/payload storage.

## 10. Working timeline (compact story, survives the history rewrite)

1. **Reverse engineering** — original 2007 binary and `PcapNet.dll` dissected; mechanics verified
   (`docs/ORIGINAL_FINDINGS.md` corrects the earlier analysis on five points).
2. **Prototype** — free-model C#/WinForms app in `prototype/`; proved the concept, failed on the
   edge cases (postmortem).
3. **Blueprint** — defect synthesis, stack decision (C#/.NET 8/WPF), dual-engine architecture.
4. **Rebuild** — Core → Drivers → Engines → Services → WPF shell → tests; first clean build and
   portable publish.
5. **Acceptance test on a real LAN** — shaping worked on both engines; a device escaped once →
   reactive defense, 2 s cadence, healing registry, session-end healing.
6. **OS certification** — declared Win10 1607 floor + guard test, DWM 1809 fallback, dual-arch
   publish (x64 + x86), WinRT projection excluded, support matrix documented.
7. **Repository hygiene** — history rewritten to purge ~200 MB of binaries; build output ignored;
   `tools/clean.cmd` added (~1.1 GB reclaimed locally).
8. **Clone-and-run** — the two portable exes (x64/x86) were re-added to the repository root as
   versioned deliverables at the user's request; ARM support was dropped. The app now shows a
   "Download Npcap" button when the only prerequisite is missing.
9. **Field report "it stopped working"** — live diagnosis showed the app still spoofs and counts
   correctly; the real cause was a phone rotating its Wi-Fi MAC, so the saved cap stayed on the old
   MAC entry and the new entry was unlimited. Fixed with identity fallback (MAC → last IP →
   hostname), plus fail-closed shaping (no gateway), send-failure reporting and About diagnostics.
10. **UX round** — Spoof column removed (all devices shaped by default), Reset-limits button,
    numeric-only limit boxes (empty = 0), taller rows, 1 Hz live speeds (reverted from
    4 Hz on 2026-10-29), paced/silent healing.
11. **UI cleanup** — OS-forwarding detection/column/warning removed entirely (user request),
    adapter dialog rows sized for two-line text and given the dark title bar, and the numeric
    editor now inherits the themed TextBox style instead of rendering white (implicit WPF styles
    do not apply to derived controls).
12. **About redesign** — the MessageBox was replaced by a proper `AboutWindow`: organized
    System/Traffic-control sections, larger Medium typography, live diagnostics, a **read-only**
    OS-forwarding row (IPEnableRouter) with a short explanatory hint, and a "Copy details" button.
    The OS-forwarding check exists only for this display — nothing warns or blocks on it.
13. **Hardening workstream (2026-10-28, complete pending integration)** — a full audit
    found shutdown timeout hazards, a fatal-capture-error path that left victims
    disconnected, stale UI state after adapter switches, saved caps not shown, unsafe
    IP-reuse inheritance, non-atomic settings writes, and unbounded discovery. The
    approved plan is `docs/superpowers/plans/2026-10-28-revamp-hardening.md`; Tasks
    1–6 and the fresh-review fix pass are complete. The review added: stuck spoof
    threads and discovery sweeps are leaked/stopped instead of closing a live handle,
    in-session global-MAC replacement resets old policy, failed heals retain their
    claim, logoff teardown is bounded, discovery starts at the local /24, and adapter
    selection surfaces a specific no-Npcap message. Full suite: **124/124**; test
    packages have no known vulnerabilities. The root executables were intentionally
    not rebuilt or committed during hardening; update them only after a stable
    release check.

## 11. Backlog (see `docs/PENDING.md`)

- **P1** Windows 7 SP1 support: separate `net48` build (medium–large port).
- **P2** Windows XP SP3 support: separate `net40` fork (large; the preserved 2007 build covers XP today).
- **P3** Mesh/multi-MAC routers (blueprint M4), configurable auto-scan interval, optional installer,
  chart export, code signing.
