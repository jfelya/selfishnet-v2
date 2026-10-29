# SelfishNet v2 Hardening Implementation Plan

> **Status 2026-10-28:** all tasks and the fresh-review fix pass are complete; 124/124 tests green; package audit clean. Integration/merge is still the user's decision.
>
> **For agentic workers:** Use superpowers:executing-plans (inline) for this plan.
> Steps use checkbox syntax. Each task ends with a green build, the full test suite,
> and one commit.

**Goal:** Fix the shutdown, capture-error, UI-state, persistence, discovery, and
display defects found in the 2026-10-28 audit without changing the dual-engine
architecture or adding a runtime dependency.

**Architecture:** Keep C#/.NET 8/WPF, the two engines, the shared `DeviceCatalog`,
and the `IPcapDriver` seam. Fix failure paths and state transitions in place.
The Legacy wire format is unchanged.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit. No new runtime packages.

**Spec:** `docs/REVAMP_HARDENING_AUDIT.md`

## Global Constraints

- Do not modify `reference/` or `prototype/`.
- Do not commit the two root executables during this plan.
- Do not add a runtime NuGet dependency.
- Legacy ARP frame bytes and parity tests stay unchanged.
- Every production change starts with a failing test; watch it fail before fixing.
- Run the full `dotnet test src/SelfishNetV2.sln -c Release` at the end of every task.
- Keep `AI_ENVIRONMENT_NOTES.md` and both guides current when behavior changes.

## Review Focus

- A close, stop, engine switch, or logoff during a stuck driver must not close a
  handle that a worker is still using.
- A capture error must never leave victims poisoned while the UI says shaping.
- A device update must not leave the grid showing a different model than the catalog.
- A reused DHCP address must not inherit another device's block or cap.
- A crash during a settings write must not erase remembered devices.
- A large subnet must not make discovery run for hours.
- A cap value typed by a user must not overflow or display as unlimited.
- Two app instances must not shape the same LAN.

---

## Task 1: Fail-safe capture and shutdown

**Files:**
- Modify: `src/SelfishNetV2.Drivers/PcapCaptureSession.cs`
- Modify: `src/SelfishNetV2.Engines/EngineSessionBase.cs`
- Create: `src/SelfishNetV2.Tests/CaptureSafetyTests.cs`
- Modify: `src/SelfishNetV2.Tests/ShutdownHammerTests.cs` only if a helper is needed

**Interfaces:**
- `PcapCaptureSession.Stop(int joinTimeoutMs = 5000)` returns `bool` (true when joined).
- `PcapCaptureSession` exposes no new public state required by callers.
- `EngineSessionBase` treats `ReceiveError` as fatal once per session.

- [x] **Step 1: Write failing session tests**
  Add a blocking fake capture whose `BreakLoop` does not unblock `Receive`.
  Assert that `Stop(50)` returns false and `Dispose()` does **not** call the fake's
  `Dispose()`.
  Add a faulting fake that returns `ReceiveResult.Error`; assert `Running` becomes
  false after the receive thread exits.

- [x] **Step 2: Run the tests and watch them fail**
  Run: `dotnet test src/SelfishNetV2.sln -c Release --filter FullyQualifiedName~CaptureSafetyTests`
  Expected: both fail against the current `PcapCaptureSession`.

- [x] **Step 3: Make the session fail safe**
  Add `_abandoned`; `Stop()` sets it on join timeout and returns false. `Dispose()`
  closes only when the thread joined or never started. `Start()` throws when
  abandoned. Set `_running = false` in a `finally` around the receive loop.

- [x] **Step 4: Write the engine fatal-error test**
  Add a loopback fault hook or a small fake driver that can end the IP capture with
  `ReceiveResult.Error`. Assert the engine leaves `Shaping`, sends restore frames,
  and does not throw.

- [x] **Step 5: Run the engine test and watch it fail**
  Expected: state stays `Shaping` and no heal frames are sent.

- [x] **Step 6: Wire fatal receive errors**
  Add a single-shot handler in `EngineSessionBase`. On error, raise the existing
  `EngineErrorKind.ReceiveError`, then run `StopAndHealAsync()` once on a background
  task. Do not call it from the capture thread directly.

