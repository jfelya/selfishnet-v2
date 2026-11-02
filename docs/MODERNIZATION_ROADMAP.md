# SelfishNet Modernization Roadmap & Technical Plan

## 1. Project Goals & Constraints

1. **Behavioral Integrity (Do Not Break the Core):**
   - The original ARP spoofing, bidirectional Man-in-the-Middle, MAC/IP rewriting, and packet forwarding mechanics are mathematically verified and must remain 100% true to the original logic.
2. **Modern Windows & Driver Compatibility:**
   - Seamless operation on Windows 10 & Windows 11 (64-bit and 32-bit).
   - Dynamic binding to modern **Npcap** (preferred standard) or legacy **WinPcap** (if installed).
   - No hard crashes from obsolete 32-bit kernel driver loading attempts.
3. **Visual & UI Modernization:**
   - Crisp, high-DPI aware interface with clear typography and responsive layout.
   - Clean iconography, status indicators, and real-time bandwidth meters/graphs.
   - Professional look while maintaining the intuitive simplicity of the classic layout.
4. **Bilingual Engine (English & Latin American Spanish):**
   - Seamless, hot-swappable language switching without restarting the app or interrupting ongoing network operations.
5. **Traffic Shaping & Concurrency Optimization:**
   - Replace the original bursty 1-second drop threshold with a smooth Token Bucket algorithm to eliminate packet burst jitter.
   - Lock-free / atomic thread safety for packet counters and device lookup maps.

---

## 2. Technology Stack & Packaging Strategy

### 2.1 Technology Options Evaluated

| Option | Pros | Cons | Recommendation |
| :--- | :--- | :--- | :--- |
| **A. Modern .NET C# (WPF or WinForms with Modern Theming)** | Direct 1:1 mapping with original architecture; native P/Invoke to `Packet.dll`/`wpcap.dll` or modern `SharpPcap`; single-file self-contained `.exe`; DPI-aware. | Requires .NET Runtime or self-contained bundling (~50MB). | **Recommended (Primary)** |
| **B. Native C++ (Win32 / C++20 / ImGui or Modern WinUI)** | Zero runtime dependencies; tiny binary footprint (~3-5MB); maximum raw packet speed. | Higher UI complexity for data grids and localization. | Good alternative for embedded tools. |
| **C. Modern C# (.NET Framework 4.8 / .NET 8 / 9)** | Native Windows component; pre-installed on every Windows 10/11 system without separate runtime installation. | Slightly older UI framework if using vanilla WinForms, but solved with modern custom rendering. | **Optimal for zero-install distribution.** |

### 2.2 Selected Architecture: Modern .NET (C#) with Npcap/WinPcap Dynamic P/Invoke
- **Why this preserves the original faithfully:**
  - The original application was already a .NET Windows Forms assembly compiled via C++/CLI.
  - Rewriting in modern clean C# eliminates the obsolete MSVC 2005 C++/CLI CRT dependencies (`Microsoft.VisualC 8.0.0.0`) and 32-bit `PcapNet.dll` limitations.
  - Dynamic P/Invoke dynamically links to either `C:\Windows\System32\Npcap\wpcap.dll` / `Packet.dll` or standard system `wpcap.dll`, making it work natively on 64-bit Windows without needing admin driver service hacks.

---

## 3. Detailed Component Plan

```
┌────────────────────────────────────────────────────────┐
│                      Presentation                      │
│   Modern Bilingual UI (DPI Aware, English / Spanish)   │
│   Toolbar, Device Table, Live Bandwidth Meters, Tray   │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│                 Application Services                   │
│   LocalizationManager  •  Settings  •  HostResolver    │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│                 Network Control Core                   │
│   • ArpEngine: Subnet scanner, Spoof/Unspoof worker    │
│   • TrafficShaper: Token Bucket rate limiter           │
│   • PacketForwarder: Layer 2/3 frame rewriting engine  │
│   • DeviceManager: Concurrent device catalog & stats   │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│              Driver & Packet Abstraction               │
│   Dynamic Native Driver Interop (Npcap / WinPcap)      │
│   wpcap.dll / Packet.dll (64-bit and 32-bit native)    │
└────────────────────────────────────────────────────────┘
```

### 3.1 Network Engine Improvements

1. **Subnet ARP Scanner:**
   - Retains the exact ARP Request construction (`opcode = 1`), with configurable thread pacing (e.g. 2–5 ms per probe) and NetBIOS / mDNS / DNS reverse resolution for hostnames.
2. **ARP Poisoning (Spoofing):**
   - Implements the exact twin-spoof packet pair (Victim <-> LocalHost <-> Gateway).
   - Re-poison interval is kept at 5000 ms to overcome modern OS ARP cache refreshes.
   - Clean shutdown handler (`UnSpoof`) sends 35 rapid genuine ARP frames on exit or stop to immediately heal the network.
3. **Smooth Traffic Shaping (Token Bucket Algorithm):**
   - Original formula:
     $$\text{if } \text{BytesThisSecond} > \text{CapBytes} \implies \text{Drop}$$
   - Modernized Token Bucket:
     Tokens are added continuously based on elapsed milliseconds:
     $$\text{tokens} = \min(\text{capacity}, \text{tokens} + \text{rate} \times \Delta t)$$
     If packet length $\le$ tokens, forward packet and deduct tokens. Otherwise, queue or drop. This prevents bursty disconnects and ensures a steady bandwidth flow.

---

## 4. Phase-by-Phase Execution Plan

### Phase 1: Specifications & Model Base (Completed)
- [x] Reverse engineer original binary (`SelfishNetv0.2-beta_vista.exe` & `PcapNet.dll`).
- [x] Document frame formats, offsets, state machines, and timers.
- [x] Produce complete bilingual dictionary (English / Spanish).

### Phase 2: Core Packet Engine & Driver Interop
- [ ] Implement driver detection and dynamic link loader (`Npcap` and `WinPcap`).
- [ ] Implement raw packet capture and transmission layer (`pcap_open_live`, `pcap_sendpacket`, `pcap_next_ex`).
- [ ] Implement ARP engine (`ArpPacket`, `ArpScanner`, `ArpSpoofer`).
- [ ] Implement packet forwarder and token bucket shaper.

### Phase 3: Hot-Swappable Localization Engine
- [ ] Implement `LocalizationManager` with English and Latin American Spanish resource sets.
- [ ] Connect dynamic string binder to controls and status messages.

### Phase 4: Modern User Interface
- [ ] Create clean, DPI-aware main window layout:
  - High-res SVG/Vector or clean icons for Discovery, Start, Stop, Adapter, Settings.
  - Data table with columns: Name, IP, MAC, Download Speed, Upload Speed, Down Limit, Up Limit, Block Checkbox, Spoof Checkbox.
  - Quick action toolbar (Language dropdown, Global Block All, Reset All).
  - Adapter selector dialog with gateway detection and IP forwarding indicator.
  - Status strip with real-time total network throughput.

### Phase 5: Verification & Testing
- [ ] Test on Windows 10 & 11 with Npcap.
- [ ] Test gateway auto-discovery on Ethernet and Wi-Fi.
- [ ] Verify instant bilingual language switching without breaking capture.
- [ ] Verify clean unspoof on application exit.
