# Phase D1: Reality Hardening — PLAN.md

## Goal
Harden the installer, OS lifecycle integration, UI automation drivers, and browser orchestration to ensure Engram survives real-world Windows environments without crashes, permission blocks, or state corruption.

## Success Criteria (from ROADMAP.md)
1. Engram installs and runs successfully under non-admin standard accounts without requiring UAC escalation (validated by automated PrivilegeSimulationTests).
2. COM automation and screenshot click coordinates scale accurately across diverse DPI monitors and configurations using per-window live scaling and post-click semantic verification (validated by DpiScaleTests).
3. The system detects OS sleep/hibernation, pauses inference and background tasks, unloads the model, and resumes cleanly on wake after an adaptive stabilization window (validated by PowerSuspensionTests).
4. Sudden process termination does not corrupt local stores; multi-entity updates self-reconcile or roll back via transactional write intent journals (validated by TransactionalWalTests).
5. Playwright edge-cases fail over gracefully to WebView2, propagate reduced environmental confidence scores to down-stream reasoning layers to prevent silent cascading corruptions, log details in pathology memory, and display capability surface transparency details to the user (validated by DriverFallbackTests).

---

## Plan D1-01: Launcher, Privilege & DPI Hardening (D1A, D1B)

### Components
1. **PreFlightDiagnostics** — Executed by Tauri Rust launcher on startup to verify sidecar binaries, write permissions in `%LOCALAPPDATA%/Engram/`, VC++ runtime status, WebView2 runtime presence, and critical DLL integrity. Reports using Info, Warning, and Critical severity levels.
2. **DpiScaleAwareCoordinates** — Modifies the COM UI automation bridge to translate logical pixel clicks to target-monitor physical pixel offsets based on active monitor HWND properties resolved dynamically.
3. **CoordinateVerificationSampling** — Performs post-click verification checking accessibility focus changes, foreground HWND titles, or new dialog handles to verify click coordinate validity.
4. **PrivilegeSimulationTests** — Automated test suite verifying that all file operations, db operations, and lock mechanisms run under standard non-admin rights.
5. **DpiScaleTests** — Automated simulator testing monitor coordinates across diverse scale factors (100%, 125%, 150%, 200%) and monitor migrations.

### Files
- `src/Engram.App/src-tauri/src/preflight.rs` [NEW]
- `src/Engram.Store/Automation/DpiScaleAwareCoordinates.cs` [NEW]
- `src/Engram.Store/Automation/WindowsUiAutomationProvider.cs` [MODIFY]
- `tests/Engram.Store.Tests/Hardening/PrivilegeSimulationTests.cs` [NEW]
- `tests/Engram.Store.Tests/Automation/DpiScaleTests.cs` [NEW]

### Test Plan
- Run pre-flight diagnostic module in sandbox and verify it reports Critical errors if port 5000 is occupied or profile directory is unwritable.
- Execute `PrivilegeSimulationTests` to assert non-admin write compatibility.
- Execute `DpiScaleTests` to verify coordinate translation calculations and monitor migration detection.

---

## Plan D1-02: OS State Transitions & Write Resilience (D1C)

### Components
- **PowerBroadcastListener** — Listens to Windows Power Events (suspend/resume), flushes current writes, unloads the model on suspend, and enforces a 2-10 second adaptive stabilization recovery window on resume.
- **TransactionalSemanticEnvelope** — Manages multi-file semantic transaction bounds. Includes TransactionId, Status, AffectedEntities, and RollbackMarkers.
- **AtomicEventStore** — Writes files using Write Intent Journaling in the WAL, enabling complete multi-entity recovery/rollback in `CausalReconciler` on next boot if crashed mid-operation.
- **PowerSuspensionTests** — Programmatically dispatch sleep/wake events to verify task suspension, model unloading, and stabilization delay.
- **TransactionalWalTests** — Automated rollback test mid-transaction to verify zero semantic corruption.

### Files
- `src/Engram.Store/Events/PowerBroadcastListener.cs` [NEW]
- `src/Engram.Store/Ingestion/TransactionalSemanticEnvelope.cs` [NEW]
- `src/Engram.Store/WriteAheadLog.cs` [MODIFY]
- `src/Engram.Store/Ingestion/CausalReconciler.cs` [MODIFY]
- `tests/Engram.Store.Tests/Events/PowerSuspensionTests.cs` [NEW]
- `tests/Engram.Store.Tests/Ingestion/TransactionalWalTests.cs` [NEW]

### Test Plan
- Execute `PowerSuspensionTests` to simulate sleep/wake transitions. Verify metabolic loops pause and model unloads, then resume only after the stabilization delay.
- Execute `TransactionalWalTests` to simulate process termination mid-multi-entity save and assert that the reconciler recovers cleanly with no partial states.

---

## Plan D1-03: Environment Fallbacks, AV Hygiene & Legibility (D1D)

### Components
- **WebView2DriverFallback** — Implements `IBrowserDriver` using standard WebView2 controllers if Playwright's local Edge profile is blocked.
- **DegradationTracker** — Maintained by `InferenceLifecycleManager` to track active degradations and capability surface details (isolation changes, missing extensions, cookie status).
- **EnvironmentalPathologyMemory** — Persists detailed environment failure logs in `.engram/diagnostics/pathology_memory.json` to prevent repeated failures.
- **BehavioralTransparencyProfile** — Generates a signed json profile declaring folders, ports, and domains to facilitate whitelist configuration for enterprise AV heuristic tools.
- **DriverFallbackTests** — Automated suite verifying Playwright-to-WebView2 fallback, pathology logging, environmental confidence propagation, and UI indicator health reporting.

### Files
- `src/Engram.Store/Automation/WebView2DriverFallback.cs` [NEW]
- `src/Engram.Store/Inference/DegradationTracker.cs` [NEW]
- `src/Engram.Store/Security/BehavioralTransparencyProfile.cs` [NEW]
- `src/Engram.Store/Inference/InferenceLifecycleManager.cs` [MODIFY]
- `src/Engram.Store/Automation/BrowserAgentRuntime.cs` [MODIFY]
- `tests/Engram.Store.Tests/Agent/DriverFallbackTests.cs` [NEW]

### Test Plan
- Execute `DriverFallbackTests`. Verify browser fallback, degradation tracking, and the propagation of `EnvironmentalConfidenceScore` to reasoning engines.
- Verify the behavioral transparency manifest is generated and returned by a `/api/health/transparency` API endpoint.

---

## Execution Order
1. **Plan D1-01:** Implement Rust pre-flight checks, C# DPI-scale COM coordinates, post-click focus verification, and automated tests (`PrivilegeSimulationTests`, `DpiScaleTests`).
2. **Plan D1-02:** Build Win32 power listener, implement wake stabilization delay, construct transactional envelopes in the WAL, and verify rollback behavior via `PowerSuspensionTests` and `TransactionalWalTests`.
3. **Plan D1-03:** Add WebView2 browser fallback, construct `DegradationTracker`, log pathology memory, and verify capability transparency propagation via `DriverFallbackTests`.
4. **Integration:** Update `validate-install.ps1` with new checks and verify all tests pass.
