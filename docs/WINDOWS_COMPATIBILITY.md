# Windows Compatibility & Administrator Rights

## Current build — support matrix

| OS | Status | How |
|---|---|---|
| Windows 11 (x64) | ✅ Supported | `publish/x64/SelfishNetV2.exe` |
| Windows 10 1607+ (x64) | ✅ Supported | `publish/x64/SelfishNetV2.exe` |
| Windows 10 32-bit (x86) | ✅ Supported | `publish/x86/SelfishNetV2.exe` |
| Windows 8.1 / 8 / 7 SP1 | ❌ Not supported by the modern build | See options below |
| Windows Vista / XP SP3 | ❌ Not supported by the modern build | Original 2007 build preserved in `reference/original-app-xp/` |

The x86 executable also runs on 64-bit Windows (WOW64), but there is no reason to use it there.

## Why one binary cannot cover Windows XP → 11

The app is a **self-contained .NET 8 application**: the .NET runtime is embedded inside the
executable. That sets a hard floor at the operating systems .NET 8 supports — Windows 10 1607+
and Windows 11. It is not a packaging choice; the runtime itself does not load on older kernels.

The other end of the range has the same problem in reverse:

- Windows XP supports **.NET Framework up to 4.0** (4.8 requires Windows 7 SP1+).
- Our code uses .NET 8 APIs and C# 12 features that do not exist there: `Span<T>`-based packet
  parsing, unmanaged function pointers (`delegate*`) for wpcap, `Environment.TickCount64`,
  `Task.WaitAsync`, records, etc.
- Single-file publishing does not exist for .NET Framework; it is framework-dependent by nature.

So "one exe from XP to 11" is technically impossible. The realistic maximum is: **one modern
build for Win10/11** (current) plus, if ever needed, **one legacy-target build** for older systems.

## The preserved XP build

`reference/original-app-xp/` contains the untouched 2007 **Windows XP** release
(`SelfishNetv0.2-beta.exe` + `PcapNet.dll`, `Packet.dll`, `WanPacket.dll`, 32-bit `npf.sys`, etc.).
It is reference material, not part of the modern app:

- It requires **.NET Framework 2.0/3.5** and **WinPcap 4.1.3** (installer available in `reference/redist/`).
- It is 32-bit, unsigned, and unsupported by its original authors today.
- It is the practical way to use the tool on XP without maintaining a second codebase.

XP users should run that executable. It is deliberately **not** merged into `src/` or into the
modern build: mixing 2007 binaries into the modern product would damage both the architecture
and the trust story of the revamp.

## If support for older Windows becomes a requirement

Three options, in increasing cost:

1. **Keep the current split (recommended).** Modern app for Win10/11; original XP build for XP.
   Zero maintenance, zero risk to the modern codebase.

2. **Add a .NET Framework 4.8 build for Windows 7 SP1 – 11** (not XP).
   A second project in the same solution reusing the same architecture. The driver layer must
   swap function pointers for delegates and spans for arrays; the UI port is mostly mechanical.
   Still a separate, framework-dependent artifact (no single-file). Doesn't help XP.

3. **Add a .NET Framework 4.0 build for XP SP3 – 11** (full range).
   Requires the same port as option 2 plus WPF 4.0-era UI work and a WinPcap-only driver path.
   .NET 4.0 is long out of support and XP is an unpatched OS; this is a security and maintenance
   liability, not just engineering work. Only worth it for a real, sizeable XP user base.

The architecture already keeps the OS boundary behind `IPcapDriver` and the engines UI-agnostic,
so option 2 or 3 would not require redesigning the app — but each is a **separate executable**
and a substantial compatibility effort, not a switch we can flip.

## Administrator rights

The app now starts with `asInvoker` — **no elevation by default**.

Raw ARP capture and injection go through the Npcap/WinPcap kernel driver. Whether a standard
user may open that driver is decided at **Npcap install time**:

- If Npcap was installed **with** *"Restrict Npcap driver's access to Administrators only"*
  (the common default), a standard user gets an access-denied error.
- If that option was **cleared**, standard users can capture and send — the app runs fully
  unelevated.

What the app does at startup:

1. Loads the driver and opens a throwaway capture handle to test access.
2. **Access allowed** → runs normally, no UAC prompt ever.
3. **Access denied** → shows a bilingual dialog offering to restart elevated (a single UAC
   prompt). Declining keeps the app open in a read-only state with a status-bar warning.

If access is lost mid-session (for example the driver was reinstalled), the engine reports an
`AccessDenied` error and the same restart prompt appears.

Notes and caveats:

- The elevated restart launches a **new process**; the unelevated one closes. Settings and device
  memory are shared on disk, so nothing is lost.
- The UAC prompt appears **only when the driver requires it** — not on every launch.
- The app cannot bypass driver permissions; `asInvoker` only removes our own requirement, it does
  not grant access the driver refuses.
- If you want guaranteed elevation, run it from an elevated terminal or right-click → *Run as
  administrator*; the probe will see access as allowed.