- [x] **Step 7: Make engine joins gate disposal**
  Change `JoinThread()` to return `bool`. In `CloseCore()`, dispose `_arp` only when
  the spoof, stats, and janitor joins all succeeded; otherwise leave the capture
  alive (leak) rather than close it under a live thread.

- [x] **Step 8: Verify**
  Run the focused tests, then the full suite.
  Run: `dotnet test src/SelfishNetV2.sln -c Release`
  Expected: all tests pass, including the 300-cycle hammer.

- [x] **Step 9: Commit**
  `git add` the two source files and the test file; commit
  `fix: make capture shutdown and receive errors fail safe`.

---

## Task 2: Catalog/UI identity and adapter matching

**Files:**
- Modify: `src/SelfishNetV2.Core/DeviceCatalog.cs`
- Modify: `src/SelfishNetV2.Core/AdapterInfo.cs`
- Modify: `src/SelfishNetV2.App/ViewModels/MainViewModel.cs`
- Modify: `src/SelfishNetV2.App/ViewModels/DeviceViewModel.cs`
- Modify: `src/SelfishNetV2.Tests/DeviceCatalogTests.cs`
- Create: `src/SelfishNetV2.Tests/ViewModelStateTests.cs`
- Modify: `src/SelfishNetV2.Tests/SelfishNetV2.Tests.csproj` if the App reference is needed

**Interfaces:**
- `DeviceCatalog.Clear()` raises `DeviceRemoved` for each device it removes.
- `DeviceViewModel` constructor and `RefreshRow()` synchronize caps and `Blocked`.
- `NetworkAdapterEnumerator.MatchPcapName()` returns `""` when no GUID matches.

- [x] **Step 1: Write failing catalog/UI tests**
  Assert `Clear()` raises one removal per device. Construct a `DeviceViewModel`
  for a device with a saved cap and assert the cap text is not `"0"`.

- [x] **Step 2: Run and watch them fail**
  Expected: no removal events; cap text is `"0"`.

- [x] **Step 3: Fix the catalog clear path**
  Snapshot the devices, clear both indexes, then raise `DeviceRemoved` for each.
  Remove the unused `Changed` event if it is still unused.

- [x] **Step 4: Fix the view-model synchronization**
  Call `RefreshCaps()` in the constructor. In `RefreshRow()`, call `RefreshCaps()`
  and raise `Blocked`.

- [x] **Step 5: Write the adapter-match test**
  Make the helper testable and assert a non-matching GUID returns `""`, not the
  first pcap device.

- [x] **Step 6: Verify**
  Run the focused tests and the full suite. Open the app and switch adapters if
  hardware is available; the old rows must disappear.

- [x] **Step 7: Commit**
  Commit `fix: keep device rows and saved caps in sync`.

---

## Task 3: Persistence, identity, and bulk edits

**Files:**
- Create: `src/SelfishNetV2.Services/AtomicFile.cs`
- Modify: `src/SelfishNetV2.Services/KnownDevicesStore.cs`
- Modify: `src/SelfishNetV2.Services/SettingsStore.cs`
- Modify: `src/SelfishNetV2.Services/EngineOrchestrator.cs`
- Modify: `src/SelfishNetV2.App/ViewModels/MainViewModel.cs`
- Modify: `src/SelfishNetV2.Core/Device.cs`
- Modify: `src/SelfishNetV2.Core/NetKeys.cs`
- Modify: `src/SelfishNetV2.Tests/KnownDevicesStoreTests.cs`
- Create: `src/SelfishNetV2.Tests/PersistenceTests.cs`

**Interfaces:**
- Stores accept an optional file path for tests; the default path is unchanged.
- `KnownDevicesStore.UpdateMany(IEnumerable<Device>)` saves once.
- `EngineOrchestrator.SetCapsBulk(IEnumerable<Device>, int, int)` updates and saves once.
- `Device.UserEdited` is set by explicit name/cap/block edits.

- [x] **Step 1: Write failing persistence tests**
  Load a truncated JSON file through a temp path and assert the store starts empty
  without throwing. Write a saved file twice and assert the `.bak` exists and the
  live file is valid.

