using System.Text;
using System.Text.RegularExpressions;

namespace Engram.Store.Wiki;

public class WikiNodeSerializer
{
    public string Serialize(WikiNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("node_id: " + Safe(node.NodeId));
        sb.AppendLine("title: " + Safe(node.Title));
        sb.AppendLine("node_type: " + node.NodeType);
        sb.AppendLine("summary: " + Safe(node.Summary));
        sb.AppendLine("salience: " + node.Salience.ToString("F2"));
        sb.AppendLine("confidence: " + node.Confidence.ToString("F2"));
        sb.AppendLine("last_touched_at: " + node.LastTouchedAt.ToString("O"));
        sb.AppendLine("created_at: " + node.CreatedAt.ToString("O"));
        sb.AppendLine("version: " + node.Version);
        if (node.Links.Count > 0)
            sb.AppendLine("links: [" + string.Join(", ", node.Links) + "]");
        if (node.Edges.Count > 0)
            sb.AppendLine("edges: " + System.Text.Json.JsonSerializer.Serialize(node.Edges));
        if (node.Claims.Count > 0)
            sb.AppendLine("claims: " + System.Text.Json.JsonSerializer.Serialize(node.Claims));
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# " + node.Title);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(node.Summary))
        {
            sb.AppendLine("> " + node.Summary);
            sb.AppendLine();
        }
        if (node.Facts.Count > 0)
        {
            sb.AppendLine("## Facts");
            sb.AppendLine();
            foreach (var fact in node.Facts)
            {
                var srcLinks = string.Join(" ", fact.Sources.Select(s =>
                    "[source:" + s.EventId + "|" + s.Source + "](source:" + s.EventId + " \"" + s.CapturedAt.ToString("yyyy-MM-dd HH:mm") + "\")"));
                sb.AppendLine("- " + fact.Text + " " + srcLinks);
            }
            sb.AppendLine();
        }
        if (node.OpenQuestions.Count > 0)
        {
            sb.AppendLine("## Open Questions");
            sb.AppendLine();
            foreach (var q in node.OpenQuestions)
                sb.AppendLine("- [ ] " + q);
            sb.AppendLine();
        }
        if (node.Links.Count > 0)
        {
            sb.AppendLine("## Related");
            sb.AppendLine();
            foreach (var link in node.Links)
                sb.AppendLine("- [[" + link + "]]");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public WikiNode? Deserialize(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var (fm, body) = SplitFrontMatter(markdown);
        if (fm == null) return null;
        var node = ParseFrontMatter(fm);
        if (node == null) return null;
        ParseBody(body, node);
        return node;
    }

    private static (string?, string) SplitFrontMatter(string md)
    {
        var lines = md.Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---") return (null, md);
        int end = -1;
        for (int i = 1; i < lines.Length; i++)
            if (lines[i].Trim() == "---") { end = i; break; }
        if (end < 0) return (null, md);
        return (string.Join("\n", lines[1..end]), string.Join("\n", lines[(end + 1)..]));
    }

    private static WikiNode? ParseFrontMatter(string yaml)
    {
        var node = new WikiNode();
        foreach (var line in yaml.Split('\n'))
        {
            var t = line.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            int ci = t.IndexOf(':');
            if (ci < 0) continue;
            var key = t[..ci].Trim();
            var val = t[(ci + 1)..].Trim().Trim('"');
            switch (key)
            {
                case "node_id": node.NodeId = val; break;
                case "title": node.Title = val; break;
                case "node_type": if (Enum.TryParse<WikiNodeType>(val, true, out var tp)) node.NodeType = tp; break;
                case "summary": node.Summary = val; break;
                case "salience": if (double.TryParse(val, out var s)) node.Salience = s; break;
                case "confidence": if (double.TryParse(val, out var c)) node.Confidence = c; break;
                case "last_touched_at": if (DateTimeOffset.TryParse(val, out var l)) node.LastTouchedAt = l; break;
                case "created_at": if (DateTimeOffset.TryParse(val, out var a)) node.CreatedAt = a; break;
                case "version": if (int.TryParse(val, out var v)) node.Version = v; break;
                case "links": node.Links = val.Trim('[', ']').Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); break;
                case "edges":
                    try {
                        node.Edges = System.Text.Json.JsonSerializer.Deserialize<List<WikiEdge>>(val) ?? new();
                    } catch { }
                    break;
                case "claims":
                    try {
                        node.Claims = System.Text.Json.JsonSerializer.Deserialize<List<SemanticClaim>>(val) ?? new();
                    } catch { }
                    break;
            }
        }
        if (string.IsNullOrEmpty(node.NodeId) || string.IsNullOrEmpty(node.Title)) return null;
        return node;
    }

    private static void ParseBody(string body, WikiNode node)
    {
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("- ")) continue;
            var content = line[2..]; // strip "- "

            // Check for open questions: "- [ ] question"
            if (content.StartsWith("[ ] "))
            {
                node.OpenQuestions.Add(content[4..]);
                continue;
            }

            // Skip related links: "- [[link]]"
            if (content.StartsWith("[[")) continue;

            // Extract source references: [source:ID](source:ID "YYYY-MM-DD HH:MM")
            var sources = new List<WikiSourceReference>();
            var text = content;
            var srcTag = "[source:";
            var idx = text.IndexOf(srcTag);
            while (idx >= 0)
            {
                var closeBracket = text.IndexOf(']', idx);
                if (closeBracket < 0) break;
                var eventId = text[(idx + srcTag.Length)..closeBracket];

                // Find the date in the URL part
                var openParen = text.IndexOf('(', closeBracket);
                var closeQuote = text.IndexOf('"', openParen + 1);
                var endQuote = text.IndexOf('"', closeQuote + 1);
                var dateStr = text[(closeQuote + 1)..endQuote];

                // Extract source name if present (format: event_id|source_name)
                var pipeIdx = eventId.IndexOf('|');
                var sourceName = "";
                if (pipeIdx >= 0)
                {
                    sourceName = eventId[(pipeIdx + 1)..];
                    eventId = eventId[..pipeIdx];
                }

                sources.Add(new WikiSourceReference
                {
                    EventId = eventId,
                    Source = sourceName,
                    CapturedAt = DateTimeOffset.TryParse(dateStr, out var dt) ? dt : default
                });

                // Remove this source link from text
                var closeParen = text.IndexOf(')', endQuote);
                if (closeParen < 0) break;
                text = text[..idx].Trim() + " " + text[(closeParen + 1)..].Trim();
                idx = text.IndexOf(srcTag);
            }

            text = text.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                node.Facts.Add(new WikiFact { Text = text, Sources = sources });
        }
    }

    private static string Safe(string v)
    {
        if (v.Contains(':') || v.Contains('#') || v.Contains('"'))
            return "\"" + v.Replace("\"", "'") + "\"";
        return v;
    }
}
