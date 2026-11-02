# Prototype Postmortem — Why the free-model prototype fell short

> Internal engineering note. The frozen prototype lived in `prototype/SelfishNetV2.Test/`
> and was removed on 2026-10-29; this document remains as its historical record. It
> explains why the prototype could not hold up on a real network, and how each failure
> was engineered out of the revamp.

---

## 1. Context

The prototype was built to validate the concept quickly: ARP discovery, twin-spoofing,
packet forwarding, per-device caps and blocking, plus a bilingual UI. It **proved the
concept** — devices appeared, traffic was redirected, limits applied — which is why it
looked successful at first.

Real-network use then exposed intermittent failures: a device occasionally escaped the
shaping, engine changes were flaky, stops could stall, and limits sometimes failed to hold
under load. None of these were one catastrophic bug; they were accumulated weaknesses on the
**failure paths** — and a man-in-the-middle shaper lives on exactly those paths.

---

## 2. What the prototype got right (and was carried forward)

- The 42-byte Ethernet II + ARP frame layout and the twin-poison + self-refresh idea.
- Discovery by paced broadcast ARP requests; learning from ARP replies.
- Forwarding by source-MAC classification with MAC rewriting.
- Per-device caps/block, byte-based storage, unit conversion rules, persistence by MAC,
  and the hot-swappable localization pattern.

These parts were reused conceptually (and re-verified) in the revamp.

---

## 3. Failure modes, root causes, and the fixes

| # | Symptom on a real network | Root cause in the prototype | Fix in the revamp |
|---|---|---|---|
| 1 | A device (e.g. a phone) occasionally escaped the shaping until stop/restart | Re-poison every 5 s with no reaction to ARP races: a device re-asking for the gateway could keep the true router MAC until the next round | Modern engine re-poisons every 2 s, sends a double initial burst, and answers ARP races instantly (victim asks for gateway / router answers victim / victim announces itself) — `EngineSessionBase.OnArpFrame`, `ReactPoison`; covered by `EngineRobustnessTests.Modern_ReactivePoison_AnswersVictimArpRequestInstantly` |
| 2 | Poison could be orphaned after unticking a device, eviction, engine switch or adapter change | Healing iterated only the devices whose "Spoof" box was ticked at that moment; the poisoned set was not tracked independently | `_poisoned` registry (ip→mac) independent of the UI flags; heal on untarget, eviction, adapter switch, engine switch, close; failed claims are retained and retried — `HealOne`, `UnspoofAll`, `CloseCore`; tests `UntargetMidSession_HealsDeviceBeforeDroppingIt`, `Orchestrator_AdapterSwitchWhileShaping_HealsOldLanFirst`, `Close_RetriesOutstandingHealClaims_WhenGatewayMacWasLost` |
| 3 | Stop/engine switch could hang, take ~5 s, or crash intermittently | `pcap_close()` could run while a worker was still blocked inside `pcap_next_ex()`; the spoof thread was not cancelled before joining | `PcapCaptureSession` enforces **breakloop → join → close**; shaping cancellation happens before joining the spoof thread — `PcapCaptureSession.Stop/Dispose`; 300-cycle `ShutdownHammerTests` |
| 4 | Latent corrupted frame parsing; crashes under bursts | `pcap_pkthdr` was marshalled with 64-bit `long` fields although the native Windows struct is 16 bytes with 32-bit `long`s → `caplen`/`len` read out of bounds | Correct 16-byte LLP64 struct, an ABI guard test and a first-frame sanity check — `PcapPkthdr`, `PcapAbiSelfTest`, `CompatibilityTests`, `PcapAbiTests` |
| 5 | High CPU and capture drops under load; the same class of crash the 2007 app had on large transfers | Per-packet heap allocations, `ToString()`-based MAC/IP identity, O(n) MAC scans; `DateTime.UtcNow` as the shaper clock; bucket refilled on cap change | Numeric `uint`/`ulong` keys, span parsing, single reused 65 KB transmit buffer, `Stopwatch`-based bucket with clamped refill and single-packet deficit — `PacketClassifier`, `NetKeys`, `EngineSessionBase.OnIpFrame`, `TokenBucket`; 10 000-frame burst test |
| 6 | Uploads could be emitted onto the broadcast address when the gateway MAC was unknown | Fallback to `FF:FF:FF:FF:FF:FF` | Hard gate: shaping refuses to start without the real gateway MAC, and per-packet forwarding drops instead of broadcasting — `StartShapingAsync`, `OnIpFrame`; `Modern_DoesNotForward_WhenRouterMacUnknown` |
| 7 | A failed heal could silently leave a cache poisoned | No gateway re-resolution, no broadcast corrections, no retry after a failed heal | Gateway MAC re-resolved once per heal pass; 35 unicast restore pairs + 3+3 broadcast gratuitous announcements per device; failed claims retried on close — `HealOne`, `ResolveRouterMacForHeal`; broadcast assertions in `EngineRobustnessTests` |
| 8 | No way out if the single engine misbehaved | One code path only | Two interchangeable engines with a runtime switch that heals before swapping — `EngineOrchestrator.SwitchEngineAsync` |
| 9 | UI froze up to ~750 ms when resolving the gateway | Blocking `Thread.Sleep` loop on the UI thread | Async start pipeline with progress/busy overlay — `StartShapingAsync`, `MainViewModel.ToggleShapingAsync` |
| 10 | Speed/limit readouts drifted; caps could burst after edits | 1 s UI timer for sampling; wall-clock bucket delta; full-bucket refill on rate change | Timestamped sampling; high-resolution clock; clamp-not-refill — `StatsLoop`, `TokenBucket` |

---

## 4. Lessons now encoded as project rules

1. **Frame parity is a test, not a convention.** Every ARP builder is diffed against golden
   byte vectors (`ArpFrameBuilderParityTests`).
2. **The ABIs at the driver boundary are asserted.** Struct size/offsets are unit-tested and
   sanity-checked on the first frame of every session.
3. **The shutdown ritual is non-negotiable.** No handle is ever closed while a thread may be
   using it; cancellation precedes joining.
4. **No string identity or steady-state allocation in the packet hot path.**
5. **Anything that can leave the network modified needs a registry and a heal path** —
   stop, close, logoff/shutdown, switches, eviction, untick.
6. **Failure paths are tested without hardware** using `LoopbackDriver` (both engines,
   blocking, caps, healing, engine swap, 10 000-frame burst, 300 shutdown cycles).
7. **Promises are scoped.** Healing is best-effort by nature: force-kill or devices that
   reject corrections cannot be handled, and the guides say so.

---

## 5. Where the evidence lives

| Path | Contents |
|---|---|
| `prototype/SelfishNetV2.Test/` | Frozen prototype source (removed 2026-10-29; historical) |
| `docs/ORIGINAL_FINDINGS.md` | Verified mechanics of the original 2007 binary |
| `docs/REVAMP_BLUEPRINT.md` §1 | Full defect catalogue (C1–C3, H1–H3, M1–M8) and prevention strategy |
| `src/SelfishNetV2.Tests/` | One regression test per failure mode above |
| `docs/WINDOWS_COMPATIBILITY.md` | OS support contract and elevation model |
