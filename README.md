# Engram

A Windows-first personal semantic operating layer. Captures your digital life, builds a structured wiki, and lets you search, recall, and reason over everything.

## Quick Start

```
1. Download Engram_1.0.0_x64-setup.exe (72 MB)
2. Run installer
3. Open Engram from Start Menu
4. Chat, search, research — it just works
```

## What It Does

- **Capture**: Clipboard, files, active windows (opt-in)
- **Metabolize**: Events → structured wiki with facts, sources, links
- **Search**: TF-IDF keyword search over wiki memory
- **Briefs**: Morning/evening summaries with citations
- **Chat**: Local Phi-4-mini inference (Eco mode) or cloud (Turbo mode)
- **Research**: Multi-step web research with citations
- **Automation**: Desktop actions with permission gating
- **Google Workspace**: Email, calendar, drive metadata ingestion
- **Security**: AES-256 encryption, export, delete, encrypted sync

## Tech Stack

| Layer | Tech |
|-------|------|
| Shell | Tauri v2 (Rust) |
| Frontend | React 19 + TypeScript + Tailwind |
| Backend | .NET 8 Minimal API |
| Inference | LLamaSharp + Vulkan |
| Model | Phi-4-mini GGUF (2.3GB) |
| Storage | Markdown (.engram/) |
| Encryption | AES-256-GCM |

## Architecture

```
User opens Engram
  → Tauri shell spawns .NET API sidecar
  → React frontend connects to sidecar
  → Chat/Search/Wiki/Timeline/Settings/Archive/Research/Automation
  → Model auto-downloads on first launch
```

## Free vs Pro

| Feature | Free | Pro ($20-30/mo) |
|---------|------|----------------|
| Local inference (Phi-4-mini) | ✓ | ✓ |
| User's own API keys | ✓ | ✓ |
| Managed cloud credits | ✗ | ✓ |
| Google Workspace | ✗ | ✓ |
| Research agent | ✗ | ✓ |
| Automation | ✗ | ✓ |
| Monthly tokens | ~60K | 500K |

## Project Structure

```
src/
  Engram.Store/     Core library (12 layers)
  Engram.Api/       ASP.NET API sidecar (75 endpoints)
  Engram.App/       Tauri + React frontend (10 views)
  Engram.Cli/       Developer CLI
tests/
  Engram.Store.Tests/  849 tests
  Engram.Api.Tests/     84 tests
```

## Build

```powershell
# From Windows PowerShell
cd src\Engram.App
.\build-nsis-installer.ps1
# Output: Engram_1.0.0_x64-setup.exe (72 MB)
```

## Tests

```
dotnet test
# 933/933 passing (849 Store + 84 API)
```

### Soak Tests (runtime stability)

```
dotnet test --filter "Category=Soak"
# Requires running API sidecar with model loaded
```

See [.planning/soak-validation.md](.planning/soak-validation.md) for results.

## Runtime Status (Soak Validation)

**Branch:** `soak-validation`

Key findings from soak testing:
- KV cache accumulates across requests (never clears) — deterministic collapse at ~2000 tokens
- Health endpoint false positive after runtime death
- Prompt template is model-specific (Phi-4-mini requires `<|system|>/<|user|>/<|assistant|>`)
- Phase-transition failure pattern (healthy → dead, no gradual degradation)

See [.planning/](.planning/) for detailed findings and next steps.

## License

Proprietary
