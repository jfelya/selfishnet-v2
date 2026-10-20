# SelfishNet v2 — Modern Revamp

A from-scratch rebuild of the classic SelfishNet LAN bandwidth controller (ARP spoofing + traffic
shaping), shipped as a **single portable Windows executable**.

## User guide (plain-language, how it works under the hood)

| Language | File |
|---|---|
| English | [`GUIDE_EN.txt`](GUIDE_EN.txt) |
| Español (Latinoamérica) | [`GUIDE_ES.txt`](GUIDE_ES.txt) |

## Windows support

| OS | Status |
|---|---|
| Windows 11 (all releases) | ✅ Supported |
| Windows 10 1607 → 22H2 (all releases) | ✅ Supported (1607 = .NET 8 runtime floor) |
| Windows 10/11 x64 or 32-bit | ✅ `publish/x64` · `publish/x86` |
| Windows 8.1 / 7 SP1 | ❌ Not in the modern build — see `docs/PENDING.md` (P1) |
| Windows XP SP3 | ❌ Prefer the preserved original build in `reference/original-app-xp/` (`docs/PENDING.md` P2) |

The Windows 10 1607 floor is declared in `src/Directory.Build.targets`, enforced by a guard test,
and the platform analyzer flags any API newer than it. Details and caveats:
`docs/WINDOWS_COMPATIBILITY.md`.

The app starts **without administrator rights** when the Npcap driver allows it, and offers a
single elevated restart only if the driver is restricted.

## Status — feature-complete rebuild (v2.0.0)

| Area | State |
|---|---|
| Core (device catalog, frame builder, classifier, token bucket) | ✅ |
| Drivers (dynamic Npcap/WinPcap FFI, guarded capture sessions, ABI self-test) | ✅ |
| Legacy engine (spec-locked 2007 mechanics) | ✅ |
| Modern engine (token-bucket, VLAN-aware, zero-alloc hot path) | ✅ |
| Runtime engine switch (UI toggle, auto-heal before swap) | ✅ |
| WPF shell (dark/light, live charts, per-device caps/block, 4 Hz speeds) | ✅ |
| Localization (en-US / es-419, hot-swappable) | ✅ |
| Auto-discovery (60 s background sweep + passive ARP learning) | ✅ |
| Robust healing (registry-backed, broadcast gratuitous, gateway re-resolve) | ✅ |
| Reactive ARP defense (Modern engine) | ✅ |
| Automated tests | ✅ 85/85 green |

**Portable build:** run `src/publish.bat` → two self-contained exes:
`SelfishNetV2-x64.exe` (~68 MB) and `SelfishNetV2-x86.exe` (~63 MB), also copied to the
project root for convenience. No .NET install needed — copy one anywhere.

> Why two files? A self-contained .NET app embeds its runtime, and the runtime is
> architecture-specific, so one exe cannot cover both 32-bit and 64-bit Windows. Same
> source, two builds. (A single ~5–10 MB framework-dependent exe is possible if the
> .NET 8 Desktop Runtime is installed — that breaks the zero-dependency goal.)

## Run

1. Install **Npcap** (https://npcap.com) if not already present — the only external prerequisite.
   Not sure? Just run the app: it opens normally and shows a **Download Npcap** button.
2. Double-click `SelfishNetV2-x64.exe`. It starts **without** elevation; if your Npcap install
   restricts the driver to administrators, it offers a one-click elevated restart.
3. Pick the adapter → set caps or **Block** per device → **Start Shaping**. Every discovered
   device is shaped automatically (no per-device spoof toggle). A background sweep every 60 s
   also picks up devices that join later; you never have to rescan manually.
4. Toggle **Legacy ⇄ Modern** at any time; the app heals ARP tables before switching engines.
5. **Stop** (or close the app) restores the LAN: 35 genuine ARP pairs per spoofed device,
   plus broadcast gratuitous announcements so every cache holder is corrected.

## Robustness notes

- **Poison that sticks:** Modern re-poisons every 2 s (Legacy: original 5 s) and answers
  ARP races instantly — when a victim asks for the gateway, when the gateway replies to a
  victim, or when a victim announces itself.
- **Healing can't miss a device:** every poisoned device is tracked in a registry independent
  of the device list, so stop/switch/eviction always repairs what was actually poisoned. If the
  gateway MAC was lost, it is re-resolved before healing; failures keep the claim for a retry on
  close. Heal sends are paced (Realtek/Npcap reject dense bursts with error 31) and their failures
  are counted silently instead of raising dialogs. Windows logoff/shutdown heals synchronously
  (3 s bound) before the process exits. Restoration is best-effort by nature: force-kills and
  devices with static ARP or ARP filtering cannot be corrected.
- **Crash-resistant under bursts:** capture parsing is zero-allocation over the native buffer,
  the forwarding buffer is bounds-checked and reused, and per-packet exceptions can never kill
  the capture thread (regression-tested with a 10 000-frame 1514-byte burst).
- **Survives MAC randomization and DHCP churn:** if a phone rotates its Wi-Fi MAC, saved limits
  and names are matched by MAC, then by last IP, then by hostname — the cap follows the device
  instead of getting stuck on an old entry.
- **No silent failures:** a gateway-less adapter is rejected with a clear message (instead of
  "starting" without spoofing), packet-send failures are surfaced, and *About* shows live
  diagnostics (adapter, gateway MAC, state, targets, forwarded/dropped, send failures).
- **Comfort features:** per-device speeds refresh at 4 Hz, limit boxes accept digits only and
  commit empty as 0, and **Reset limits** returns every device to unlimited in one click.

## Build / test (developer)

```powershell
dotnet build src/SelfishNetV2.sln -c Release      # build everything
dotnet test  src/SelfishNetV2.sln -c Release      # 85 tests (no hardware/admin needed)
src/publish.bat                                    # self-contained exes (x64 + x86)
tools\clean.cmd                                    # reclaim build output (~1 GB after publishing)
```

Requirements: .NET SDK 8, Windows 10/11, Npcap (or WinPcap) for live packet I/O.

## Folder map

| Path | Contents |
|---|---|
| `AI_ENVIRONMENT_NOTES.md` | **Project context / single source of truth** — read this first |
| `docs/` | Findings, master plan (`REVAMP_BLUEPRINT.md`), OS support, postmortem, backlog |
| `GUIDE_EN.txt` / `GUIDE_ES.txt` | Plain-language user guides (English / Spanish) |
| `SelfishNetV2-*.exe` | Portable executables at the root — versioned, clone-and-run |
| `reference/original-app/`, `-xp/` | Untouched 2007 golden binaries (verification reference) |
| `reference/redist/`, `reference/tools/` | Era installers and reverse-engineering scripts |
| `prototype/` | Frozen proof-of-concept (reference only — not the final app) |
| `src/` | The revamp: Core · Drivers · Engines · Services · App (WPF) · Tests |
| `tools/` | `clean.cmd` (reclaim space), history-purge script, artifact policy |
| `publish/` | Build output (git-ignored) |

## Repository hygiene

Source, docs, reference material and the two **portable executables at the root**
(`SelfishNetV2-x64.exe`, `SelfishNetV2-x86.exe`) are versioned, so a fresh clone is ready to run.
Build output (`bin/`, `obj/`, `publish/`) is ignored; run `tools\clean.cmd` after publishing
sessions. See `tools/README.md`.

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