- [x] **Step 2: Write failing identity tests**
  Keep the legitimate randomized-MAC-at-same-IP case. Add a normal-MAC reuse case
  that must not inherit. Add a late-hostname case where the user has edited the
  device and the saved settings must not overwrite it.

- [x] **Step 3: Run and watch them fail**
  Expected: corrupt load loses data silently; normal-MAC reuse inherits; late DNS
  overwrites.

- [x] **Step 4: Implement atomic writes and path seams**
  Use one small `AtomicFile.WriteAllText` helper (temp + replace + backup). Inject
  the store path only for tests.

- [x] **Step 5: Implement the safer IP policy and user-edit guard**
  Inherit by `LastIp` only when the new MAC is locally administered. Keep
  `TryApplyByName` as the recovery path. Set `UserEdited` in the orchestrator's
  explicit edit methods and skip late name matches for edited devices.

- [x] **Step 6: Implement bulk update**
  Add `UpdateMany` and `SetCapsBulk`; make `ResetLimits()` use them. Re-load the
  temp store in a test to prove both device caps persisted.

- [x] **Step 7: Verify and commit**
  Run focused tests and the full suite, then commit
  `fix: make device memory atomic and identity-safe`.

---

## Task 4: Discovery bounds and cap input

**Files:**
- Modify: `src/SelfishNetV2.Core/AdapterInfo.cs` if a host-count helper is needed
- Modify: `src/SelfishNetV2.Engines/EngineSessionBase.cs`
- Modify: `src/SelfishNetV2.Services/SpeedFormatter.cs`
- Modify: `src/SelfishNetV2.App/ViewModels/MainViewModel.cs`
- Modify: `src/SelfishNetV2.App/ViewModels/DeviceViewModel.cs`
- Modify: `src/SelfishNetV2.Tests/EngineRobustnessTests.cs`
- Modify: `src/SelfishNetV2.Tests/SpeedFormatterTests.cs`

**Interfaces:**
- Discovery exposes a completion event; `IEngineSession` and the orchestrator forward it.
- `StartDiscovery()` remains callable by existing code.
- `SpeedFormatter.FromCapDisplay` clamps to `int.MaxValue`; `DeviceViewModel`
  displays sub-1 KByte caps as `<1` and preserves them on an unchanged commit.

- [x] **Step 1: Write failing discovery tests**
  Use a test engine with zero pacing and a small host cap. Assert the sweep stops
  at the cap and that `DiscoveryCompleted` fires.

- [x] **Step 2: Write failing cap tests**
  Assert `FromCapDisplay(decimal.MaxValue, UnitMode.Bytes)` does not produce a
  negative/undefined cap. Assert a 500 B/s cap displays as `<1` in Bytes mode and
  an unchanged commit keeps it.

- [x] **Step 3: Run and watch them fail**
  Expected: unbounded sweep, no completion event, overflow, `"0"` display.

- [x] **Step 4: Implement the sweep budget and completion event**
  Cap the number of hosts per sweep (4096) and expose completion. Reset the
  “Scanning…” status when discovery finishes.

- [x] **Step 5: Implement cap clamping and display**
  Clamp the conversion and preserve the existing cap when the editor still shows
  `<1`.

- [x] **Step 6: Verify and commit**
  Run the focused tests and the full suite; commit
  `fix: bound discovery and harden cap entry`.

---

## Task 5: Display and update performance

**Files:**
- Modify: `src/SelfishNetV2.Core/DeviceCatalog.cs`
- Modify: `src/SelfishNetV2.App/Controls/BandwidthChartControl.cs`
- Modify: `src/SelfishNetV2.App/ViewModels/MainViewModel.cs` if event subscriptions change
- Modify: `src/SelfishNetV2.Tests/DeviceCatalogTests.cs`
- Create: `src/SelfishNetV2.Tests/ChartScaleTests.cs` if a pure helper is extracted

**Interfaces:**
- `DeviceUpdated` fires only when identity or flags change.
- The chart uses one scale for both series and the axis.

- [x] **Step 1: Write a failing catalog event test**
  Add the same ARP device twice with the same MAC and assert only the first add
  raises `DeviceAdded` and no `DeviceUpdated` is raised for the unchanged repeat.

- [x] **Step 2: Run and watch it fail**
  Expected: the current code raises `DeviceUpdated` every time.

