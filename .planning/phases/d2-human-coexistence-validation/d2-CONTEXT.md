# Phase D2: Human Coexistence Validation - Context

**Gathered:** 2026-05-23
**Status:** Completed

<domain>
## Phase Boundary

Optimize the friction, pacing, and presence of Engram to achieve a non-intrusive, respectful, and calm coexistence with its human user.

Objectives:
- **Intervention Fatigue:** Measure and limit proactive suggestions, keeping alert dispatches within reasonable human cognitive bounds.
- **Annoyance Archaeology:** Track behavioral indicators of annoyance (e.g., dismissing alerts within 1 second, repeatedly cancelling tasks, ignoring interventions, manual trust override adjustments) to learn boundaries automatically.
- **Workflow Frustration:** Avoid interfering with active foreground user workflows. Prevent attention hijacking.
- **Semantic Creep Detection:** Prevent Engram from drawing premature conclusions about user life/habits (e.g. over-interpreting temporary research as procrastination or burnout) and allow users to ground interpretations easily.

</domain>

<decisions>
## Implementation Decisions

### D2A: Annoyance & Friction Telemetry
- **D2-01: Micro-Friction Event Logging:** Record user interface interactions (Swift cancel clicks, close buttons, "don't ask again" flags) as explicit friction vectors in the EventBus.
- **D2-02: Silence Threshold Scaling:** If friction events cluster (e.g., >3 dismissals in 6 hours), automatically increase the safety constitution restraint multipliers, silencing non-essential prompts for 48 hours.

### D2B: Pacing & Interruption Gates
- **D2-03: Yield-to-Focus Protocol:** If foreground application switches frequently (high-velocity multitasking), block all proactive notifications, queuing them into Cognitive Debt for idle processing.
- **D2-04: Trust-Pacing Warmups:** Limit newly discovered automation capabilities (e.g. file deletion) to require explicit manual approval gates for the first 10 runs, slowly graduating to delegation based on historical correctness.

</decisions>

<canonical_refs>
## Canonical References

- `.planning/phases/12-longitudinal-endurance/12-CONTEXT.md` — Section 12B/D details on pacing controllers and friction trackers.
- `src/Engram.Store/Governance/FrictionTracker.cs` — The core logic tracking dismissals and escalations.
- `src/Engram.Store/Governance/PacingController.cs` — Token-bucket rate limiting logic.

</canonical_refs>

<code_context>
## Existing Code Insights

- `CognitiveRestraintEngine.cs` — Exposes the 9 gates controlling when Engram should speak.
- `TruthCalibrationStore.cs` — Persistent store for user reality corrections.
- `InterventionFatigueTracker.cs` — Tracks dismissal, ignore, and action rates.

</code_context>

<deferred>
## Deferred Ideas
- Elaborate user feedback surveys or popup questionnaires (Strictly out of scope; telemetry must remain silent, behavioral, and local).
- Settings-heavy UI config panels (Preference is for Engram to learn restraint behaviorally).

</deferred>
