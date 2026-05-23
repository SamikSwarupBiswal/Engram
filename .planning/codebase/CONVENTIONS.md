# Coding Conventions

**Analysis Date:** 2026-05-23

## Naming Patterns

**C# Files & Code:**
- **PascalCase** for classes, interfaces (prefixed with `I`), methods, public properties, public fields, and enum members (e.g., `WikiNodeStore`, `IUiEmbodimentProvider`, `Save()`, `NodeId`, `TierLevel.Pro`).
- **camelCase with leading underscore (`_`)** for private instance fields (e.g., `_wikiPath`, `_logger`, `_lock`).
- **camelCase** for parameters and local variables (e.g., `nodeId`, `filePath`).
- **File-scoped Namespaces:** File-scoped namespace matching directory layout is required (e.g. `namespace Engram.Store.Wiki;`).
- **File Names:** Match the name of the primary class declared within (`WikiNodeStore.cs`).

**Frontend TypeScript & React:**
- **PascalCase** for React component files and functions (`Sidebar.tsx`, `ChatWindow.tsx`).
- **camelCase** for variables, functions, and properties (`apiClient`, `handleSendMessage`).
- **UPPER_SNAKE_CASE** for global constants.

## Code Style

**C# / .NET:**
- **Indentation:** 4 spaces for C#.
- **Curly Braces:** Open and close braces on separate lines (Allman style), except for simple getter/setter properties.
- **Null Safety:** Enable `<Nullable>enable</Nullable>`. Explicitly handle null properties and arguments. Use `?` for optional references (`ILogger?`).

**Frontend / TypeScript:**
- **Indentation:** 2 spaces.
- **Formatting:** Single quotes for string literals, semicolons required.

## Import & Using Organization

**C# Usings:**
- Group alphabetically.
- Put System namespaces first, followed by Microsoft namespaces, then project internal namespaces.
- Keep them clean; remove unused usings.

**TypeScript / React Imports:**
- Order: External libraries (e.g., `react`, `@tauri-apps/api`), internal path aliases (e.g., `@/lib`, `@/components`), relative imports, type imports last.

## Error Handling

**Strategy:** Exception bubbling combined with graceful degradation.
- **Guard Clauses:** Use early checks like `ArgumentNullException.ThrowIfNull(arg)` or throws for invalid states.
- **Try/Finally for Resources:** Always execute cleanup (`Dispose()`, lock releases, or process terminators) in `finally` blocks.
- **Log and Suppress for Non-Critical Operations:** In background workers or loops (like metadata providers reading email files), wrap items in a try-catch, log the warning, and continue to the next item instead of crashing the process.

## Logging

**Framework:**
- Backend utilizes `Microsoft.Extensions.Logging` injected via Dependency Injection (`ILogger<T>`).
- Use structured log templates: `_logger?.LogDebug("Saved wiki node: {NodeId} -> {Path}", node.NodeId, filePath);` instead of string interpolation.
- UI uses custom logs routed through Tauri IPC or standard console outputs during dev.

## Documentation & Comments

**JSDoc & XML Comments:**
- C# classes and public methods must utilize XML documentation comments (`/// <summary>`).
- Describe behavior, arguments, returns, and potential throws.
- Explanations should target *why* code exists rather than *what* it is doing mechanically.

---

*Convention analysis: 2026-05-23*
*Update when patterns change*
