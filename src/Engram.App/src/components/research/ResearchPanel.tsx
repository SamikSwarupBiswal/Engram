import { useState, useEffect, useCallback } from "react";
import { api } from "../../lib/api";

type ResearchRun = {
  runId: string;
  query: string;
  status: string;
  steps: { stepId: string; type: string; description: string; status: string; output: string | null }[];
  sources: { sourceId: string; url: string; title: string; domain: string; citationIndex: number }[];
  summary: string | null;
  progress: number;
  error?: string | null;
  createdAt?: string;
};

export function ResearchPanel() {
  const [query, setQuery] = useState("");
  const [runs, setRuns] = useState<ResearchRun[]>([]);
  const [activeRun, setActiveRun] = useState<ResearchRun | null>(null);
  const [loading, setLoading] = useState(false);

  const loadRuns = useCallback(async () => {
    try {
      const result = await api.researchList();
      setRuns(result.runs as ResearchRun[]);
    } catch {}
  }, []);

  useEffect(() => { loadRuns(); }, [loadRuns]);

  const handleStart = async () => {
    if (!query.trim()) return;
    setLoading(true);
    try {
      const run = await api.researchStart(query.trim());
      setActiveRun(run);
      setQuery("");
      await loadRuns();
    } catch {}
    setLoading(false);
  };

  const handleSelect = async (runId: string) => {
    try {
      const run = await api.researchGet(runId);
      setActiveRun(run);
    } catch {}
  };

  const handleResume = async (runId: string) => {
    try {
      const run = await api.researchResume(runId);
      setActiveRun(run as ResearchRun);
      await loadRuns();
    } catch {}
  };

  const handleCancel = async (runId: string) => {
    try {
      await api.researchCancel(runId);
      if (activeRun?.runId === runId) {
        const run = await api.researchGet(runId);
        setActiveRun(run);
      }
      await loadRuns();
    } catch {}
  };

  const statusColor = (status: string) => {
    switch (status) {
      case "completed": return "text-emerald-400";
      case "running": return "text-blue-400";
      case "failed": return "text-red-400";
      case "paused": return "text-yellow-400";
      case "cancelled": return "text-[#888]";
      default: return "text-[#888]";
    }
  };

  const stepIcon = (type: string) => {
    switch (type) {
      case "search": return "🔍";
      case "scrape": return "📄";
      case "analyze": return "🔬";
      case "synthesize": return "📝";
      case "citeLink": return "🔗";
      default: return "•";
    }
  };

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="border-b border-white/[0.06] px-4 py-3">
        <h2 className="text-[14px] font-medium text-[#ececec]">Research</h2>
        <p className="text-[11px] text-[#888]">Multi-step web research with citations</p>
      </div>

      {/* Search input */}
      <div className="border-b border-white/[0.06] p-4">
        <div className="flex gap-2">
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleStart()}
            placeholder="Research topic..."
            className="flex-1 rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-sm text-[#ececec] placeholder:text-[#666] focus:outline-none"
          />
          <button
            onClick={handleStart}
            disabled={loading || !query.trim()}
            className="rounded-lg bg-emerald-600 px-4 py-2 text-[12px] text-white hover:bg-emerald-700 disabled:opacity-50"
          >
            {loading ? "..." : "Start"}
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto">
        {/* Active run */}
        {activeRun && (
          <div className="border-b border-white/[0.06] p-4">
            <div className="mb-2 flex items-center justify-between">
              <h3 className="text-[13px] font-medium text-[#ececec]">{activeRun.query}</h3>
              <span className={`text-[11px] ${statusColor(activeRun.status)}`}>{activeRun.status}</span>
            </div>

            {/* Progress bar */}
            {activeRun.status === "running" && (
              <div className="mb-3">
                <div className="h-1.5 w-full rounded-full bg-white/[0.06]">
                  <div className="h-1.5 rounded-full bg-emerald-500 transition-all" style={{ width: `${activeRun.progress}%` }} />
                </div>
              </div>
            )}

            {/* Steps */}
            <div className="mb-3 space-y-1">
              {activeRun.steps.map((step) => (
                <div key={step.stepId} className="flex items-center gap-2 text-[12px]">
                  <span>{stepIcon(step.type)}</span>
                  <span className={step.status === "completed" ? "text-emerald-400" : step.status === "running" ? "text-blue-400" : step.status === "failed" ? "text-red-400" : "text-[#888]"}>
                    {step.description}
                  </span>
                  {step.output && <span className="text-[10px] text-[#666]">— {step.output}</span>}
                </div>
              ))}
            </div>

            {/* Sources */}
            {activeRun.sources.length > 0 && (
              <div className="mb-3">
                <div className="text-[11px] text-[#888] mb-1">Sources ({activeRun.sources.length})</div>
                {activeRun.sources.map((s) => (
                  <div key={s.sourceId} className="text-[11px] py-0.5">
                    <span className="text-[#888]">[{s.citationIndex}]</span>{" "}
                    <a href={s.url} target="_blank" rel="noopener noreferrer" className="text-blue-400 hover:underline">{s.title || s.domain}</a>
                  </div>
                ))}
              </div>
            )}

            {/* Summary */}
            {activeRun.summary && (
              <div className="rounded-lg bg-white/[0.04] p-3">
                <div className="text-[11px] text-[#888] mb-1">Summary</div>
                <div className="text-[12px] text-[#b4b4b4] whitespace-pre-wrap">{activeRun.summary}</div>
              </div>
            )}

            {/* Actions */}
            <div className="mt-3 flex gap-2">
              {activeRun.status === "paused" && (
                <button onClick={() => handleResume(activeRun.runId)} className="rounded-lg bg-blue-600 px-3 py-1 text-[11px] text-white hover:bg-blue-700">Resume</button>
              )}
              {(activeRun.status === "running" || activeRun.status === "paused") && (
                <button onClick={() => handleCancel(activeRun.runId)} className="rounded-lg border border-red-500/30 px-3 py-1 text-[11px] text-red-400 hover:bg-red-500/10">Cancel</button>
              )}
              <button onClick={() => setActiveRun(null)} className="rounded-lg border border-white/[0.08] px-3 py-1 text-[11px] text-[#888] hover:bg-white/[0.06]">Close</button>
            </div>
          </div>
        )}

        {/* Run history */}
        {runs.length > 0 && (
          <div className="p-4">
            <div className="text-[11px] text-[#888] mb-2">Research History</div>
            {runs.map((run) => (
              <button
                key={run.runId}
                onClick={() => handleSelect(run.runId)}
                className="flex w-full items-center justify-between rounded-lg px-3 py-2 text-left hover:bg-white/[0.04]"
              >
                <div>
                  <div className="text-[12px] text-[#ececec]">{run.query}</div>
                  <div className="text-[10px] text-[#888]">{run.createdAt ? new Date(run.createdAt).toLocaleDateString() : ""}</div>
                </div>
                <span className={`text-[10px] ${statusColor(run.status)}`}>{run.status}</span>
              </button>
            ))}
          </div>
        )}

        {/* Empty state */}
        {runs.length === 0 && !activeRun && (
          <div className="p-4 text-center">
            <div className="text-[#888] text-[12px]">No research runs yet.</div>
            <div className="text-[#666] text-[11px] mt-1">Enter a topic above to start.</div>
          </div>
        )}
      </div>
    </div>
  );
}
