# Original-binary findings (from IL disassembly, Oct 2026)

Verified against `SelfishNetv0.2-beta_vista.exe` (`SelfishNet10.CArp`) and
`PcapNet.dll` (`PcapNet.CPcapNet`) using the `disasm` approach. The earlier
`docs/ARCHITECTURE_ANALYSIS.md` was wrong on two load-bearing points —
this file corrects them for the main project.

## 1. Capture opens NON-promiscuous

`pcapnet_openLive(name, size, options, timeout, err)` passes `options`
straight into native `pcap_open_live(..., promisc, ...)`.
All three call sites (`startArpListener`, `startRedirector`, `findMac`) use:

```
snaplen = 65535, promisc = 0, timeout = 1 ms
```

Non-promiscuous capture still sees all poisoned traffic (unicast to us) plus
broadcasts, with less noise — and it works on Wi-Fi/Npcap where promiscuous
mode is unreliable. Adopted as-is (timeout relaxed to 20 ms for CPU).

## 2. Forwarder matches on SOURCE MAC, not destination

Per packet (`V_4` = bytes 6..11 = Ethernet **source** MAC):

- `srcMAC == localMAC` → own transmission. Counted on the local-PC row,
  never re-forwarded.
- `srcMAC == routerMAC` → **Download**: device = lookup by DST IP
  (bytes 30..33); cap/redirect checks; rewrite dstMAC=victim, srcMAC=us.
- else → **Upload**: if dstIP == localIP, skip; device = lookup by SRC MAC;
  cap/redirect checks; rewrite dstMAC=router, srcMAC=us.

There is NO EtherType check (the `"ip"` BPF filter already guarantees IPv4).
`routerMAC` is re-read from the device table every loop iteration.
Cap semantics: `if (cap != 0 && cap <= bytesThisSecond) drop`.

## 3. Spoof sends FOUR frames per victim (not two)

`CArp::Spoof(ip1, ip2)` emits, via the ARP-filtered handle:

1. To victim: reply "routerIP is at localMAC" (poison).
2. To router: reply "victimIP is at localMAC" (poison).
3. To **us** (eth dst = localMAC, eth src = routerMAC): reply
   "routerIP is at routerMAC" — a self-refresh: our own listener sees it and
   re-learns the TRUE router MAC into the device table every round.
4. To us (eth dst = eth src = localMAC): reply "victimIP is at victimMAC" —
   inert (our listener skips eth-src == localMAC); likely a codegen leftover.

## 4. Router MAC learning is active, not just passive

- `findMacRouter()` → `findMac(ip)`: broadcasts one ARP *request* (opcode 1,
  eth dst = broadcast) for the gateway; the reply is learned by the listener.
- `arpListener` accepts opcode 2 only, skips eth-src == localMAC, learns the
  router MAC from the **Ethernet source** field when sender IP == router IP.

## 5. Implication for "upload ignores limits"

With behavior (2), unpoisoned victim→router traffic is duplicate-forwarded but
never truly throttled (the original delivery already succeeded) — in the
original too. Real upload shaping requires the victim poisoned AND the router
MAC correct (upload rewrite targets it). If upload is unaffected while
download works, check in order: victim ARP cache (poison sticking?),
gateway MAC known (zeros in the grid = unknown), Windows `IPEnableRouter`.
