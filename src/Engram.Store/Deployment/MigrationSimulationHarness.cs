using System;
using System.IO;
using Engram.Store.Wiki;

namespace Engram.Store.Deployment;

public class MigrationSimulationHarness
{
    private readonly WorkspacePaths _paths;

    public MigrationSimulationHarness(WorkspacePaths paths)
    {
        _paths = paths;
    }

    public void InjectCorruptNodeJson(string nodeId)
    {
        var nodeFilePath = Path.Combine(_paths.Wiki, $"{nodeId}.md");
        Directory.CreateDirectory(_paths.Wiki);

        // Write an incomplete and malformed JSON frontmatter (omitting title)
        File.WriteAllText(nodeFilePath, @"---
node_id: " + nodeId + @"
confidence: 0.9
invalid_json_formatting: [
---
# Main Content
Malformed JSON in the frontmatter blocks parser.
");
    }

    public void InjectLegacyNodeFormat(string nodeId, string title)
    {
        var nodeFilePath = Path.Combine(_paths.Wiki, $"{nodeId}.md");
        Directory.CreateDirectory(_paths.Wiki);

        // Write a format with old keys (e.g. legacy_salience)
        File.WriteAllText(nodeFilePath, $@"---
node_id: {nodeId}
title: {title}
legacy_salience: 9.9
old_claims_structure:
  - claim_id: legacy_1
    prop: status
    val: active
---
# Legacy Node
This uses attributes from version 0.1 schema.
");
    }

    public void SimulateInterruptedWrite(string nodeId)
    {
        var nodeFilePath = Path.Combine(_paths.Wiki, $"{nodeId}.md");
        Directory.CreateDirectory(_paths.Wiki);

        // Write a file that is truncated mid-write
        File.WriteAllText(nodeFilePath, @"---
node_id: " + nodeId + @"
title: Half Written File");
    }
}
