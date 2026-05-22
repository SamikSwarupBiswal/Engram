using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Engram.Store.Events;
using Engram.Store.Perception;
using Engram.Store.Reality;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class TemporalFusionEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly CrossModalResolver _resolver;
    private readonly InMemoryEventBus _eventBus;
    private readonly TemporalFusionEngine _engine;

    public TemporalFusionEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_fusion_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _nodeStore = new WikiNodeStore(_paths);
        
        SeedResolverData();
        
        _resolver = new CrossModalResolver(_nodeStore);
        _eventBus = new InMemoryEventBus();
        _engine = new TemporalFusionEngine(_resolver, _eventBus);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _eventBus.Dispose();
        _nodeStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void SeedResolverData()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "proj_engram",
            Title = "Engram Project",
            NodeType = WikiNodeType.Project,
            Facts = new List<WikiFact>
            {
                new() { Text = "path: c:\\projects\\Engram" },
                new() { Text = "window: *Engram*" }
            }
        });
    }

    [Fact]
    public void FusesState_OnActiveWindowChangedEvent()
    {
        FusedChronologyEntry? fused = null;
        var resetEvent = new ManualResetEvent(false);

        _eventBus.Subscribe("reality.temporal_fused", envelope =>
        {
            fused = envelope.Payload as FusedChronologyEntry;
            resetEvent.Set();
        });

        // Publish window change event
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "perception.active_window_changed",
            Source = "active_window_service",
            Payload = new
            {
                Process = "code",
                Title = "Program.cs - Engram - VS Code"
            }
        });

        Assert.True(resetEvent.WaitOne(1000));
        Assert.NotNull(fused);
        Assert.Equal("code", fused!.WindowProcess);
        Assert.Equal("Program.cs - Engram - VS Code", fused.WindowTitle);
        Assert.Equal("proj_engram", fused.ResolvedNodeId);
    }

    [Fact]
    public void FusesState_OnWorldModelChangedEvent()
    {
        FusedChronologyEntry? fused = null;
        var resetEvent = new ManualResetEvent(false);

        _eventBus.Subscribe("reality.temporal_fused", envelope =>
        {
            fused = envelope.Payload as FusedChronologyEntry;
            resetEvent.Set();
        });

        // Publish ActiveWorkflow update
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "automation.worldmodel.changed",
            Source = "operational_world_model",
            Payload = new
            {
                Property = "ActiveWorkflow",
                Value = "wf_123"
            }
        });

        Assert.True(resetEvent.WaitOne(1000));
        Assert.NotNull(fused);
        Assert.Equal("wf_123", fused!.WorkflowId);
    }

    [Fact]
    public void FusesState_OnSemanticFileEvent()
    {
        FusedChronologyEntry? fused = null;
        var resetEvent = new ManualResetEvent(false);

        _eventBus.Subscribe("reality.temporal_fused", envelope =>
        {
            fused = envelope.Payload as FusedChronologyEntry;
            resetEvent.Set();
        });

        // Publish file changed event
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "perception.file_changed",
            Source = "file_watcher_service",
            Payload = new SemanticFileEvent
            {
                FilePath = "c:\\projects\\Engram\\src\\Reality\\TemporalFusionEngine.cs",
                FileName = "TemporalFusionEngine.cs",
                Extension = ".cs",
                Directory = "Reality",
                ChangeType = "changed",
                Category = "source_code",
                Timestamp = DateTimeOffset.UtcNow
            }
        });

        Assert.True(resetEvent.WaitOne(1000));
        Assert.NotNull(fused);
        Assert.Equal("c:\\projects\\Engram\\src\\Reality\\TemporalFusionEngine.cs", fused!.ActiveDocumentPath);
        Assert.Equal("proj_engram", fused.ResolvedNodeId);
    }

    [Fact]
    public void ForceFusion_GeneratesSynchronousUpdate()
    {
        var entry = _engine.ForceFusion("Coding task");
        Assert.NotNull(entry);
        Assert.Equal("Coding task", entry.FocusReason);
    }
}
