# Phase 3: Local Ingestion MVP - Context

**Gathered:** 2026-05-13
**Status:** Decisions locked
**Source:** Discuss-phase with user approval

## Phase Boundary

Phase 3 adds local capture sources with explicit consent, exclusion enforcement, and production-grade reliability.

Out of scope:
- Markdown wiki generation (Phase 4)
- Search, tray UI, identity, drift, cloud, GWS, research, automation, encryption

## Implementation Decisions

### D-016: FileSystemWatcher with Production Hardening
- Use System.IO.FileSystemWatcher as base
- Debounce: 500ms window, coalesce duplicate events
- Rate limiter: max 100 events/second, drop excess with warning
- Self-filter: ignore changes under .engram/ workspace path
- Error recovery: watcher.Error event -> restart watcher
- Buffer: 64KB internal buffer, log overflow warnings

### D-017: Clipboard Monitoring via Polling
- Poll every 500ms using platform clipboard API
- Content hash (SHA-256) to detect actual changes
- Skip if active window is in excluded apps list
- Dev fallback: IClipboardProvider interface for WSL/testing

### D-018: Active Window via Polling
- Poll every 1 second
- Extract: process name, window title, executable path
- Used for: source attribution + excluded app enforcement
- Dev fallback: IActiveWindowProvider interface for WSL/testing

### D-019: Provider Interface Pattern
- IFileCaptureProvider: Start/Stop watching, OnFileChanged event
- IClipboardProvider: GetClipboardContent(), GetActiveWindow()
- IActiveWindowProvider: GetActiveWindowInfo()
- IOcrProvider: ExtractText(image bytes) -> string
- All interfaces have mock implementations for testing

### D-020: Consent Model (NFR-004)
- All capture sources OFF by default
- Each source independently toggleable via EngramConfig
- ExclusionList: process names that are NEVER captured
- Config persisted via EngramConfigStore
- Changes take effect immediately (hot-reload)

### D-021: Event Flood Protection
- Debouncer: coalesces rapid file changes into single event
- Rate limiter: token bucket algorithm, configurable rate
- Batch grouping: similar events within window are batched
- Circuit breaker: if rate exceeded for 10s, pause capture for 30s

### D-022: Excluded App Enforcement
- Match by process name (case-insensitive)
- Checked at: clipboard capture, active window capture
- Default exclusions: password managers, banking apps
- User can add/remove via config

## Canonical References
- Artifacts/Product Requirements Document_Engram Full Specification.md (§2.1)
- Artifacts/Engram Implementation Plan.md (Phase 2)
- .planning/REQUIREMENTS.md (REQ-007..009, NFR-004)

*Phase: 03-local-ingestion-mvp*
*Context gathered: 2026-05-13*
