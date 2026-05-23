# Phase D5: Productization - Context

**Gathered:** 2026-05-23
**Status:** Planned

<domain>
## Phase Boundary

Prepare Engram for public adoption by polishing the installer, configuring secure updates, designing the permission/onboarding UX, and building interfaces for governance transparency.

Objectives:
- **Onboarding:** A seamless first-run experience, including dynamic local model downloading, system requirements checks, and the Discovery SOP.
- **UX & UI Transparency:** Make Engram's inner thinking visible to the user. Show active tasks, reasons for interventions, and local token budgets clearly.
- **Packaging & Updates:** Bundle signed binaries inside the NSIS installer. Set up Tauri's secure auto-updater with signature checks.
- **Privacy Guarantees & Permission UI:** Expose the safety constitution limits, whitelisted directory zones, and memory sovereign deletion options in the settings interface, putting full control back in the user's hands.

</domain>

<decisions>
## Implementation Decisions

### D5A: Governance UI & Transparency
- **D5-01: Narration Stream View:** Build a visual trace panel in the Chat UI showing why Engram retrieved specific memories, what contradictions it detected, and what actions it took.
- **D5-02: Sovereign Deletion Portal:** Design a settings screen enabling the user to search the wiki, view associated raw event history, and permanently purge entities with deletion validation reports.

### D5B: Packaging & Update Pipelines
- **D5-03: Signed NSIS Installer:** Configure GitHub actions / build scripts to sign both Tauri and the .NET sidecar assemblies with trusted certificates, preventing SmartScreen warning popups.
- **D5-04: Tauri Updater Integration:** Set up Tauri's built-in updater system referencing an HTTPS endpoint with public key verification, ensuring updates can be pushed and verified locally.

</decisions>

<canonical_refs>
## Canonical References

- `src/Engram.App/installer.nsi` — Current installer script.
- `src/Engram.App/validate-install.ps1` — Verification script.
- `.planning/PROJECT.md` — Section 11 on explainability narration and memory sovereignty.

</canonical_refs>

<code_context>
## Existing Code Insights

- `DiscoveryInterview.tsx` — Exists in frontend to guide initial setup.
- `ExplainabilityNarration.cs` — Converts reasoning traces to plain text.
- `MemorySovereignty.cs` — Enforces deletion envelopes.
- `/api/governance/settings` — Endpoint for boundary and config management.

</code_context>

<deferred>
## Deferred Ideas
- Cross-platform distribution (e.g. MSIX/UWP) — Stick to signed NSIS executables for broad Win10/11 compatibility.
- Cloud billing dashboard (all billing metrics remain local and in-app).

</deferred>
