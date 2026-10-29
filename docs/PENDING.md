# Pending / Backlog

Items consciously deferred. Nothing here blocks the current Windows 10/11 builds.

---

## P1 — Windows 7 SP1 support (optional legacy build)

**Goal:** run the same engine stack on Windows 7 SP1 through Windows 11 from a *separate* artifact.

**Why it is not in the current build:** the modern app is self-contained .NET 8, whose runtime
requires Windows 10 1607+. Windows 7 SP1 caps at .NET Framework 4.8 (and 4.8 is the last
supported line for 8.1/7). One binary cannot span both; a second, framework-dependent build is
the only realistic path.

**Tasks**
- [ ] Add a `SelfishNetV2.Legacy` WPF project targeting `net48` (Windows 7 SP1+).
- [ ] Port the driver interop: replace `delegate*` function pointers with
      `[UnmanagedFunctionPointer(CallingConvention.Cdecl)]` delegates.
- [ ] Replace `Span<T>`-based packet parsing with offset/array parsing (or add the
      `System.Memory` package and evaluate the hot-path cost).
- [ ] Replace .NET Core-only APIs (`Environment.TickCount64`, `Task.WaitAsync`, records,
      collection expressions) with equivalents.
- [ ] WPF 4.8 XAML compatibility pass over themes, templates and the chart control.
- [ ] Publish framework-dependent (single-file does not exist on .NET Framework).
- [ ] Packet driver: Npcap on Windows 7, or WinPcap 4.1.3 as fallback.
- [ ] Re-run the full engine/parity test suite against the legacy build.
- [ ] Test matrix: Windows 7 SP1 x86 and x64 (VMs).

**Effort:** medium–large. **Payoff:** Windows 7 SP1 → 11 users. **Risk:** two artifacts to
maintain and a second test matrix.

**Acceptance:** same features and the same healing guarantees as the modern build on Windows 7 SP1.

---

## P2 — Windows XP SP3 support (only if a real user base appears)

**Goal:** range coverage from XP to 11.

**Why it is not in the current build:** XP supports .NET Framework **4.0** at most (4.8 requires
Windows 7 SP1+). Reaching it means giving up `Span<T>`, function pointers, modern concurrency
primitives and modern C#, plus WPF 4.0-era UI work — effectively a compatibility fork of the whole
app. XP is also unpatched, so this carries a permanent security and maintenance burden.

**Tasks**
- [ ] Target `net40` (or maintain a separate legacy codebase) and remove all C# 8+ language features.
- [ ] Rewrite the engine's hot path and concurrency primitives without `Span`/`ValueTask`.
- [ ] WPF 4.0 UI compatibility pass (templates, resources, chart rendering).
- [ ] WinPcap 4.1.3 only (32-bit `npf.sys`); document the driver install flow.
- [ ] Framework-dependent publish (requires .NET Framework 4.0 on the target).
- [ ] Test matrix: Windows XP SP3 x86 (VM).

**Effort:** large. **Risk:** high (unsupported OS, security, maintenance).
**Alternative:** the old 2007 XP build is no longer included in this repository
(removed 2026-10-29). Obtain the original release separately if XP support is
needed; it is not part of the modern build.

**Decision gate:** proceed only if XP usage is demonstrated by real user reports.

---

## P3 — Other deferred items

- [ ] **Mesh / multi-MAC routers** (blueprint item M4): track a *set* of gateway identities instead
      of a single gateway MAC, so ISPs with per-SSID or failover gateway MACs keep working.
- [ ] **Configurable auto-discovery interval** (currently fixed at 60 s) — add a setting if the
      sweep ever proves noisy on large Wi-Fi networks.
- [ ] **Optional installer** (MSIX/Inno) for distribution outside a portable folder — only if
      required; the portable exe remains the primary artifact.
- [ ] **Chart export** (PNG/CSV of the bandwidth history) — convenience feature.
- [ ] **Signed binaries** — the portable exe is unsigned, so Windows SmartScreen warns on first
      run; code signing is a distribution decision, not a code change.
