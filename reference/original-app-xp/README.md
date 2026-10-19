# Original SelfishNet v0.2-beta — Windows XP build (reference only)

This folder preserves the untouched **Windows XP** release of the classic SelfishNet, kept as a
golden reference and as the practical way to run the tool on XP-era systems.

## Contents

| File | Role |
|---|---|
| `SelfishNetv0.2-beta.exe` | Original 2007 XP build (.NET 2.0, 32-bit, C++/CLI) |
| `PcapNet.dll`, `Packet.dll`, `WanPacket.dll`, `npf.sys` | Original WinPcap bridge + 32-bit driver |
| `ExpandableGridView.dll`, `DataGridViewNumericUpDownElements.dll` | Original UI components |
| `HlpIndex.html`, `Readme.txt`, `release_notes.txt`, `LicenseYouAccepted.txt` | Original documentation/license |

## Running it on Windows XP

1. Install **.NET Framework 2.0 or 3.5** (3.5 includes the 2.0 runtime).
2. Install **WinPcap 4.1.3** — the installer is in `../redist/` (or `../original-app/`).
3. Run `SelfishNetv0.2-beta.exe` as Administrator.

This build is 32-bit, unsigned and long unsupported by its original authors. Use it only on
networks you own or are authorized to test.

## Why it is separate

The modern revamp (`src/`) targets .NET 8, whose runtime requires **Windows 10 1607+**. A single
executable cannot cover XP through 11; the full reasoning and the options for older-Windows
support are documented in `../../docs/WINDOWS_COMPATIBILITY.md`.

Do **not** merge these binaries into `src/` or into the modern build — they exist purely as the
historical reference the revamp was verified against.
