# Phase D4: Real Task Execution Validation - Context

**Gathered:** 2026-05-23
**Status:** Planned

<domain>
## Phase Boundary

Validate that Engram can successfully plan, execute, and verify complex end-to-end tasks (e.g. generating briefings, conducting research, retrieving files, operating browsers, compiling spreadsheets) across messy desktops and chaotic environments, recovering gracefully from human interruptions.

Objectives:
- **Real Task Robustness:** Execute comprehensive multi-system workflows (e.g., extracting transaction tables from a browser tab, saving it to local Excel/CSV, generating a summary report, and posting it).
- **Interruption Tolerance & Recovery:** Gracefully handle cases where the user hijacks input focus, mouse control, or clipboard mid-execution. Pause immediately and resume without repeating completed steps or causing corrupted outcomes.
- **Messy Desktop Adaptation:** Ensure dynamic COM UI Automation handles window overlap, minimized states, unexpected popups, and missing files without throwing unhandled exceptions.

</domain>

<decisions>
## Implementation Decisions

### D4A: Interruption Recovery & Takeover
- **D4-01: Focus Hijack Guard:** The Win32 mouse/keyboard driver must abort active script playback instantly if it detects manual user inputs (e.g., physical keypresses or mouse movement offsets) during execution.
- **D4-02: Checkpoint-Resume Cycle:** Save execution step progress to local JSON checkpoint stores after every successful node traversal in the Action Graph, allowing resumption from the last verified safe state.

### D4B: Desktop Chaos Tolerance
- **D4-03: Sandbox Verification Gates:** Before executing filesystem operations or window manipulation, run a fast, non-intrusive dry-run check ensuring target file paths exist and destination window handles are valid.
- **D4-04: Auto-Minimize Restoration:** If a target window is minimized or covered, use the Win32 `ShowWindow` API with `SW_RESTORE` to bring it into interactive focus before executing inputs.

</decisions>

<canonical_refs>
## Canonical References

- `.planning/phases/07-salience-and-drift/07-CONTEXT.md` — Early reference to action graphs and browser drivers.
- `src/Engram.Store/Automation/SandboxManager.cs` — Safe directory whitelist enforcement.
- `src/Engram.Store/Automation/WorkflowCheckpoint.cs` — Pause and resume state serialization.

</canonical_refs>

<code_context>
## Existing Code Insights

- `ActionRuntime.cs` — The execution loop manager. Uses `ActionRuntime.Pause()` to cancel tokens cleanly.
- `IBrowserDriver.cs` — Abstraction for browser orchestration.
- `IUiEmbodimentProvider.cs` — Decoupled UI drivers.

</code_context>

<deferred>
## Deferred Ideas
- Support for complex macros/custom scripts in third-party applications (out of scope; we focus on core OS capability and browser tasks).

</deferred>
