# SelfishNet v2 — Modern Revamp

A from-scratch rebuild of the classic SelfishNet LAN bandwidth controller (ARP spoofing + traffic
shaping), shipped as a **single portable Windows executable**.

## Status — feature-complete rebuild (v2.0.0)

| Area | State |
|---|---|
| Core (device catalog, frame builder, classifier, token bucket) | ✅ |
| Drivers (dynamic Npcap/WinPcap FFI, guarded capture sessions, ABI self-test) | ✅ |
| Legacy engine (spec-locked 2007 mechanics) | ✅ |
| Modern engine (token-bucket, VLAN-aware, zero-alloc hot path) | ✅ |
| Runtime engine switch (UI toggle, auto-heal before swap) | ✅ |
| WPF shell (dark/light, live charts, per-device caps/block/spoof) | ✅ |
| Localization (en-US / es-419, hot-swappable) | ✅ |
| Auto-discovery (60 s background sweep + passive ARP learning) | ✅ |
| Robust healing (registry-backed, broadcast gratuitous, gateway re-resolve) | ✅ |
| Reactive ARP defense (Modern engine) | ✅ |
| Automated tests | ✅ 81/81 green |

**Portable build:** run `src/publish.bat` →
`publish/x64/SelfishNetV2.exe` (~68 MB) and `publish/x86/SelfishNetV2.exe` (~63 MB).
Each is a single self-contained file — no .NET install needed. Copy either one anywhere.

> Why two files? A self-contained .NET app embeds its runtime, and the runtime is
> architecture-specific. One exe cannot cover both 32-bit and 64-bit Windows; the same
> source builds both. (A single ~5–10 MB framework-dependent exe is possible if the
> .NET 8 Desktop Runtime is installed — that breaks the zero-dependency goal.)

## Run

1. Install **Npcap** (https://npcap.com) if not already present — the only external prerequisite.
2. Double-click `SelfishNetV2.exe` (Windows asks for Administrator — raw packet injection needs it).
3. Pick the adapter → set caps/block/spoof per device → **Start Shaping**. (A background
   sweep every 60 s also picks up devices that join later; you never have to rescan manually.)
4. Toggle **Legacy ⇄ Modern** at any time; the app heals ARP tables before switching engines.
5. **Stop** (or close the app) restores the LAN: 35 genuine ARP pairs per spoofed device,
   plus broadcast gratuitous announcements so every cache holder is corrected.

## Robustness notes

- **Poison that sticks:** Modern re-poisons every 2 s (Legacy: original 5 s) and answers
  ARP races instantly — when a victim asks for the gateway, when the gateway replies to a
  victim, or when a victim announces itself.
- **Healing can't miss a device:** every poisoned device is tracked in a registry independent
  of its current "Spoof" checkbox, so un-checking a device mid-session heals it immediately,
  and stop/switch/eviction always repairs what was actually poisoned. If the gateway MAC was
  lost, it is re-resolved before healing; failures keep the claim for a retry on close.
- **Crash-resistant under bursts:** capture parsing is zero-allocation over the native buffer,
  the forwarding buffer is bounds-checked and reused, and per-packet exceptions can never kill
  the capture thread (regression-tested with a 10 000-frame 1514-byte burst).

## Build / test (developer)

```powershell
dotnet build src/SelfishNetV2.sln -c Release      # build everything
dotnet test  src/SelfishNetV2.sln -c Release      # 75 tests (no hardware/admin needed)
src/publish.bat                                    # single-file portable exe
```

Requirements: .NET SDK 8, Windows 10/11 x64, Npcap (or WinPcap) for live packet I/O.

## Folder map

| Path | Contents |
|---|---|
| `docs/` | Specifications, findings, and the **master plan** → `docs/REVAMP_BLUEPRINT.md` |
| `reference/original-app/` | Untouched 2007 golden binaries (verification reference) |
| `reference/redist/` | Era installers the original app depended on |
| `reference/tools/` | Reverse-engineering PowerShell tools |
| `prototype/` | Frozen proof-of-concept (reference only — not the final app) |
| `src/` | The revamp: Core · Drivers · Engines · Services · App (WPF) · Tests |
| `publish/` | Portable exe output (git-ignored) |

## Architecture in one paragraph

The app core owns a device catalog and two interchangeable engine sessions behind one
`IEngineSession` contract. Both engines emit **byte-identical** ARP frames (parity-tested) and share
capture/forwarding/shutdown discipline; they differ only in shaping policy (hard 1-second window vs.
token bucket), spoof burst shape (4 frames incl. the original inert leftover vs. 3 functional frames),
and frame classification (fixed offsets vs. VLAN-aware). Switching engines stops shaping, heals ARP
(35× per target), tears down handles with `breakloop → join → close`, builds the other engine and
resumes — devices, caps and names survive the swap.

## Safety / ethics

ARP spoofing intercepts other devices' traffic. Only use on networks you own or are explicitly
authorized to test. Stopping (or closing) the app heals the LAN by broadcasting genuine ARP frames
(35 per device), matching the original's `completeUnspoof()`.
