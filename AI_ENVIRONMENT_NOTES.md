# AI Environment Notes & Project Memory: SelfishNet v2 (Revamp)

## 1. Project Mission & Context
- **Repository:** `SelfishNet v2`
- **Objective:** Rebuild the classic SelfishNet utility from zero as a modern, standalone, portable
  Windows `.exe` with a **dual-engine architecture** (spec-locked *Legacy* engine + optimized
  *Modern* engine, switchable at runtime), a modern dark/light UI with live bandwidth charts, and
  bilingual support (English US / Latin American Spanish, hot-swappable).
- **Author/Owner:** Fely
- **Current phase (2026-10-19): FIELD-TESTED, HARDENED, OS-CERTIFIED BASE.** All layers implemented;
  build clean, **85/85 tests pass**, triple-arch portable exes (`publish/x64` ~68 MB,
  `publish/x86` ~63 MB, `publish/arm64` ~65 MB). Supported OS floor is declared (Win10 1607)
  and guard-tested; see `docs/WINDOWS_COMPATIBILITY.md`. Win7/XP are backlogged in `docs/PENDING.md`.
  First real-network test passed (both engines limited a phone, engine switching worked).
  Post-acceptance changes: reactive ARP defense + 2 s re-poison (Modern), registry-backed
  healing with broadcast gratuitous frames, heal-on-untarget/evict/adapter-switch, 60 s
  auto-discovery, dark-theme readability fix, single language button, unit-aware charts,
  x64+x86+arm64 publishing, `asInvoker` + driver-access probe (elevate only when needed),
  session-end healing (logoff/shutdown) and retry of outstanding heal claims on Close.
  Remaining polish comes from user feedback rounds.
- **Git:** repo initialized, remote `https://github.com/jfelya/selfishnet-v2`.
- **UI verification method (no admin/UAC needed):** a temporary console harness referencing the App
  assembly, calling `new App().InitializeComponent()`, constructing `MainWindow`, then
  `Measure/Arrange/UpdateLayout` + theme/command exercise. This caught a real bug — use
  **component-relative pack URIs** (`pack://application:,,,/{AssemblyName};component/...`) for theme
  dictionaries; application-relative URIs break when the shell is hosted by another exe.

---

## 2. Golden Rules (Carried Forward — Still Binding)
1. **Never alter or delete the original binaries** in `reference/original-app/`; they are the golden
   verification references.
2. **Byte-for-byte frame fidelity** with `CArp::buildArpPacket` / `CArp::redirector` is enforced via
   golden-vector parity tests (42-byte Ethernet II + ARP, offsets documented in §3).
3. Every UI string routes through `LocalizationManager.Get(key)` with `OnLanguageChanged` subscription.
4. **Driver safety:** check Npcap first (`C:\Windows\System32\Npcap\wpcap.dll`), fall back to WinPcap
   (`C:\Windows\System32\wpcap.dll`). Never register the obsolete 32-bit `npf.sys` on 64-bit Windows.
5. **Shutdown discipline:** `pcap_breakloop()` → join worker → `pcap_close()`. Never close a handle
   whose worker may be blocked in `pcap_next_ex()` (prototype bug C2).

---

## 3. Verified Original Mechanics (authoritative — supersedes old analysis where conflicting)
- **Capture:** non-promiscuous, `snaplen=65535` (F1). Timeout relaxed 1 ms → 20 ms for CPU.
- **Forwarder classification** is by Ethernet **source MAC**: local → count only; router → download
  (match DST IP); else → upload (match SRC MAC). `"ip"` BPF filter, no EtherType re-check (F2).
- **Spoof = 4 frames/victim** every 5000 ms: poison-to-victim, poison-to-router, self-refresh
  (re-teaches our listener the true router MAC), one inert leftover (F3).
- **Router MAC learning is active**: broadcast ARP request; listener learns from the Ethernet source
  field when sender IP == router IP (F4).
- **Caps:** `if (cap != 0 && cap <= bytesThisSecond) drop` — hard 1-second window in Legacy;
  token bucket (hi-res clock, clamped burst) in Modern (F6).
- **Unspoof:** 35 rapid genuine ARP frames per device on Stop/exit; also on eviction and engine switch.
- **Upload shaping requires the victim to be poisoned** — duplicate-forward quirk is original behavior (F5).

