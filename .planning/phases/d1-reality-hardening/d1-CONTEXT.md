# Phase D1: Reality Hardening - Context

**Gathered:** 2026-05-23
**Status:** Active Phase

<domain>
## Phase Boundary

Establish absolute runtime resilience for Engram across messy, diverse, and unpredictable Windows environments. Hardening aims to eliminate failures caused by installer privileges, OS dynamic state transitions, hardware configuration variances, and hostile host environments (antivirus/permissions/divergent browsers).

Objectives:
- **Installer Resilience:** Ensure the desktop shell and .NET sidecar install, launch, and update robustly under both Admin and non-Admin standard user privileges, resolving any dynamic environment path or folder permission issues. Includes dependency integrity verification (VC++ redistributables, WebView2 Runtime presence, DLL and model checksums) to prevent ghost deployment failures.
- **Windows Edge Cases:** Harden the Windows UI Automation (dynamic COM) against multi-monitor setups, varying DPI scaling, and registry read/write restrictions. Standardize coords to use per-window live DPI scaling (HWND monitor aware) and perform post-click semantic verification sampling to catch coordinate drift.
- **OS State Transitions:** Gracefully detect and handle Windows sleep, hibernation, system lock, wake cycles, and unexpected process termination or power cuts without corrupting local stores. Incorporate a wake stabilization delay before resuming active workflows.
- **Environment Divergence:** Mitigate antivirus false-positives, browser driver mismatches (Playwright/Chrome/Edge), and partial permissions (whitelisted containment zone violations) with smart WebView2 fallbacks. Display capability degradation warnings transparently to maintain trust.

</domain>

<decisions>
## Implementation Decisions

### D1A: Installer & Privileges Resilience
- **D1-01: Low-Privilege Containment:** Ensure Engram runs entirely inside `%LOCALAPPDATA%/Engram/` without requiring local administrator escalation, creating fallback directories if access is denied.
- **D1-02: Self-Validating Launcher:** The Tauri shell must run a pre-launch diagnostic script verifying sidecar executable checksums, DLL presence, WebView2 / VC++ redistributable status, directory read/write rights, and port 5000 availability.
- **D1-03: Survivability-Oriented Severity Levels:** Do not over-block startup. Classify checks into Info, Warning (degraded launch acceptable for missing WebView2, missing model, GPU failure), IntegrityUncertain (quarantine/Safe-Mode launch for corrupted propagation graph or failed replay reconciliation), and Critical (startup blocked only on missing sidecar binary, corrupted write layer, or unrecoverable WAL).
- **D1-04: Safe-Mode / Quarantine Startup:** If IntegrityUncertain triggers, launch the sidecar in Safe-Mode. The system runs read-only (commits blocked), suspends event propagation, disables task executions, and displays a recovery console to resolve semantic contradictions.

### D1B: Windows Edge Cases
- **D1-05: Per-Window Live DPI Coordinates:** Standardize Win32 COM click coordinates and viewport screenshots to use per-window live DPI resolution rather than global scaling, recalculating dynamically during monitor migration.
- **D1-06: Coordinate Verification Sampling:** Validate click automation outcomes post-click using accessibility focus queries and window handle verification to detect spatial drift.
- **D1-07: Registry Fallback:** Replace direct registry edits with file-based configs in `.engram/` if access to `HKCU` or `HKLM` is blocked by group policy.

### D1C: OS State Transitions
- **D1-08: Wake-up Re-Initialization:** Background metabolic loops and the inference engine must hook into the Win32 `WM_POWERBROADCAST` and `PBT_APMRESUMESUSPEND` events to flush memory state on sleep and re-initialize drivers on resume.
- **D1-09: Wake Stabilization Delay:** Enforce an adaptive 2-10 second stabilization window after sleep recovery to allow stale handles, suspended COM, network, and filesystems to recover before workflows resume.
- **D1-10: Transactional Semantic Envelopes:** Replace single-file writes with Write Intent Journaling (TransactionId, Status, AffectedEntities, RollbackMarkers) in the WAL to preserve multi-file semantic atomicity and perform atomic rollbacks on startup.

### D1D: Environmental Divergence & Fallbacks
- **D1-11: Behavioral Transparency Profiles:** Generate structured profiles listing whitelisted folders, port bindings, and browser control domains to establish trust deployment infrastructure for anti-virus heuristic audits.
- **D1-12: WebView2 Driver Fallback:** If Playwright Edge fails to launch, fall back to headless WebView2 orchestration automatically.
- **D1-13: Capability Surface Transparency & Legibility:** Expose active degradation states legibly (e.g. WebView2 fallback details like missing extensions, altered session isolation, or degraded cookie continuity) via health diagnostics and render clear UX warnings.
- **D1-14: Degradation Persistence & Pathology Recovery Curves:** Persist environment failure history in `.engram/diagnostics/pathology_memory.json`. Implement a recovery curve ($D(t) = D_0 \cdot e^{-\lambda t}$) that decays distrust and schedules revalidation queries to forgive transient failures.
- **D1-15: Capability Hysteresis:** Enforce minimum stable intervals (e.g. 5 minutes) before re-promoting recovered capabilities to prevent state oscillation.
- **D1-16: Semantic Capability Propagation & Subsystem Adaptation:** Propagate capability and perception degradation scores globally (e.g., Playwright DOM: 0.95, WebView2: 0.62) to downstream reasoning engines to enforce autonomy ceilings and epistemic caution.

</decisions>

<canonical_refs>
## Canonical References

- `.planning/PROJECT.md` — Core architecture layout (Tauri Rust shell, .NET sidecar, React frontend).
- `.planning/STATE.md` — Known issues (Windows Defender quarantine, Vulkan on clean machines).
- `src/Engram.App/installer.nsi` — Current NSIS installer configuration.
- `src/Engram.App/validate-install.ps1` — Validation script after installation.

</canonical_refs>

<code_context>
## Existing Code Insights

- `InferenceLifecycleManager.cs` — The single source of truth for runtime state and degradation triggers. Can be extended to track homeostasis and long-term degradation.
- `ActionRuntime.cs` — Controls execution loops. Needs to listen for OS sleep/wake cancellations to release device/automation locks.
- `CausalReconciler.cs` — Repairs WAL sequence fractures on startup. Crucial for recovery after sudden power cuts.

</code_context>

<deferred>
## Deferred Ideas
- Cross-platform porting (macOS/Linux) — Strictly deferred until Windows-first deployment is 100% hardened.
- Cloud-first database sync.

</deferred>
