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
| Automated tests | ✅ 75/75 green |

**Portable build:** run `src/publish.bat` → `publish/SelfishNetV2.exe` (~72 MB, self-contained,
no .NET install needed). Copy that single file anywhere.

## Run

1. Install **Npcap** (https://npcap.com) if not already present — the only external prerequisite.
2. Double-click `SelfishNetV2.exe` (Windows asks for Administrator — raw packet injection needs it).
3. Pick the adapter → **Discover** → set caps/block/spoof per device → **Start Shaping**.
4. Toggle **Legacy ⇄ Modern** at any time; the app heals ARP tables before switching engines.
5. **Stop** (or close the app) restores the LAN: 35 genuine ARP frames per targeted device.

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
