# Testing Patterns

**Analysis Date:** 2026-05-23

## Test Framework

**Runner:**
- **xUnit 2.5.3** - Managed by the standard `Microsoft.NET.Test.Sdk`.
- Configured as standard .NET test projects.

**Assertion Library:**
- xUnit built-in assertions (`Assert.Equal`, `Assert.Null`, `Assert.NotNull`, `Assert.Throws`, `Assert.True`, `Assert.False`).

**Run Commands:**
```bash
dotnet test                              # Run all tests in the solution
dotnet test --filter Category=Unit       # Filter by trait/category
dotnet test tests/Engram.Store.Tests/    # Run only backend Store tests
```

## Test File Organization

**Location:**
- Test files live under the `tests/` root directory in dedicated test projects.
- Backend library tests are in `tests/Engram.Store.Tests/`.
- API integration tests are in `tests/Engram.Api.Tests/`.
- Structure mirrors the source namespace layout.

**Structure Example:**
```
tests/
├── Engram.Store.Tests/
│   ├── Wiki/
│   │   ├── WikiNodeStoreTests.cs
│   │   └── WikiNodeSerializerTests.cs
│   ├── Search/
│   │   └── SemanticSearchEngineTests.cs
│   └── Sprint7ValidationSuite.cs
└── Engram.Api.Tests/
    └── HealthTests.cs
```

## Test Structure

**Suite Organization:**
Tests follow the standard xUnit class structure:
```csharp
using Xunit;
using Engram.Store.Wiki;

namespace Engram.Store.Tests.Wiki;

public class WikiNodeStoreTests
{
    [Fact]
    public void Save_ValidNode_WritesToFile()
    {
        // Arrange
        using var tempDir = new TempWorkspace();
        var store = new WikiNodeStore(tempDir.Paths);
        var node = new WikiNode("node-1", "Test Title");

        // Act
        store.Save(node);

        // Assert
        Assert.True(store.Exists("node-1"));
        var loaded = store.Load("node-1");
        Assert.NotNull(loaded);
        Assert.Equal("Test Title", loaded.Title);
    }
}
```

**Patterns:**
- **AAA (Arrange, Act, Assert):** Group tests visually using whitespace into these three logical steps.
- **Fact vs Theory:** Use `[Fact]` for single-condition tests. Use `[Theory]` along with `[InlineData]` for parameter-driven scenarios testing multiple boundaries.
- **Fixture / Workspace Cleanup:** Use `IDisposable` or helper utility classes (`TempWorkspace`) to create isolated test directories on disk and automatically delete them in the teardown phase.

## Mocking

**Strategy:**
- The codebase relies on constructor Dependency Injection.
- Abstract interfaces (e.g. `ILogger`, `IBrowserDriver`, `IUiEmbodimentProvider`) are mocked using lightweight stubs or mock implementations created in the test project.
- Mocking libraries are minimized; explicit stub class definitions (e.g., `TestUiEmbodimentProvider`) are preferred for reliability.

## Coverage

- **Reporting Tool:** `Coverlet.Collector` is included in the test projects.
- **Verification Gate:** Verification suites are configured to run locally and must pass 100% green before commits or installer compilation.

---

*Testing analysis: 2026-05-23*
*Update when test patterns change*
