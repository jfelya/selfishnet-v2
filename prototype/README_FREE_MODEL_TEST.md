# SelfishNet v2 — FREE MODEL TEST PROTOTYPE

> ⚠️ **This folder (`Free model test/`) is an isolated prototype built to test a free model's limits.**
> It is NOT the final app. The main folder (`SelfishNet v2/`) is left 100% intact as the golden reference.
> If this prototype fails, delete this folder and rebuild with a premium model — the main app is untouched.

- Title bar carries `[FREE MODEL TEST]` suffix so it is always identifiable.
- All source headers carry `FREE MODEL TEST` markers.
- Behavior target: faithful to `docs/ARCHITECTURE_ANALYSIS.md`, `docs/LOCALIZATION_SPEC.md`,
  `docs/MODERNIZATION_ROADMAP.md` from the main folder.

## What this prototype is

- **Stack:** C# / .NET 8 WinForms, no external NuGet packages.
- **Packet mode:** REAL only (real ARP scan / spoof / forward via Npcap or WinPcap `wpcap.dll`).
  Requires Administrator + Npcap (or WinPcap) installed.
- **Spoof model:** Start spoofs **all** discovered devices at once (no per-device column).
- **Shaping:** Token-Bucket smooth rate limiter.
- **Memory:** custom names, caps and blocked flags persist by MAC in
  `%AppData%/SelfishNetV2.FreeModelTest/known_devices.json`.
- **Units:** speeds/limits toggle between `KB/s` and `Kb/s`; see `UNIT_NOTATIONS.md`.
- **Languages:** English (US) + Español Latinoamericano (es-419), hot-swappable without restart.
- **Platform:** Windows 10/11 64-bit. Tested target: Windows 11.

## Run

No terminal needed for daily use:

- **Portable exe (recommended):** run `publish.bat` once, then double-click
  `Free model test/SelfishNetV2.Test/SelfishNetV2.exe`
  (~150 MB, needs no .NET install). Windows asks for Administrator automatically.
  Copy that single file anywhere and run it.
- **From source:** open an **Administrator** terminal in `SelfishNetV2.Test/`:
  `build.bat` + `run.bat`, or `dotnet build -c Release` / `dotnet run -c Release`.

In the app: `Adapter → Discover → Start`. Default language is Español (Latam);
switch anytime from the toolbar dropdown. Stopping (or closing) restores ARP tables.

## Layout

```
Free model test/
  README_FREE_MODEL_TEST.md
  SelfishNetV2.Test/
    SelfishNetV2.Test.csproj
    Program.cs
    app.manifest
    build.bat / run.bat
    Core/      ArpPacket, NpcapInterop, Device, DeviceManager,
               NetworkInterfaces, ArpEngine, PacketForwarder,
               TokenBucket, HostResolver
    Services/  LocalizationManager, AppSettings
    UI/        MainForm, AdapterDialog
```

## Safety / ethics

ARP spoofing intercepts other devices' traffic. Only use on networks you own
or are explicitly authorized to test. Stopping the app (or closing it) triggers
`completeUnspoof()` — 35 genuine ARP frames per device — to heal the LAN.
