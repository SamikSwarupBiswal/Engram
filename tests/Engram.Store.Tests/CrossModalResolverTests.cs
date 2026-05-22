using System;
using System.Collections.Generic;
using System.IO;
using Engram.Store.Wiki;
using Engram.Store.Reality;
using Xunit;

namespace Engram.Store.Tests;

public class CrossModalResolverTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly CrossModalResolver _resolver;

    public CrossModalResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_resolver_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _nodeStore = new WikiNodeStore(_paths);
        
        SeedNodes();
        
        _resolver = new CrossModalResolver(_nodeStore);
    }

    public void Dispose()
    {
        _nodeStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void SeedNodes()
    {
        // Node 1: Project Engram
        _nodeStore.Save(new WikiNode
        {
            NodeId = "proj_engram",
            Title = "Engram",
            NodeType = WikiNodeType.Project,
            Facts = new List<WikiFact>
            {
                new() { Text = "path: c:\\projects\\Engram" },
                new() { Text = "url: github.com/SamikSwarupBiswal/Engram" },
                new() { Text = "window: *Engram - Visual Studio Code*" },
                new() { Text = "alias: engram, semantic operating system" },
                new() { Text = "process: engram_daemon, engram_cli" }
            }
        });

        // Node 2: Document details
        _nodeStore.Save(new WikiNode
        {
            NodeId = "doc_design",
            Title = "Design Spec",
            NodeType = WikiNodeType.Document,
            Facts = new List<WikiFact>
            {
                new() { Text = "path: c:\\projects\\Engram\\docs\\design.md" },
                new() { Text = "alias: specification, spec doc" }
            }
        });
        
        // Node 3: Billing Concept
        _nodeStore.Save(new WikiNode
        {
            NodeId = "billing_concept",
            Title = "Billing",
            NodeType = WikiNodeType.Concept,
            Facts = new List<WikiFact>
            {
                new() { Text = "url: stripe.com/dashboard" },
                new() { Text = "process: stripe_cli" }
            }
        });
    }

    [Fact]
    public void ResolvePath_MatchesDirectFileAndSubdirectories()
    {
        // Precise file match
        Assert.Equal("doc_design", _resolver.ResolvePath("c:\\projects\\Engram\\docs\\design.md"));
        
        // Directory prefix match (longest match wins)
        Assert.Equal("proj_engram", _resolver.ResolvePath("c:\\projects\\Engram\\src\\Program.cs"));
        
        // Forward slash normalization
        Assert.Equal("proj_engram", _resolver.ResolvePath("c:/projects/Engram/src/Program.cs"));
    }

    [Fact]
    public void ResolveUrl_MatchesDomainAndPaths()
    {
        // Exact match with different schemes
        Assert.Equal("proj_engram", _resolver.ResolveUrl("https://github.com/SamikSwarupBiswal/Engram"));
        Assert.Equal("proj_engram", _resolver.ResolveUrl("http://www.github.com/SamikSwarupBiswal/Engram/"));
        
        // Contains match
        Assert.Equal("billing_concept", _resolver.ResolveUrl("https://stripe.com/dashboard/payments"));
    }

    [Fact]
    public void ResolveWindow_MatchesWildcardPattern()
    {
        Assert.Equal("proj_engram", _resolver.ResolveWindow("Program.cs - Engram - Visual Studio Code"));
        Assert.Null(_resolver.ResolveWindow("Some other Window Title"));
    }

    [Fact]
    public void ResolveProcess_MatchesExactOrCommaSeparatedList()
    {
        Assert.Equal("proj_engram", _resolver.ResolveProcess("engram_cli"));
        Assert.Equal("proj_engram", _resolver.ResolveProcess("engram_daemon"));
        Assert.Equal("billing_concept", _resolver.ResolveProcess("stripe_cli"));
        Assert.Null(_resolver.ResolveProcess("unknown_process"));
    }

    [Fact]
    public void ResolveAlias_MatchesVaryingSpecificity()
    {
        Assert.Equal("proj_engram", _resolver.ResolveAlias("engram"));
        Assert.Equal("proj_engram", _resolver.ResolveAlias("semantic operating system"));
        Assert.Equal("doc_design", _resolver.ResolveAlias("specification"));
    }

    [Fact]
    public void Resolve_GenericResolvesAcrossModalities()
    {
        // Path modality
        Assert.Equal("proj_engram", _resolver.Resolve("c:/projects/Engram/file.txt"));
        
        // URL modality
        Assert.Equal("billing_concept", _resolver.Resolve("https://stripe.com/dashboard"));
        
        // Process modality
        Assert.Equal("proj_engram", _resolver.Resolve("engram_daemon"));
        
        // Alias fallback
        Assert.Equal("doc_design", _resolver.Resolve("specification"));
    }
}
