# Phase D1: Reality Hardening — PLAN.md

## Goal
Harden the installer, OS lifecycle integration, UI automation drivers, and browser orchestration to ensure Engram survives real-world Windows environments without crashes, permission blocks, or state corruption.

## Success Criteria (from ROADMAP.md)
1. Engram installs and runs successfully under non-admin standard accounts without requiring UAC escalation.
2. COM automation and screenshot click coordinates scale accurately across diverse DPI monitors and configurations.
3. The system detects OS sleep/hibernation, pauses inference and background tasks, and resumes them cleanly on wake.
4. Sudden process termination does not corrupt local stores; uncommitted event sequences self-reconcile on next boot.
5. Playwright edge-cases fail over gracefully to standard system WebView2 interfaces when needed.

---

## Plan D1-01: Installer & Privileges Hardening (D1A, D1B)

### Components
1. **PreFlightDiagnostics** — Executed by Tauri Rust launcher on startup to verify sidecar binaries, write permissions in `%LOCALAPPDATA%/Engram/`, and bind ability of port 5000.
2. **DpiScaleAwareCoordinates** — Modifies the COM UI automation bridge to translate logical pixel clicks to target-monitor physical pixel offsets based on monitor handle coordinates.

### Files
- `src/Engram.App/src-tauri/src/preflight.rs` [NEW]
- `src/Engram.Store/Automation/DpiScaleAwareCoordinates.cs` [NEW]
- `src/Engram.Store/Automation/Win32AutomationBridge.cs` [MODIFY]
- `tests/Engram.Store.Tests/Automation/DpiScaleTests.cs` [NEW]

### Test Plan
- Run pre-flight diagnostic module in sandbox and verify it reports warnings if port 5000 is occupied.
- Assert click coordinate translation math behaves correctly under 100%, 125%, 150%, and 200% screen DPI scalings.

---

## Plan D1-02: OS State Transitions & Write Resilience (D1C)

### Components
- **PowerBroadcastListener** — Listens to Windows Power Events (suspend/resume) and coordinates model unloading or task pausing.
- **AtomicEventStore** — Replaces raw file appends with write-to-temp-and-rename semantics to ensure no half-written JSON files exist after a sudden power loss.

### Files
- `src/Engram.Store/Events/PowerBroadcastListener.cs` [NEW]
- `src/Engram.Store/Ingestion/AtomicEventStore.cs` [NEW]
- `tests/Engram.Store.Tests/Ingestion/AtomicEventStoreTests.cs` [NEW]

### Test Plan
- Simulate a `PBT_APMRESUMESUSPEND` event and verify the background metabolizer triggers self-healing and task resumption.
- Simulate mid-write power cut (kill process mid-save) and verify the `AtomicEventStore` detects the write fracture and heals or rolls back cleanly.

---

## Plan D1-03: Environment Fallbacks & Anti-Virus Hygiene (D1D)

### Components
- **WebView2DriverFallback** — Implements `IBrowserDriver` using standard WebView2 controllers if Playwright's local Edge profile is blocked or corrupted.
- **ManifestSignerHelper** — Generates diagnostic manifest metadata files showing read/write profiles to allow easy whitelisting by enterprise anti-virus tools.

### Files
- `src/Engram.Store/Agent/WebView2DriverFallback.cs` [NEW]
- `src/Engram.Store/Security/ManifestSignerHelper.cs` [NEW]
- `tests/Engram.Store.Tests/Agent/DriverFallbackTests.cs` [NEW]

### Test Plan
- Force Playwright driver to throw a startup exception and assert the browser runner falls back to WebView2 without failing the task.
- Generate and verify checksum manifests.

---

## Execution Order
1. **Plan D1-01:** Implement Rust pre-flight checks and C# DPI-scale COM coordinates.
2. **Plan D1-02:** Build Win32 power listener and integrate atomic file stores. Verify recovery.
3. **Plan D1-03:** Add WebView2 browser fallback strategy and test failure recovery.
4. **Integration:** Update `validate-install.ps1` with new checks and verify all tests pass.
