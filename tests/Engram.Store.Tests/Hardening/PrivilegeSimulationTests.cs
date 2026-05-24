using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Wiki;
using Engram.Store.Automation;

namespace Engram.Store.Tests.Hardening;

public class PrivilegeSimulationTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ENGRAM_SAFE_MODE", null);
        _workspace.Dispose();
    }

    [Fact]
    public void SafeMode_BlocksWikiNodeSave()
    {
        Environment.SetEnvironmentVariable("ENGRAM_SAFE_MODE", "true");
        using var store = new WikiNodeStore(_workspace.Paths);

        var node = new WikiNode
        {
            NodeId = "test-node",
            Title = "Test Node",
            Summary = "Test Content",
            NodeType = WikiNodeType.Project
        };

        Assert.Throws<InvalidOperationException>(() => store.Save(node));
    }

    [Fact]
    public void SafeMode_BlocksWikiNodeDelete()
    {
        Environment.SetEnvironmentVariable("ENGRAM_SAFE_MODE", "true");
        using var store = new WikiNodeStore(_workspace.Paths);

        Assert.Throws<InvalidOperationException>(() => store.Delete("test-node"));
    }

    [Fact]
    public void SafeMode_BlocksRawEventWriter()
    {
        Environment.SetEnvironmentVariable("ENGRAM_SAFE_MODE", "true");
        using var writer = new RawEventWriter(_workspace.Paths, new ContentHasher());

        var evt = TestEvents.Create();

        Assert.Throws<InvalidOperationException>(() => writer.Write(evt));
    }

    [Fact]
    public async Task SafeMode_BlocksActionRuntimePlanExecution()
    {
        Environment.SetEnvironmentVariable("ENGRAM_SAFE_MODE", "true");

        // Mock dependencies
        var executor = new ActionExecutor();
        var gate = new PermissionGate();
        using var runtime = new ActionRuntime(executor, gate);

        var plan = new ExecutionPlan
        {
            PlanId = "plan-1",
            Goal = "Test Goal"
        };
        var context = new Engram.Store.Automation.ExecutionContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, context));
    }

    [Fact]
    public void PrivilegeConfinement_WritesContainWithinWorkspaceRoot()
    {
        // Safe mode disabled
        Environment.SetEnvironmentVariable("ENGRAM_SAFE_MODE", "false");

        using var store = new WikiNodeStore(_workspace.Paths);
        var node = new WikiNode
        {
            NodeId = "confinement_node",
            Title = "Confinement Title",
            Summary = "Content",
            NodeType = WikiNodeType.Goal
        };

        store.Save(node);

        var expectedPath = Path.Combine(_workspace.Paths.Wiki, "confinement_node.md");
        Assert.True(File.Exists(expectedPath));
        Assert.StartsWith(_workspace.Root, expectedPath);
    }
}
