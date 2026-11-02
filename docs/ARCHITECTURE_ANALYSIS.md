# SelfishNet v0.2 Architecture & Reverse-Engineering Analysis

## 1. Overview & Origins
- **Original Name:** SelfishNet v0.2-beta (release date approx. June 2007)
- **Authors:** Benjamin Lassort & Clément Chazarra (Creadev.net)
- **Primary Function:** Local Area Network (LAN) bandwidth management, traffic shaping, and device blocking using ARP cache poisoning (spoofing) and packet forwarding.
- **Runtime Environment:** .NET Framework 2.0 (x86 32-bit), compiled with C++/CLI (Visual C++ 2005 / MSVC 8.0 CRT).
- **Target OS at release:** Windows XP, Windows Vista.

---

## 2. Binary Inventory & Dependencies

| Binary / File | Type | Role |
| :--- | :--- | :--- |
| `SelfishNetv0.2-beta_vista.exe` | .NET 2.0 Assembly (x86 C++/CLI) | Main application UI, state machine, timers, and orchestration. |
| `PcapNet.dll` | .NET 2.0 Assembly (x86 C++/CLI) | Managed bridge to native WinPcap (`Packet.dll` & `wpcap.dll`). Controls driver service registration. |
| `Packet.dll` | Native Win32 DLL | Low-level WinPcap packet driver interface. |
| `WanPacket.dll` | Native Win32 DLL | WinPcap WAN packet wrapper. |
| `npptools.dll` | Native Win32 DLL | Network Packet Provider Vista compatibility helper. |
| `npf.sys` | 32-bit Kernel Driver | Netgroup Packet Filter (WinPcap kernel driver). *Incompatible with 64-bit Windows Driver Signature Enforcement*. |
| `ExpandableGridView.dll` | .NET 2.0 Assembly (C#) | `AdvancedDataGridView.TreeGridView` component (hierarchical grid). |
| `DataGridViewNumericUpDownElements.dll` | .NET 2.0 Assembly (C#) | Custom DataGridView cell and column for numeric up/down spinners. |

---

## 3. Internal Decompilation & Class Breakdown

### 3.1 Namespace: `SelfishNet10`

#### 1. `PC` Class
Represents a network node detected on the LAN.
- `IPAddress ip`: Device IPv4 address.
- `PhysicalAddress mac`: Device MAC address (6 bytes).
- `string name`: Hostname / NetBIOS name (resolved asynchronously via DNS/NetBIOS).
- `bool isGateway`: True if device matches the default router/gateway IP.
- `bool isLocalPc`: True if device matches the host computer running SelfishNet.
- `int capDown`: Download cap limit in bytes/sec (derived from user KB/s input * 1024).
- `int capUp`: Upload cap limit in bytes/sec (derived from user KB/s input * 1024).
- `bool redirect`: If `true`, packets are forwarded. If `false` (Blocked), packets are dropped.
- `int nbPacketSentSinceLastReset`: Byte counter for outgoing traffic in current 1-second interval.
- `int nbPacketReceivedSinceLastReset`: Byte counter for incoming traffic in current 1-second interval.
- `int totalPacketSent` / `totalPacketReceived`: Lifetime packet counters.
- `DateTime timeSinceLastRarp`: Timestamp of last observed ARP activity. Used for timeout eviction.

#### 2. `PcList` Class
Thread-safe container managing the active device collection.
- `ArrayList pclist`: Internal array of `PC` objects.
- `delegateOnNewPC delOnNewPC`: Event callback triggered when a new device is discovered.
- `delegateOnNewPC delOnPCRemove`: Event callback triggered when a device is purged.
- Key Methods:
  - `addPcToList(PC pc)`: Deduplicates by IP/MAC and triggers `delOnNewPC`.
  - `removePcFromList(PC pc)`: Removes stale host and triggers `delOnPCRemove`.
  - `getRouter()` / `getLocalPC()`: Finds gateway and local machine records.
  - `getPCFromIP(byte[] ip)` / `getPCFromMac(byte[] mac)`: Fast lookups.
  - `ResetAllPacketsCount()`: Resets `nbPacket*SinceLastReset` counters every second.

#### 3. `CArp` Class (Core Network Engine)
Orchestrates raw packet capture, ARP discovery, ARP poisoning, and packet routing.
- **Fields:**
  - `CPcapNet pcaparp`: Dedicated pcap capture handle with filter `"arp"`.
  - `CPcapNet pcapredirect`: Dedicated pcap capture handle with filter `"ip"`.
  - `Thread discovererThread`, `arpListenerThread`, `redirectorThread`: Background workers.
  - `byte[] localIP`, `byte[] localMAC`, `byte[] routerIP`, `byte[] routerMAC`, `byte[] broadcastMac`.
  - `bool isListeningArp`, `bool isDiscovering`, `bool isRedirecting`.

- **Key Methods:**
  - `buildArpPacket(...)`: Constructs a raw 42-byte Ethernet frame containing an ARP packet:
    - Bytes 0–5: Destination MAC
    - Bytes 6–11: Source MAC
    - Bytes 12–13: EtherType `0x0806` (ARP)
    - Bytes 14–15: Hardware Type `0x0001` (Ethernet)
    - Bytes 16–17: Protocol Type `0x0800` (IPv4)
    - Byte 18: Hardware Size `6`
    - Byte 19: Protocol Size `4`
    - Bytes 20–21: Opcode (`1` = ARP Request, `2` = ARP Reply)
    - Bytes 22–27: Sender MAC
    - Bytes 28–31: Sender IP
    - Bytes 32–37: Target MAC
    - Bytes 38–41: Target IP
  - `discoverer()`:
    - Calculates the IPv4 network range using `localIP` and `netmask`.
    - Iterates sequentially through all host IPs in the subnet.
    - Sends an ARP request (`opcode = 1`) to each IP with a 5 ms sleep interval.
  - `arpListener()`:
    - Captures ARP reply frames (`opcode = 2`).
    - Skips frames originating from `localMAC`.
    - Extracts sender IP and MAC; flags gateway if sender IP == `routerIP`.
    - Appends/updates device in `pcList`.
  - `Spoof(IPAddress victimIP, IPAddress routerIP)`:
    - Sends two poisoned ARP replies (`opcode = 2`):
      1. To Victim: `[SrcMAC: localMAC, SrcIP: routerIP, DstMAC: victimMAC, DstIP: victimIP]`
         *(Tells victim: "I am the router")*
      2. To Router: `[SrcMAC: localMAC, SrcIP: victimIP, DstMAC: routerMAC, DstIP: routerIP]`
         *(Tells router: "I am the victim")*
  - `UnSpoof(IPAddress victimIP, IPAddress routerIP)`:
    - Sends genuine ARP frames restoring the original MAC bindings:
      1. To Victim: `[SrcMAC: routerMAC, SrcIP: routerIP, DstMAC: victimMAC, DstIP: victimIP]`
      2. To Router: `[SrcMAC: victimMAC, SrcIP: victimIP, DstMAC: routerMAC, DstIP: routerIP]`
  - `completeUnspoof()`:
    - Iterates over all active devices and invokes `UnSpoof` to restore LAN stability.
  - `redirector()` (Packet Forwarder & Bandwidth Limiter):
    - Continuously pulls IPv4 packets via `pcapnet_next_ex`.
    - **Direction A: Router -> Victim (Download)**
      - Incoming packet destination MAC == `localMAC` (poisoned router sent it here).
      - Matches destination IP against `pcList`.
      - Checks `capDown`: If `nbPacketReceivedSinceLastReset > capDown`, **drops packet**.
      - If `pc.redirect == false` (Blocked), **drops packet**.
      - Otherwise, modifies frame: rewrites destination MAC to `pc.mac`, source MAC to `localMAC`.
      - Transmits packet out with `pcapnet_sendpacket`.
      - Updates `nbPacketReceivedSinceLastReset += packetLength`.
    - **Direction B: Victim -> Router (Upload)**
      - Incoming packet source MAC == victim MAC, destination MAC == `localMAC`.
      - Matches source MAC against `pcList`.
      - Checks `capUp`: If `nbPacketSentSinceLastReset > capUp`, **drops packet**.
      - If `pc.redirect == false` (Blocked), **drops packet**.
      - Otherwise, rewrites destination MAC to `routerMAC`, source MAC to `localMAC`.
      - Transmits packet out with `pcapnet_sendpacket`.
      - Updates `nbPacketSentSinceLastReset += packetLength`.

#### 4. `ArpForm` Class (Main View & Controller)
- Contains grid view (`treeGridView1`) with columns:
  - `ColPCName`: Hostname / NetBIOS name.
  - `ColPCIP`: IPv4 Address.
  - `ColPCMac`: Physical MAC Address.
  - `ColDownload`: Real-time Download speed (KB/s).
  - `ColUpload`: Real-time Upload speed (KB/s).
  - `ColDownCap`: Download limit spinner (KB/s).
  - `ColUploadCap`: Upload limit spinner (KB/s).
  - `ColBlock`: Checkbox (drops all redirected traffic).
  - `ColSpoof`: Checkbox (activates ARP poisoning for this host).
- **Timers:**
  - `timerSpoof` (Interval: 5000 ms): Re-broadcasts poisoned ARP packets to keep ARP caches poisoned.
  - `timer1` (Interval: 1000 ms): Calculates speed:
    $$\text{Speed (KB/s)} = \frac{\text{BytesSinceLastReset} \times 0.0009765625}{\text{ElapsedSeconds}}$$
    Updates UI cells and resets counters.
  - `timer2` (Interval: 5000 ms): Heartbeat / eviction:
    - If device silent for $> 20\text{ s}$, sends unicast ARP probe.
    - If silent for $> 350\text{ s}$ (~5.8 min), removes device from list and grid.

---

## 4. Architectural Weaknesses in the Original Binary

1. **Kernel Driver Architecture (WinPcap 4.1.3 / npf.sys):**
   - SelfishNet tries to install and start `npf.sys` directly via Windows Service Control Manager.
   - On Windows 10 and Windows 11 (x64), unsigned 32-bit drivers cannot be loaded, causing driver errors or crashes.
   - Modern replacement: **Npcap** with native 64-bit NDIS 6 Light-Weight Filter (LWF) driver and backward-compatible WinPcap DLLs.
2. **Bandwidth Shaping Precision (Hard-Burst Window):**
   - The original throttling drops all packets the moment the accumulated count hits `capDown` within the 1-second window.
   - This causes abrupt TCP packet bursts followed by complete silence, resulting in high jitter and broken streaming/gaming.
   - Modern solution: Token Bucket Rate Limiting / Leaky Bucket packet pacing.
3. **Threading & Concurrency Issues:**
   - Packet processing loops run without batching or high-resolution sleep timers, spiking CPU usage on active networks.
   - Lack of lock synchronization between `pclist` mutations (timer2) and the `redirector` packet loop.
4. **UI Inflexibility & Fixed Typography:**
   - Fixed English strings hardcoded inside IL instructions.
   - Windows Forms controls not scaled for 96–192 DPI (modern 1080p, 2K, 4K monitors).
   - Inability to switch language at runtime.
