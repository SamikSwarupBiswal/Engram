using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for wiki node serialization/deserialization.
/// Production requirement: round-trip fidelity, front matter parsing, source links.
/// </summary>
public class WikiNodeSerializerTests
{
    private readonly WikiNodeSerializer _serializer = new();

    [Fact]
    public void Serialize_ProducesValidMarkdown()
    {
        var node = CreateTestNode();
        var md = _serializer.Serialize(node);

        Assert.Contains("---", md);
        Assert.Contains("node_id:", md);
        Assert.Contains("# Test Project", md);
        Assert.Contains("## Facts", md);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = CreateTestNode();
        var md = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize(md);

        Assert.NotNull(deserialized);
        Assert.Equal(original.NodeId, deserialized!.NodeId);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.NodeType, deserialized.NodeType);
        Assert.Equal(original.Summary, deserialized.Summary);
        Assert.Equal(original.Salience, deserialized.Salience);
        Assert.Equal(original.Confidence, deserialized.Confidence);
        Assert.Equal(original.Version, deserialized.Version);
    }

    [Fact]
    public void RoundTrip_PreservesFacts()
    {
        var original = CreateTestNode();
        var md = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize(md);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.Facts.Count);
        Assert.Equal("First fact", deserialized.Facts[0].Text);
        Assert.Equal("Second fact", deserialized.Facts[1].Text);
    }

    [Fact]
    public void RoundTrip_PreservesSourceReferences()
    {
        var original = CreateTestNode();
        var md = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize(md);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized!.Facts[0].Sources);
        Assert.Equal("evt-001", deserialized.Facts[0].Sources[0].EventId);
    }

    [Fact]
    public void Serialize_IncludesSourceLinks()
    {
        var node = CreateTestNode();
        var md = _serializer.Serialize(node);

        Assert.Contains("[source:evt-001|", md);
    }

    [Fact]
    public void Serialize_IncludesOpenQuestions()
    {
        var node = CreateTestNode();
        var md = _serializer.Serialize(node);

        Assert.Contains("- [ ] Is this correct?", md);
    }

    [Fact]
    public void Serialize_IncludesRelatedLinks()
    {
        var node = CreateTestNode();
        var md = _serializer.Serialize(node);

        Assert.Contains("[[related_node]]", md);
    }

    [Fact]
    public void Deserialize_HandlesEmptyInput()
    {
        Assert.Null(_serializer.Deserialize(""));
        Assert.Null(_serializer.Deserialize(null!));
    }

    [Fact]
    public void Deserialize_HandlesMissingFrontMatter()
    {
        Assert.Null(_serializer.Deserialize("# Just a heading\nNo front matter"));
    }

    [Fact]
    public void Deserialize_HandlesInvalidYaml()
    {
        var md = "---\ninvalid: yaml: content: here\n---\n# Title";
        var node = _serializer.Deserialize(md);
        // Invalid YAML with no node_id/title returns null
        Assert.Null(node);
    }

    [Fact]
    public void Serialize_HandlesSpecialCharacters()
    {
        var node = new WikiNode
        {
            NodeId = "test_node",
            Title = "Title with: colons & special chars",
            NodeType = WikiNodeType.Concept,
            Summary = "Summary with \"quotes\" and #hash"
        };

        var md = _serializer.Serialize(node);
        var deserialized = _serializer.Deserialize(md);

        Assert.NotNull(deserialized);
        Assert.Equal("Title with: colons & special chars", deserialized!.Title);
    }

    [Fact]
    public void Serialize_AllNodeTypes_RoundTrip()
    {
        foreach (WikiNodeType type in Enum.GetValues<WikiNodeType>())
        {
            var node = new WikiNode
            {
                NodeId = $"test_{type.ToString().ToLower()}",
                Title = $"Test {type}",
                NodeType = type,
                Summary = $"A test {type} node"
            };

            var md = _serializer.Serialize(node);
            var deserialized = _serializer.Deserialize(md);

            Assert.NotNull(deserialized);
            Assert.Equal(type, deserialized!.NodeType);
        }
    }

    [Fact]
    public void Serialize_EmptyFacts_ProducesNoEmptySection()
    {
        var node = new WikiNode
        {
            NodeId = "empty",
            Title = "Empty Node",
            NodeType = WikiNodeType.Concept,
            Facts = new List<WikiFact>()
        };

        var md = _serializer.Serialize(node);
        Assert.DoesNotContain("## Facts", md);
    }

    private WikiNode CreateTestNode()
    {
        return new WikiNode
        {
            NodeId = "project_engram",
            Title = "Test Project",
            NodeType = WikiNodeType.Project,
            Summary = "A test project for unit testing",
            Salience = 0.85,
            Confidence = 0.95,
            Version = 1,
            Facts = new List<WikiFact>
            {
                new WikiFact
                {
                    Text = "First fact",
                    Sources = new List<WikiSourceReference>
                    {
                        new() { EventId = "evt-001", CapturedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero), Source = "file_watcher" }
                    }
                },
                new WikiFact
                {
                    Text = "Second fact",
                    Sources = new List<WikiSourceReference>
                    {
                        new() { EventId = "evt-002", CapturedAt = new DateTimeOffset(2026, 5, 13, 11, 0, 0, TimeSpan.Zero), Source = "clipboard" }
                    }
                }
            },
            OpenQuestions = new List<string> { "Is this correct?" },
            Links = new List<string> { "related_node" }
        };
    }
}
