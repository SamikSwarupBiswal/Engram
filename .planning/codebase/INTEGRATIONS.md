# External Integrations

**Analysis Date:** 2026-05-23

## APIs & External Services

**Google Workspace Integrations:**
- **Google Calendar / Gmail / Google Drive** - Used to ingest metadata (email headers, event subjects, file names/metadata) for user context extraction.
  - Client/SDK: Custom HTTP client implementations in `GoogleWorkspaceManager`, `GmailMetadataProvider`, `CalendarMetadataProvider`, `DriveMetadataProvider`.
  - Auth: OAuth 2.0 flow using standard Google client credentials (`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`) via `GoogleOAuthManager`.
  - Scopes: Minimal metadata-only scopes to preserve privacy (e.g., `gmail.readonly` metadata, `calendar.events.readonly`, `drive.metadata.readonly`).

**Cloud LLM Providers:**
- **OpenAI / Claude / Gemini API** - Used in "Turbo" power mode for advanced reasoning and larger context tasks.
  - Connection: Standard HTTPS client targeting OpenAI-compatible endpoints.
  - Auth: API Key configured via `api/provider` and saved locally.
  - Cost tracking: Audited per request via `BudgetManager`.

## Data Storage

**Local File System (No Server Database):**
- **Markdown & JSON Store** - All user memory, timeline events, and settings reside in the local `%USERPROFILE%/.engram/` workspace.
  - **Raw Events**: Saved in append-only JSON files (`.engram/raw/YYYY-MM-DD/{id}.json`).
  - **Wiki Nodes**: Saved as Markdown files (`.engram/wiki/{id}.md`) with YAML front matter.
  - **Logs & Archives**: Saved under `.engram/logs/` (including `cloud-audit.jsonl`) and `.engram/archives/` respectively.
  - **Procedural Memory**: Saved to `.engram/automation/procedural_memory.json`.

## Authentication & Identity

**OAuth Integration:**
- **Google OAuth 2.0** - Direct local redirect flow for Workspace ingestion. The app spins up a temporary HTTP listener during the redirect to capture the auth code.

## Operating System & Native Services

**Windows Graphics Capture / Desktop Perception:**
- **ScreenCaptureService** - Native Windows capture APIs used to capture desktop frames for layout analysis and OCR.
  - SDK: `System.Drawing.Common` and custom Win32 interop wrappers.
  - System dependency: Requires Windows Graphics Capture APIs (supported natively on Win 10 1903+).

**Windows UI Automation (COM):**
- **COM UI Automation Interface** - Used to interact with on-screen controls, extract window handles, and read tree node values dynamically.
  - Implementation: Dynamic COM wrapper (`IUIAutomation` interfaces) to operate without static platform lock-in.

**Win32 Input Simulation:**
- **SendInput API** - Simulation of keyboard and mouse events to drive Windows operations.
  - Safety mitigation: Containment zones and LIFO rollback handlers prevent destructive inputs.

**Web Automation (Playwright):**
- **Chromium / Playwright** - Playwright integration (`IBrowserDriver` interface) to automate browser tabs.
  - Executable: Custom Chromium binary downloaded locally and controlled headlessly.

---

*Integration audit: 2026-05-23*
*Update when adding/removing external services*