- [x] **Step 3: Gate the update event**
  Compare MAC and flags before raising. Keep `Touch()` unconditional. Remove the
  unused `Changed` event if Task 2 has not already done so.

- [x] **Step 4: Fix the chart scale**
  Pass the combined `scaleMax` into `DrawSeries()` and remove the per-series
  `seriesScaleMax()` calculation. Add a pure scale test if practical, otherwise
  record a manual visual check with a 10:1 series.

- [x] **Step 5: Verify and commit**
  Run the focused tests and the full suite; commit
  `perf: reduce device update churn and fix chart scale`.

---

## Task 6: Process guard, dependencies, dead code, docs

**Files:**
- Create: `src/SelfishNetV2.Services/SingleInstanceGuard.cs`
- Modify: `src/SelfishNetV2.App/App.xaml.cs`
- Modify: `src/SelfishNetV2.App/Infrastructure/ElevationHelper.cs`
- Modify: `src/SelfishNetV2.Tests/SelfishNetV2.Tests.csproj`
- Modify: `src/SelfishNetV2.Tests/` package references and dead-code call sites
- Modify: `README.md`, `AI_ENVIRONMENT_NOTES.md`, `src/README.md`,
  `docs/REVAMP_BLUEPRINT.md`, `docs/UNIT_NOTATIONS.md`,
  `src/SelfishNetV2.Tests/CompatibilityTests.cs`, `docs/LOCALIZATION_SPEC.md`
- Modify: `GUIDE_EN.txt` / `GUIDE_ES.txt` only if user-visible behavior changed
- Modify: dead-code files listed in the audit

**Interfaces:**
- `SingleInstanceGuard.TryAcquire(name)` returns false when another instance holds it.
- `SingleInstanceGuard.Release()` frees the mutex before an elevated relaunch.
- The test project has no high-severity vulnerable packages.

- [x] **Step 1: Write a failing single-instance test**
  Acquire a unique name twice in one process; the second must fail; release and
  acquire again.

- [x] **Step 2: Implement the guard and wire startup/elevation**
  Acquire in `App.OnStartup`; reject a second instance with a short message.
  Release before `ElevationHelper.RelaunchElevated` and reacquire if UAC is
  cancelled.

- [x] **Step 3: Upgrade test packages**
  Update `Microsoft.NET.Test.Sdk` and xUnit; remove `coverlet.collector` if it is
  not used by a documented command. Verify with
  `dotnet list package --vulnerable --include-transitive`.

- [x] **Step 4: Remove dead code**
  Delete the unused members and merge the duplicate MAC writer as listed in
  `docs/REVAMP_HARDENING_AUDIT.md`. Run grep for each removed name.

- [x] **Step 5: Update docs**
  Correct the test count to 96 (or the new total), publish output, settings path,
  compatibility file name, current localization keys, and the blueprint status.
  Add a dated hardening entry to `AI_ENVIRONMENT_NOTES.md`.
  Update both guides only for user-visible changes (single instance, errors,
  discovery status).

- [x] **Step 6: Verify and commit**
  Run the full suite, `dotnet list package --vulnerable --include-transitive`,
  and a grep for removed symbols. Commit
  `chore: harden startup, trim dead code, and refresh docs`.

---

## Task 7: Final verification and handoff

**Files:** none unless verification finds a defect.

- [x] **Step 1: Full build and test**
  Run `dotnet build src/SelfishNetV2.sln -c Release` and
  `dotnet test src/SelfishNetV2.sln -c Release`.
  Expected: 0 warnings, 0 failures.

- [x] **Step 2: Package audit**
  Run `dotnet list package --vulnerable --include-transitive`.
  Expected: no high/critical findings.

- [x] **Step 3: Review**
  Run `git diff --check`, inspect the full diff, and run the
  `requesting-code-review` workflow. Record any rulings/deferred minors in the
  ledger.

- [x] **Step 4: Optional hardware smoke test**
  If Npcap and a test LAN are available: launch the built app, discover, shape one
  device, stop, and confirm the ARP cache is restored. Do not commit new exes
  until this passes and the user confirms.

- [x] **Step 5: Finish**
  Use `finishing-a-development-branch`. Leave the root executables untouched.
