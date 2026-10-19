# SelfishNet v2 — Modern Revamp

A from-scratch rebuild of the classic SelfishNet LAN bandwidth controller (ARP spoofing + traffic
shaping), targeting a **single portable Windows executable** with:

- **Dual-engine core** — a spec-locked *Legacy Engine* replicating the original 2007 mechanics, and an
  optimized *Modern Engine* (multi-threaded, zero-allocation packet path, smooth token-bucket shaping),
  switchable at runtime from the UI.
- **Modern UI** — dark/light themes, real-time per-device bandwidth charts, responsive layout.
- **Bilingual** — English (US) / Español Latinoamericano, hot-swappable without restarting.

## Folder map

| Path | Contents |
|---|---|
| `docs/` | Specifications, findings, and the **master plan** → start at `docs/REVAMP_BLUEPRINT.md` |
| `reference/original-app/` | Untouched 2007 golden binaries (verification reference) |
| `reference/redist/` | Era installers the original app depended on |
| `reference/tools/` | Reverse-engineering PowerShell tools |
| `prototype/` | Frozen proof-of-concept (reference only — not the final app) |
| `src/` | The clean rebuild (scaffolded after stack confirmation) |

## Status

Analysis phase complete — see `docs/REVAMP_BLUEPRINT.md` for the bug synthesis, the language/framework
decision, and the dual-engine architecture blueprint. Code scaffolding begins once the stack is confirmed.

## Safety / ethics

ARP spoofing intercepts other devices' traffic. Only use on networks you own or are explicitly
authorized to test. Stopping (or closing) the app heals the LAN by broadcasting genuine ARP frames
(35 per device), matching the original's `completeUnspoof()`.
