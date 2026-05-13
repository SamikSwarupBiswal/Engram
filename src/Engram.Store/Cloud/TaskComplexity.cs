namespace Engram.Store.Cloud;

/// <summary>
/// Task complexity level used by ModelRouter to select compute tier.
/// Low: routine ingestion, summarization — local SLM
/// Medium: search queries, briefs — Gemini 3 Flash (cheap cloud)
/// High: complex research, conflict analysis — Claude 4.5 Sonnet (expensive)
/// </summary>
public enum TaskComplexity
{
    Low = 0,
    Medium = 1,
    High = 2
}
