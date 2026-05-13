# ADR-001: Stack Choice — .NET/C#

## Status

Accepted — 2026-05-10

## Context

Engram is a Windows-first personal semantic operating layer. It needs:
- Deep Windows integration (system tray, global hotkeys, file watching, OCR)
- Background service capabilities
- Strong typing and testability
- Good performance for local file I/O and JSON processing
- Ability to ship as a single distributable

## Decision

Use C# on .NET 8 as the primary implementation stack.

## Rationale

1. **Windows-native:** Best integration with Windows APIs (WinRT for OCR, system tray, file watchers, COM interop)
2. **Performance:** Compiled language with good JSON serialization performance for high-throughput event logging
3. **Ecosystem:** Mature libraries for file watching (System.IO), JSON (System.Text.Json), HTTP, background services, and testing (xUnit)
4. **Tooling:** Excellent VS Code / Visual Studio support, dotnet CLI, NuGet ecosystem
5. **Distribution:** Single-file publish, self-contained executables, Windows installer tooling
6. **Type safety:** Strong typing catches data contract issues at compile time — important for the raw event schema

## Alternatives Considered

- **Node.js/TypeScript:** Good for rapid prototyping but weaker Windows integration, garbage collection pauses, and less natural for background services
- **Rust:** Excellent performance but higher development cost, smaller ecosystem for Windows UI/service patterns
- **Python:** Good for prototyping but not suitable for a background service with system tray UI and Windows API integration

## Consequences

- All team members need .NET 8 SDK
- Tests run via `dotnet test` on Windows
- CI uses `windows-latest` runner
- Provider interfaces use C# interface/abstract class patterns
