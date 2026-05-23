# Phase D1: Reality Hardening - Context

**Gathered:** 2026-05-23
**Status:** Active Phase

<domain>
## Phase Boundary

Establish absolute runtime resilience for Engram across messy, diverse, and unpredictable Windows environments. Hardening aims to eliminate failures caused by installer privileges, OS dynamic state transitions, hardware configuration variances, and hostile host environments (antivirus/permissions/divergent browsers).

Objectives:
- **Installer Resilience:** Ensure the desktop shell and .NET sidecar install, launch, and update robustly under both Admin and non-Admin standard user privileges, resolving any dynamic environment path or folder permission issues.
- **Windows Edge Cases:** Harden the Windows UI Automation (dynamic COM) against multi-monitor setups, varying DPI scaling, and registry read/write restrictions.
- **OS State Transitions:** Gracefully detect and handle Windows sleep, hibernation, system lock, wake cycles, and unexpected process termination or power cuts without corrupting local stores.
- **Environment Divergence:** Mitigate antivirus false-positives, browser driver mismatches (Playwright/Chrome/Edge), and partial permissions (whitelisted containment zone violations) with smart fallbacks.

</domain>

<decisions>
## Implementation Decisions

### D1A: Installer & Privileges Resilience
- **D1-01: Low-Privilege Containment:** Ensure Engram runs entirely inside `%LOCALAPPDATA%/Engram/` without requiring local administrator escalation, creating fallback directories if access is denied.
- **D1-02: Self-Validating Launcher:** The Tauri shell must run a pre-launch diagnostic script verifying sidecar executable checksums, directory read/write rights, and port 5000 availability.

### D1B: Windows Edge Cases
- **D1-03: DPI & Scale Aware coordinates:** Standardize Win32 COM click coordinates and viewport screenshots to use logical DPI-aware math, preventing off-screen clicks on multi-monitor setups.
- **D1-04: Registry Fallback:** Replace direct registry edits with file-based configs in `.engram/` if access to `HKCU` or `HKLM` is blocked by group policy.

### D1C: OS State Transitions
- **D1-05: Wake-up Re-Initialization:** Background metabolic loops and the inference engine must hook into the Win32 `WM_POWERBROADCAST` and `PBT_APMRESUMESUSPEND` events to flush memory state on sleep and re-initialize drivers on resume.
- **D1-06: Corruption-Safe WAL Flush:** Flush all pending event writes to disk using atomic rename operations rather than direct appends, ensuring partial writes during system crashes do not invalidate the whole store.

### D1D: Environmental Divergence & Fallbacks
- **D1-07: Anti-Virus Exclusion Guidelines:** Generate signed manifests and diagnostic reports outlining exactly what files are accessed to prevent behavioral heuristic flags from Defender/avast/etc.
- **D1-08: Dual Browser Driver Fallback:** If Playwright Edge fails to launch, fall back to headless WebView2 orchestration or standard browser automation interfaces automatically.

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