## 4. Prototype Defects That Must Not Recur (full detail: REVAMP_BLUEPRINT.md §1.2)
- **C1:** `pcap_pkthdr` layout — native Windows struct is 16 bytes with 32-bit `long`s
  (`int tv_sec; int tv_usec; uint caplen; uint len;` in C#). Prototype used 64-bit `long`s → garbage lengths.
- **C2:** `pcap_close` while worker blocked in `pcap_next_ex` → use-after-free.
- **C3:** evicting a silently-spoofed device orphans its poisoned ARP cache; 20 s keep-alive probe was dropped.
- **H1/H2:** per-packet allocations + `ToString()` identity in the hot path → use spans, pooling,
  `uint`/`ulong` keys.
- **H3:** token bucket must use `Stopwatch.GetTimestamp()` and never refill to 100% on cap change.
- **M2:** never forward to `FF:FF:FF:FF:FF:FF` when the gateway MAC is unknown — gate the forwarder.

---

## 5. Repository Structure (current, tidy)

```
SelfishNet v2/
├── AI_ENVIRONMENT_NOTES.md      # This file — session memory for AI agents
├── README.md                    # Human-facing project index (status, run, robustness)
├── docs/                        # All specifications & findings
│   ├── ARCHITECTURE_ANALYSIS.md    # Original decompilation report (see ORIGINAL_FINDINGS for corrections)
│   ├── ORIGINAL_FINDINGS.md        # Verified corrections from IL disassembly (authoritative)
│   ├── MODERNIZATION_ROADMAP.md    # Original phase plan (superseded by REVAMP_BLUEPRINT)
│   ├── LOCALIZATION_SPEC.md        # en-US / es-419 string matrix
│   ├── UNIT_NOTATIONS.md           # KByte/s vs KBit/s rules (storage: bytes/s integers only)
│   ├── PROTOTYPE_POSTMORTEM.md     # Why the prototype failed; failure-mode → fix map
│   └── REVAMP_BLUEPRINT.md         # ★ Current master plan: bugs, stack decision, dual-engine design
├── reference/                   # Read-only golden material — never modify
│   ├── original-app/               # SelfishNetv0.2-beta_vista.exe + its DLLs, npf.sys, docs
│   ├── redist/                     # Era installers (WinPcap, VC++ redists, .NET 4 web)
│   └── tools/                      # analyze_app.ps1, disasm.ps1
├── prototype/                   # Frozen "free model test" prototype (reference only, do not extend)
│   ├── README_FREE_MODEL_TEST.md
│   └── SelfishNetV2.Test/          # C#/.NET 8 WinForms source (+ published SelfishNetV2.exe)
├── src/                         # ★ The revamp (implemented)
│   ├── SelfishNetV2.sln
│   ├── SelfishNetV2.Core/          # DTOs, DeviceCatalog, ArpFrameBuilder, PacketClassifier, TokenBucket
│   ├── SelfishNetV2.Drivers/       # wpcap FFI, PcapCaptureSession, LoopbackDriver, ABI self-test
│   ├── SelfishNetV2.Engines/       # IEngineSession, EngineSessionBase, LegacyEngine, ModernEngine
│   ├── SelfishNetV2.Services/      # Localization, Settings, KnownDevices, EngineOrchestrator
│   ├── SelfishNetV2.App/           # WPF shell (themes, charts, view models, views)
│   ├── SelfishNetV2.Tests/         # 75 tests (parity, ABI, engines, swap, hammer, formatter)
│   └── publish.bat                 # → ..\publish\SelfishNetV2.exe
└── publish/                     # portable exe output (git-ignored)
```

## 6. Environment Facts
- OS: Windows (win32), working dir: `C:\Users\Fely\Documents\OpenCode\SelfishNet v2` (not a git repo).
- Installed toolchains: **.NET SDK 8.0.425 only** (no Rust, no Go, no C++/cmake as of 2026-10-18).
- Recommended stack (pending confirmation): C# / .NET 8 / WPF, self-contained single-file `win-x64`.
- The app requires Administrator (raw packet injection) and Npcap or WinPcap on the target machine.
