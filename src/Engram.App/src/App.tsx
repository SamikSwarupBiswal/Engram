import { useState, useCallback, useEffect } from "react";
import { Sidebar, type ChatSession } from "./components/sidebar/Sidebar";
import { ChatPanel } from "./components/chat/ChatPanel";
import { Titlebar } from "./components/layout/Titlebar";
import { api, checkApiHealth, type HealthResponse } from "./lib/api";
import { DiscoveryInterview } from "./components/discovery/DiscoveryInterview";
import { ModelDownloadBar } from "./components/chat/ModelDownloadBar";
import { GoogleWorkspacePanel } from "./components/settings/GoogleWorkspacePanel";
import { ResearchPanel } from "./components/research/ResearchPanel";
import { AutomationPanel } from "./components/automation/AutomationPanel";
import { GovernancePanel } from "./components/settings/GovernancePanel";
import type { SearchResult, WikiNodeSummary, RawEvent, StatusResponse, IdentityResponse, DriftAlert } from "./lib/api";

export type View = "chat" | "search" | "wiki" | "timeline" | "settings" | "archive" | "research" | "automation" | "governance";

const SESSIONS_KEY = "engram-chat-sessions";

function loadSessions(): ChatSession[] {
  try {
    const stored = localStorage.getItem(SESSIONS_KEY);
    return stored ? JSON.parse(stored) : [];
  } catch { return []; }
}

function saveSessions(sessions: ChatSession[]) {
  localStorage.setItem(SESSIONS_KEY, JSON.stringify(sessions.slice(0, 100)));
}

export default function App() {
  const [activeView, setActiveView] = useState<View>("chat");
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [sessions, setSessions] = useState<ChatSession[]>(loadSessions);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [discoveryDone, setDiscoveryDone] = useState<boolean | null>(null);

  // Derive state from health — single source of truth
  const apiOnline = health !== null;
  const modelReady = health?.isReady ?? false;

  useEffect(() => {
    // Unified health polling — replaces fragmented health/model checks
    const pollHealth = async () => {
      const h = await checkApiHealth();
      setHealth(h);
      if (h && discoveryDone === null) {
        api.discoveryStatus().then(d => setDiscoveryDone(d.complete)).catch(() => setDiscoveryDone(false));
      }
    };
    pollHealth();
    const interval = setInterval(pollHealth, 3000); // Faster poll during startup
    return () => clearInterval(interval);
  }, [discoveryDone]);

  const handleNewChat = useCallback(() => {
    const newSession: ChatSession = {
      id: Date.now().toString(),
      title: "New chat",
      lastMessage: "",
      timestamp: new Date().toISOString(),
    };
    const updated = [newSession, ...sessions];
    setSessions(updated);
    saveSessions(updated);
    setActiveSessionId(newSession.id);
    setActiveView("chat");
  }, [sessions]);

  const handleSelectSession = useCallback((id: string) => {
    setActiveSessionId(id);
    setActiveView("chat");
  }, []);

  const handleDeleteSession = useCallback((id: string) => {
    const updated = sessions.filter(s => s.id !== id);
    setSessions(updated);
    saveSessions(updated);
    if (activeSessionId === id) setActiveSessionId(null);
  }, [sessions, activeSessionId]);

  const handleUpdateSession = useCallback((id: string, title: string) => {
    const updated = sessions.map(s =>
      s.id === id ? { ...s, title, timestamp: new Date().toISOString() } : s
    );
    setSessions(updated);
    saveSessions(updated);
  }, [sessions]);

  return (
    <div className="flex h-screen w-screen flex-col overflow-hidden bg-[#212121] text-[#ececec]">
      <Titlebar apiOnline={apiOnline} />
      <div className="flex flex-1 overflow-hidden">
        <Sidebar
          activeView={activeView}
          onViewChange={setActiveView}
          collapsed={sidebarCollapsed}
          onToggleCollapse={() => setSidebarCollapsed(!sidebarCollapsed)}
          sessions={sessions}
          activeSessionId={activeSessionId}
          onNewChat={handleNewChat}
          onSelectSession={handleSelectSession}
          onDeleteSession={handleDeleteSession}
        />
        <main className="flex-1 overflow-hidden">
          {apiOnline && !modelReady && (
            <ModelDownloadBar
              visible={discoveryDone === true && activeView === "chat"}
              onComplete={() => {/* modelReady is derived from health, no manual state needed */}}
              health={health}
            />
          )}
          {discoveryDone === false && (
            <DiscoveryInterview
              onComplete={() => setDiscoveryDone(true)}
              onSkip={() => setDiscoveryDone(true)}
            />
          )}
          {discoveryDone && activeView === "chat" && (
            <ChatPanel
              sessionId={activeSessionId}
              onFirstMessage={(title) => {
                if (activeSessionId) handleUpdateSession(activeSessionId, title);
              }}
            />
          )}
          {activeView === "search" && <SearchView />}
          {activeView === "wiki" && <WikiView />}
          {activeView === "timeline" && <TimelineView />}
          {discoveryDone && activeView === "settings" && <SettingsView onRedoDiscovery={() => setDiscoveryDone(false)} />}
          {discoveryDone && activeView === "archive" && <ArchiveView />}
          {discoveryDone && activeView === "research" && <ResearchPanel />}
          {discoveryDone && activeView === "automation" && <AutomationPanel />}
          {discoveryDone && activeView === "governance" && <GovernancePanel />}
        </main>
      </div>
    </div>
  );
}

// ─── Search View ───
function SearchView() {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searched, setSearched] = useState(false);

  const handleSearch = async () => {
    if (!query.trim()) return;
    setLoading(true);
    setError(null);
    setSearched(true);
    try {
      const data = await api.search(query);
      setResults(data.results);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Search failed");
      setResults([]);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex h-full flex-col p-6">
      <h2 className="mb-4 text-base font-medium">Search Memory</h2>
      <div className="mb-6 flex gap-2">
        <input
          className="flex-1 rounded-xl border border-white/[0.08] bg-[#2f2f2f] px-4 py-2.5 text-sm text-[#ececec] placeholder:text-[#888] focus:border-white/[0.2] focus:outline-none"
          placeholder="Search your wiki memory..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && handleSearch()}
          autoFocus
        />
        <button onClick={handleSearch} disabled={loading} className="rounded-xl bg-white/[0.1] px-4 py-2.5 text-sm hover:bg-white/[0.15] disabled:opacity-50">
          {loading ? "..." : "Search"}
        </button>
      </div>
      {error && <div className="mb-4 rounded-xl bg-red-900/20 border border-red-900/30 px-4 py-3 text-sm text-red-400">{error}</div>}
      {searched && !loading && results.length === 0 && !error && (
        <div className="py-8 text-center text-sm text-[#888]">No results found for "{query}"</div>
      )}
      <div className="flex-1 space-y-2 overflow-y-auto">
        {results.map((r, i) => (
          <div key={i} className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4 hover:border-white/[0.12]">
            <div className="flex items-center justify-between">
              <span className="text-sm font-medium">{r.title}</span>
              <span className="text-[11px] text-[#888]">Score: {r.score.toFixed(2)}</span>
            </div>
            <p className="mt-1 text-[13px] text-[#b4b4b4]">{r.snippet}</p>
            <div className="mt-2 flex gap-2">
              <span className="rounded-full bg-white/[0.06] px-2 py-0.5 text-[10px] text-[#888]">{r.nodeType}</span>
              {r.matchedFields?.map((f, j) => (
                <span key={j} className="rounded-full bg-white/[0.04] px-2 py-0.5 text-[10px] text-[#666]">{f}</span>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ─── Wiki View ───
function WikiView() {
  const [nodes, setNodes] = useState<WikiNodeSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<string | null>(null);

  const loadNodes = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.wiki();
      setNodes(data.nodes);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load wiki");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { loadNodes(); }, []);

  const nodeTypes = ["Person", "Project", "Goal", "Concept", "Document", "Decision"];
  const filtered = filter ? nodes.filter(n => n.nodeType === filter) : nodes;

  return (
    <div className="flex h-full flex-col p-6">
      <div className="flex items-center justify-between">
        <h2 className="text-base font-medium">Wiki Memory</h2>
        <button onClick={loadNodes} className="rounded-lg bg-white/[0.06] px-3 py-1.5 text-xs text-[#b4b4b4] hover:bg-white/[0.1]">Refresh</button>
      </div>
      <p className="mt-1 text-[13px] text-[#888]">{nodes.length} nodes in your knowledge graph</p>

      {error && <div className="mt-4 rounded-xl bg-red-900/20 border border-red-900/30 px-4 py-3 text-sm text-red-400">{error}</div>}

      {/* Filter chips */}
      <div className="mt-4 flex flex-wrap gap-2">
        <button onClick={() => setFilter(null)} className={`rounded-full px-3 py-1 text-xs ${!filter ? "bg-white/[0.15] text-[#ececec]" : "bg-white/[0.06] text-[#888] hover:bg-white/[0.1]"}`}>
          All ({nodes.length})
        </button>
        {nodeTypes.map(t => {
          const count = nodes.filter(n => n.nodeType === t).length;
          if (count === 0) return null;
          return (
            <button key={t} onClick={() => setFilter(filter === t ? null : t)} className={`rounded-full px-3 py-1 text-xs ${filter === t ? "bg-white/[0.15] text-[#ececec]" : "bg-white/[0.06] text-[#888] hover:bg-white/[0.1]"}`}>
              {t} ({count})
            </button>
          );
        })}
      </div>

      {loading ? (
        <div className="flex flex-1 items-center justify-center">
          <div className="animate-pulse text-sm text-[#888]">Loading wiki...</div>
        </div>
      ) : (
        <div className="mt-4 flex-1 overflow-y-auto">
          {filtered.length === 0 ? (
            <div className="py-12 text-center text-sm text-[#888]">
              {nodes.length === 0 ? "No wiki nodes yet. Start chatting to build your memory." : `No ${filter} nodes.`}
            </div>
          ) : (
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {filtered.map((node) => (
                <div key={node.nodeId} className="cursor-pointer rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4 hover:border-white/[0.12] transition-colors">
                  <div className="text-sm font-medium">{node.title}</div>
                  <div className="mt-1 flex items-center gap-2">
                    <span className="rounded-full bg-white/[0.06] px-2 py-0.5 text-[10px] text-[#888]">{node.nodeType}</span>
                    <span className="text-[10px] text-[#666]">Salience: {node.salience.toFixed(2)}</span>
                  </div>
                  <div className="mt-2 text-[11px] text-[#666]">
                    Last touched: {new Date(node.lastTouchedAt).toLocaleDateString()}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// ─── Timeline View ───
function TimelineView() {
  const [events, setEvents] = useState<RawEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const data = await api.events({ limit: 100 });
        setEvents(data.events);
      } catch (e) {
        setError(e instanceof Error ? e.message : "Failed to load events");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  return (
    <div className="flex h-full flex-col p-6">
      <h2 className="text-base font-medium">Timeline</h2>
      <p className="mt-1 text-[13px] text-[#888]">{events.length} events in your history</p>

      {error && <div className="mt-4 rounded-xl bg-red-900/20 border border-red-900/30 px-4 py-3 text-sm text-red-400">{error}</div>}

      <div className="mt-4 flex-1 overflow-y-auto">
        {loading ? (
          <div className="flex items-center justify-center py-12">
            <div className="animate-pulse text-sm text-[#888]">Loading events...</div>
          </div>
        ) : events.length === 0 ? (
          <div className="py-12 text-center text-sm text-[#888]">
            No events captured yet. Enable capture sources in Settings.
          </div>
        ) : (
          <div className="space-y-2">
            {events.map((e) => (
              <div key={e.eventId} className="flex items-start gap-3 rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <div className="mt-1 h-2 w-2 flex-shrink-0 rounded-full bg-emerald-500" />
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium">{e.eventType}</span>
                    <span className="rounded-full bg-white/[0.06] px-2 py-0.5 text-[10px] text-[#888]">{e.source}</span>
                  </div>
                  {e.textPreview && <p className="mt-1 truncate text-[13px] text-[#b4b4b4]">{e.textPreview}</p>}
                  <div className="mt-1 flex items-center gap-3 text-[11px] text-[#666]">
                    <span>{new Date(e.capturedAt).toLocaleString()}</span>
                    {e.activeWindow && <span>Window: {e.activeWindow}</span>}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Archive View ───
function ArchiveView() {
  const [archived, setArchived] = useState<{nodeId: string; title: string; nodeType: string; salience: number; lastTouchedAt: string}[]>([]);
  const [candidates, setCandidates] = useState<{nodeId: string; title: string; nodeType: string; salience: number; lastTouchedAt: string}[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([
      api.archiveList().then(d => setArchived(d.nodes)).catch(() => {}),
      api.archiveCandidates().then(d => setCandidates(d.nodes)).catch(() => {}),
    ]).finally(() => setLoading(false));
  }, []);

  const handleArchiveStale = async () => {
    const result = await api.archiveStale();
    if (result.archived > 0) {
      setCandidates([]);
      const refreshed = await api.archiveList();
      setArchived(refreshed.nodes);
    }
  };

  const handleRestore = async (nodeId: string) => {
    await api.restoreArchive(nodeId);
    setArchived(archived.filter(n => n.nodeId !== nodeId));
    const refreshed = await api.archiveCandidates();
    setCandidates(refreshed.nodes);
  };

  return (
    <div className="flex h-full flex-col p-6">
      <h2 className="text-base font-medium">Archive</h2>
      <p className="mt-1 text-[13px] text-[#888]">Stale knowledge moved from wiki. Nodes with low salience.</p>

      {loading ? (
        <div className="flex flex-1 items-center justify-center"><div className="animate-pulse text-sm text-[#888]">Loading...</div></div>
      ) : (
        <div className="mt-4 flex-1 overflow-y-auto space-y-6">
          {/* Candidates for archival */}
          {candidates.length > 0 && (
            <div>
              <div className="flex items-center justify-between mb-3">
                <h3 className="text-[13px] font-medium text-[#b4b4b4]">Candidates for Archival ({candidates.length})</h3>
                <button onClick={handleArchiveStale} className="rounded-lg bg-yellow-600/20 px-3 py-1.5 text-[12px] text-yellow-400 hover:bg-yellow-600/30">
                  Archive All Stale
                </button>
              </div>
              <div className="space-y-2">
                {candidates.map(n => (
                  <div key={n.nodeId} className="flex items-center justify-between rounded-xl border border-yellow-900/20 bg-yellow-900/5 p-3">
                    <div>
                      <div className="text-sm">{n.title}</div>
                      <div className="text-[11px] text-[#888]">{n.nodeType} · Salience: {n.salience.toFixed(2)}</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Archived nodes */}
          <div>
            <h3 className="text-[13px] font-medium text-[#b4b4b4] mb-3">Archived ({archived.length})</h3>
            {archived.length === 0 ? (
              <div className="py-8 text-center text-sm text-[#888]">No archived nodes.</div>
            ) : (
              <div className="space-y-2">
                {archived.map(n => (
                  <div key={n.nodeId} className="flex items-center justify-between rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-3">
                    <div>
                      <div className="text-sm">{n.title}</div>
                      <div className="text-[11px] text-[#888]">{n.nodeType} · Salience: {n.salience.toFixed(2)}</div>
                    </div>
                    <button onClick={() => handleRestore(n.nodeId)} className="rounded-lg bg-emerald-600/20 px-3 py-1 text-[11px] text-emerald-400 hover:bg-emerald-600/30">
                      Restore
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Settings View ───
function SettingsView({ onRedoDiscovery }: { onRedoDiscovery?: () => void }) {
  const [perceptionStatus, setPerceptionStatus] = useState<{ isRunning: boolean; framesProcessed: number; eventsGenerated: number; ocrAvailable: boolean } | null>(null);
  const [tokenPricing, setTokenPricing] = useState<{ rates: { provider: string; inputCost: string; outputCost: string; description: string }[] } | null>(null);
  const [tokenStatus, setTokenStatus] = useState<{ tier: string; monthlyAllowance: number; tokensRemaining: number; tokensUsedThisMonth: number; bonusTokens: number; usagePercent: number; daysRemaining: number; usageByProvider: Record<string, number>; history: { timestamp: string; provider: string; proTokensCost: number; balanceAfter: number }[] } | null>(null);
  const [powerMode, setPowerMode] = useState<"eco" | "turbo">("eco");
  const [status, setStatus] = useState<StatusResponse | null>(null);
  const [identity, setIdentity] = useState<IdentityResponse | null>(null);
  const [antiGoals, setAntiGoals] = useState<{ description: string; severity: string }[]>([]);
  const [priorities, setPriorities] = useState<{ description: string; category: string }[]>([]);
  const [alerts, setAlerts] = useState<DriftAlert[]>([]);
  const [driftStats, setDriftStats] = useState<{ total: number; pending: number; dismissed: number; accepted: number; converted: number } | null>(null);

  useEffect(() => {
    api.status().then(setStatus).catch(() => {});
    api.tokenStatus().then(setTokenStatus).catch(() => {});
    api.tokenPricing().then(setTokenPricing).catch(() => {});
    api.getPowerMode().then(r => setPowerMode(r.mode as "eco" | "turbo")).catch(() => {});
    api.perceptionStatus().then(setPerceptionStatus).catch(() => {});
    api.identity().then(setIdentity).catch(() => {});
    api.antiGoals().then(d => setAntiGoals((d.antiGoals || []) as { description: string; severity: string }[])).catch(() => {});
    api.priorities().then(d => setPriorities((d.priorities || []) as { description: string; category: string }[])).catch(() => {});
    api.drift().then(d => setAlerts(d.alerts)).catch(() => {});
    api.driftStats().then(setDriftStats).catch(() => {});
  }, []);

  return (
    <div className="flex h-full flex-col overflow-y-auto p-6">
      <h2 className="mb-6 text-base font-medium">Settings</h2>
      <div className="mx-auto w-full max-w-lg space-y-8">

        {/* Profile Section */}
        <div className="rounded-2xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5">
          <div className="flex items-center gap-4">
            <div className="flex h-14 w-14 items-center justify-center rounded-full bg-[#303030] text-lg font-medium">SB</div>
            <div>
              <div className="text-base font-medium">{identity?.name || "Samik Swarup Biswal"}</div>
              <div className="text-[13px] text-[#888]">samik.sb25@gmail.com</div>
              <div className="mt-1 flex items-center gap-2">
                <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-medium ${status?.tier === "Pro" ? "bg-blue-900/40 text-blue-400" : "bg-emerald-900/40 text-emerald-400"}`}>
                  {status?.tier || "Free"} Tier
                </span>
                <span className="text-[11px] text-[#888]">
                  {status?.tier === "Pro" ? "$20-30/mo" : "$0/mo — 100% local"}
                </span>
              </div>
            </div>
          </div>
          {status?.tier !== "Pro" && (
            <button className="mt-4 w-full rounded-xl border border-white/[0.08] bg-white/[0.04] py-2 text-[13px] text-[#b4b4b4] hover:bg-white/[0.08]">
              Upgrade to Pro — $20-30/mo
            </button>
          )}
        </div>

        {/* Identity / Goals */}
        <div className="rounded-2xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5">
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Identity</h3>
          {identity?.discovered ? (
            <div className="space-y-3">
              {identity.goals && identity.goals.length > 0 && (
                <div>
                  <div className="text-[11px] text-[#888] mb-1">Goals</div>
                  <div className="flex flex-wrap gap-1.5">
                    {identity.goals.map((g, i) => (
                      <span key={i} className="rounded-full bg-emerald-900/30 px-2.5 py-0.5 text-[12px] text-emerald-400">{g}</span>
                    ))}
                  </div>
                </div>
              )}
              {/* Anti-Goals */}
              {antiGoals.length > 0 && (
                <div className="mb-2">
                  <div className="text-[10px] text-[#888] mb-1">Anti-Goals</div>
                  <div className="flex flex-wrap gap-1">
                    {antiGoals.map((ag, i) => (
                      <span key={i} className="rounded-full bg-red-500/10 px-2 py-0.5 text-[10px] text-red-400">{ag.description} ({ag.severity})</span>
                    ))}
                  </div>
                </div>
              )}

              {/* Priorities */}
              {priorities.length > 0 && (
                <div className="mb-2">
                  <div className="text-[10px] text-[#888] mb-1">Priorities</div>
                  <div className="flex flex-wrap gap-1">
                    {priorities.map((p, i) => (
                      <span key={i} className="rounded-full bg-blue-500/10 px-2 py-0.5 text-[10px] text-blue-400">{p.description} ({p.category})</span>
                    ))}
                  </div>
                </div>
              )}

              {identity?.comfortTriggers && identity.comfortTriggers.length > 0 && (
                <div>
                  <div className="text-[11px] text-[#888] mb-1">Comfort Triggers</div>
                  <div className="flex flex-wrap gap-1.5">
                    {identity.comfortTriggers.map((t, i) => (
                      <span key={i} className="rounded-full bg-blue-900/30 px-2.5 py-0.5 text-[12px] text-blue-400">{t}</span>
                    ))}
                  </div>
                </div>
              )}
              {identity.recurringAnxieties && identity.recurringAnxieties.length > 0 && (
                <div>
                  <div className="text-[11px] text-[#888] mb-1">Recurring Anxieties</div>
                  <div className="flex flex-wrap gap-1.5">
                    {identity.recurringAnxieties.map((a, i) => (
                      <span key={i} className="rounded-full bg-yellow-900/30 px-2.5 py-0.5 text-[12px] text-yellow-400">{a}</span>
                    ))}
                  </div>
                </div>
              )}
              <button onClick={onRedoDiscovery} className="mt-2 text-[11px] text-[#888] hover:text-[#ececec]">Re-run Discovery Interview</button>
            </div>
          ) : (
            <div className="text-center py-4">
              <p className="text-[13px] text-[#888] mb-3">Discovery not completed yet.</p>
              <button
                onClick={onRedoDiscovery}
                className="rounded-xl bg-emerald-600 px-4 py-2 text-[13px] text-white hover:bg-emerald-700"
              >
                Start Discovery Interview
              </button>
            </div>
          )}
        </div>

        {/* Workspace Stats */}
        {status && (
          <div className="rounded-2xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5">
            <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Workspace</h3>
            <div className="grid grid-cols-2 gap-3">
              <div className="rounded-xl bg-white/[0.04] p-3 text-center">
                <div className="text-lg font-medium">{status.rawEvents}</div>
                <div className="text-[11px] text-[#888]">Raw Events</div>
              </div>
              <div className="rounded-xl bg-white/[0.04] p-3 text-center">
                <div className="text-lg font-medium">{status.wikiNodes}</div>
                <div className="text-[11px] text-[#888]">Wiki Nodes</div>
              </div>
            </div>
            <div className="mt-2 text-[11px] text-[#666] truncate">{status.workspace}</div>
          </div>
        )}

        {/* Power Mode */}
        <div>
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Power Mode</h3>
          <div className="flex gap-3">
            {[
              { id: "eco" as const, label: "Eco Mode", desc: "Local Phi-4 · Free · Offline" },
              { id: "turbo" as const, label: "Turbo Mode", desc: "Cloud API · Pro · Internet required" },
            ].map((mode) => (
              <button
                key={mode.id}
                onClick={() => {
                  setPowerMode(mode.id);
                  api.setPowerMode(mode.id).catch(() => {});
                }}
                className={`flex-1 rounded-xl border p-4 text-left transition-colors ${
                  powerMode === mode.id ? "border-emerald-600/50 bg-emerald-900/20" : "border-white/[0.06] bg-[#2f2f2f]/50 hover:border-white/[0.12]"
                }`}
              >
                <div className="text-[13px] font-medium">{mode.label}</div>
                <div className="mt-1 text-[11px] text-[#888]">{mode.desc}</div>
              </button>
            ))}
          </div>
          {powerMode === "eco" && (
            <div className="mt-3 rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
              <div className="flex items-center justify-between">
                <div>
                  <div className="text-[13px] font-medium">Local Model</div>
                  <div className="text-[11px] text-[#888]">Phi-4-mini GGUF Q4_K_M (~2.2GB)</div>
                </div>
                <button
                  onClick={() => api.loadModel().catch(() => {})}
                  className="rounded-lg bg-emerald-600 px-3 py-1.5 text-[12px] text-white hover:bg-emerald-700"
                >
                  Load Model
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Drift Stats */}
        {driftStats && (
          <div className="mb-3 grid grid-cols-4 gap-2">
            <div className="rounded-lg bg-white/[0.04] p-2 text-center"><div className="text-sm font-medium">{driftStats.total}</div><div className="text-[10px] text-[#888]">Total</div></div>
            <div className="rounded-lg bg-white/[0.04] p-2 text-center"><div className="text-sm font-medium text-yellow-400">{driftStats.pending}</div><div className="text-[10px] text-[#888]">Pending</div></div>
            <div className="rounded-lg bg-white/[0.04] p-2 text-center"><div className="text-sm font-medium text-emerald-400">{driftStats.accepted}</div><div className="text-[10px] text-[#888]">Accepted</div></div>
            <div className="rounded-lg bg-white/[0.04] p-2 text-center"><div className="text-sm font-medium text-[#888]">{driftStats.dismissed}</div><div className="text-[10px] text-[#888]">Dismissed</div></div>
          </div>
        )}

        {/* Drift Alerts */}
        {alerts.length > 0 && (
          <div>
            <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Drift Alerts ({alerts.length})</h3>
            <div className="space-y-2">
              {alerts.map((a) => (
                <div key={a.alertId} className="rounded-xl border border-yellow-900/30 bg-yellow-900/10 p-4">
                  <div className="flex items-center justify-between">
                    <span className="text-[13px] font-medium text-yellow-400">{a.severity}</span>
                    <span className="text-[11px] text-[#888]">{a.status}</span>
                  </div>
                  <p className="mt-1 text-[13px] text-[#b4b4b4]">{a.description}</p>
                  {a.status === "Pending" && (
                    <div className="mt-3 flex gap-2">
                      <button
                        onClick={async () => { await api.acceptDrift(a.alertId); setAlerts(alerts.filter(x => x.alertId !== a.alertId)); }}
                        className="rounded-lg bg-emerald-600/20 px-3 py-1 text-[11px] text-emerald-400 hover:bg-emerald-600/30"
                      >
                        Accept
                      </button>
                      <button
                        onClick={async () => { await api.dismissDrift(a.alertId); setAlerts(alerts.filter(x => x.alertId !== a.alertId)); }}
                        className="rounded-lg bg-zinc-600/20 px-3 py-1 text-[11px] text-zinc-400 hover:bg-zinc-600/30"
                      >
                        Dismiss
                      </button>
                      <button
                        onClick={async () => { await api.convertDrift(a.alertId); setAlerts(alerts.filter(x => x.alertId !== a.alertId)); }}
                        className="rounded-lg bg-blue-600/20 px-3 py-1 text-[11px] text-blue-400 hover:bg-blue-600/30"
                      >
                        Convert to Wiki
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Capture Sources */}
        <div>
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Capture Sources</h3>
          <div className="space-y-2">
            {[
              { label: "File Watcher", desc: "Monitor Downloads, Documents, Desktop" },
              { label: "Clipboard", desc: "Track clipboard changes" },
              { label: "Active Window", desc: "Detect focused application" },
            ].map((source) => (
              <label key={source.label} className="flex items-center justify-between rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4 hover:border-white/[0.12] cursor-pointer">
                <div>
                  <div className="text-[13px] font-medium">{source.label}</div>
                  <div className="text-[11px] text-[#888]">{source.desc}</div>
                </div>
                <input type="checkbox" className="h-4 w-4 rounded border-[#555] bg-[#2f2f2f]" />
              </label>
            ))}
          </div>
          <p className="mt-2 text-[11px] text-[#666]">All capture sources are off by default for privacy.</p>
        </div>

        {/* Token Budget Dashboard */}
        {tokenStatus && (
          <div>
            <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Token Budget</h3>
            <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5">
              {/* Usage bar */}
              <div className="mb-3">
                <div className="flex items-center justify-between mb-1">
                  <span className="text-[13px] font-medium">{tokenStatus.tier === "pro" ? "Pro" : "Free"} Tier</span>
                  <span className="text-[11px] text-[#888]">{tokenStatus.daysRemaining} days left</span>
                </div>
                <div className="h-2 w-full rounded-full bg-white/[0.06]">
                  <div
                    className={`h-2 rounded-full transition-all ${tokenStatus.usagePercent > 80 ? "bg-red-500" : tokenStatus.usagePercent > 50 ? "bg-yellow-500" : "bg-emerald-500"}`}
                    style={{ width: `${Math.min(100, tokenStatus.usagePercent)}%` }}
                  />
                </div>
                <div className="mt-1 flex justify-between text-[11px] text-[#888]">
                  <span>{tokenStatus.tokensUsedThisMonth.toLocaleString()} used</span>
                  <span>{tokenStatus.tokensRemaining.toLocaleString()} remaining</span>
                </div>
              </div>

              {/* Stats */}
              <div className="grid grid-cols-3 gap-3 mb-3">
                <div className="rounded-lg bg-white/[0.04] p-2 text-center">
                  <div className="text-sm font-medium">{tokenStatus.monthlyAllowance.toLocaleString()}</div>
                  <div className="text-[10px] text-[#888]">Monthly</div>
                </div>
                <div className="rounded-lg bg-white/[0.04] p-2 text-center">
                  <div className="text-sm font-medium">{tokenStatus.bonusTokens.toLocaleString()}</div>
                  <div className="text-[10px] text-[#888]">Bonus</div>
                </div>
                <div className="rounded-lg bg-white/[0.04] p-2 text-center">
                  <div className="text-sm font-medium">{Math.round(tokenStatus.usagePercent)}%</div>
                  <div className="text-[10px] text-[#888]">Used</div>
                </div>
              </div>

              {/* Provider breakdown */}
              {Object.keys(tokenStatus.usageByProvider).length > 0 && (
                <div className="mb-3">
                  <div className="text-[11px] text-[#888] mb-1">Usage by Provider</div>
                  {Object.entries(tokenStatus.usageByProvider).map(([provider, tokens]) => (
                    <div key={provider} className="flex justify-between text-[12px] py-0.5">
                      <span className="text-[#b4b4b4]">{provider}</span>
                      <span className="text-[#888]">{tokens.toLocaleString()} tokens</span>
                    </div>
                  ))}
                </div>
              )}

              {/* Actions */}
              <div className="flex gap-2">
                {tokenStatus.tier === "free" && (
                  <button
                    onClick={async () => { await api.setTier("pro"); const s = await api.tokenStatus(); setTokenStatus(s); }}
                    className="flex-1 rounded-lg bg-emerald-600 px-3 py-1.5 text-[12px] text-white hover:bg-emerald-700"
                  >
                    Upgrade to Pro
                  </button>
                )}
              {tokenPricing?.rates && (
                <div className="mb-2">
                  <div className="text-[10px] text-[#888] mb-1">Token Rates</div>
                  {tokenPricing.rates.map((r) => (
                    <div key={r.provider} className="flex justify-between text-[10px] py-0.5">
                      <span className="text-[#b4b4b4]">{r.provider}</span>
                      <span className="text-[#888]">{r.inputCost} in / {r.outputCost} out</span>
                    </div>
                  ))}
                </div>
              )}

              <button
                onClick={async () => { await api.buyTokenPack("small");const s = await api.tokenStatus(); setTokenStatus(s); }}
                  className="flex-1 rounded-lg border border-white/[0.08] bg-white/[0.04] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.08]"
                >
                  +100K tokens ($5)
                </button>
                <button
                  onClick={async () => { await api.buyTokenPack("large"); const s = await api.tokenStatus(); setTokenStatus(s); }}
                  className="flex-1 rounded-lg border border-white/[0.08] bg-white/[0.04] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.08]"
                >
                  +500K tokens ($20)
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Daily Brief */}
        <div>
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Daily Brief</h3>
          <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
            <p className="text-[11px] text-[#888] mb-2">Generate a morning or evening brief from your wiki.</p>
            <div className="flex gap-2">
              <button onClick={async () => { const b = await api.brief("morning"); alert(b.content); }} className="flex-1 rounded-lg border border-white/[0.08] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.04]">Morning Brief</button>
              <button onClick={async () => { const b = await api.brief("evening"); alert(b.content); }} className="flex-1 rounded-lg border border-white/[0.08] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.04]">Evening Brief</button>
            </div>
          </div>
        </div>

        {/* Visual Perception */}
        <div>
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Visual Perception</h3>
          <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
            <div className="flex items-center gap-2 mb-2">
              <div className={`h-2 w-2 rounded-full ${perceptionStatus?.isRunning ? "bg-emerald-500" : "bg-[#888]"}`} />
              <span className="text-[13px] text-[#ececec]">{perceptionStatus?.isRunning ? "Capturing" : "Idle"}</span>
              {perceptionStatus?.ocrAvailable && <span className="text-[10px] text-emerald-400 ml-auto">OCR Available</span>}
            </div>

            {perceptionStatus && (
              <div className="grid grid-cols-2 gap-2 mb-3">
                <div className="rounded-lg bg-white/[0.04] p-2 text-center">
                  <div className="text-sm font-medium">{perceptionStatus.framesProcessed}</div>
                  <div className="text-[10px] text-[#888]">Frames</div>
                </div>
                <div className="rounded-lg bg-white/[0.04] p-2 text-center">
                  <div className="text-sm font-medium">{perceptionStatus.eventsGenerated}</div>
                  <div className="text-[10px] text-[#888]">Events</div>
                </div>
              </div>
            )}

            <div className="flex gap-2 mb-2">
              {perceptionStatus?.isRunning ? (
                <button onClick={async () => { await api.perceptionStop(); setPerceptionStatus(await api.perceptionStatus()); }} className="flex-1 rounded-lg border border-red-500/30 px-3 py-1.5 text-[12px] text-red-400 hover:bg-red-500/10">Stop Capture</button>
              ) : (
                <button onClick={async () => { await api.perceptionStart(); setPerceptionStatus(await api.perceptionStatus()); }} className="flex-1 rounded-lg bg-emerald-600 px-3 py-1.5 text-[12px] text-white hover:bg-emerald-700">Start Capture</button>
              )}
              <button onClick={async () => {
                const r = await api.perceptionCapture();
                alert(`Window: ${r.frame.activeWindowTitle}\nProcess: ${r.frame.activeWindowProcess}\nChanges: ${r.frame.stateChanges.length}\nEvents: ${r.events.length}`);
              }} className="rounded-lg border border-white/[0.08] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.04]">Capture Now</button>
            </div>

            <p className="text-[10px] text-[#666]">Captures screen frames at 2s intervals. Detects window switches, app changes, and notifications.</p>
          </div>
        </div>

        {/* Layout Snap */}
        <div>
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Layout Snap</h3>
          <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
            <p className="text-[11px] text-[#888] mb-3">Snap windows for side-by-side viewing.</p>
            <div className="grid grid-cols-2 gap-2">
              <button onClick={() => api.layoutSnapLeft()} className="rounded-lg border border-white/[0.08] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.04]">← Snap Left</button>
              <button onClick={() => api.layoutSnapRight()} className="rounded-lg border border-white/[0.08] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.04]">Snap Right →</button>
              <button onClick={() => api.layoutSnapResearch()} className="rounded-lg border border-white/[0.08] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.04]">Research Layout</button>
              <button onClick={() => api.layoutMaximize()} className="rounded-lg border border-white/[0.08] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.04]">Maximize</button>
            </div>
          </div>
        </div>

        {/* Security */}
        <div>
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Security</h3>
          <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
            <div className="flex items-center gap-2 mb-2">
              <div className="h-2 w-2 rounded-full bg-emerald-500" />
              <span className="text-[13px] text-[#ececec]">AES-256-GCM Encryption</span>
              <button onClick={async () => { const s = await api.securityStatus(); alert(s.encryptionConfigured ? "Encryption configured" : "Not configured"); }} className="ml-auto text-[10px] text-[#888] hover:text-[#b4b4b4]">Check Status</button>
            </div>
            <p className="text-[11px] text-[#888] mb-3">All data encrypted at rest. Export your data anytime.</p>
            <div className="flex gap-2">
              <button onClick={async () => { const r = await api.securityExport(); alert(`Exported ${r.fileCount} files to ${r.outputPath}`); }} className="flex-1 rounded-lg border border-white/[0.08] px-3 py-1.5 text-[12px] text-[#b4b4b4] hover:bg-white/[0.04]">Export All Data</button>
              <button onClick={async () => { if (confirm("Delete ALL data permanently?")) { await api.securityDelete(); alert("Data deleted."); } }} className="rounded-lg border border-red-500/30 px-3 py-1.5 text-[12px] text-red-400 hover:bg-red-500/10">Delete All</button>
            </div>
          </div>
        </div>

        {/* Google Workspace */}
        <GoogleWorkspacePanel />

        {/* Provider Config */}
        <div>
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Cloud Provider</h3>
          <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
            <p className="text-[11px] text-[#888] mb-3">Connect any OpenAI-compatible API.</p>
            <div className="space-y-2">
              <input type="text" placeholder="Provider name (openai/groq/ollama)" className="w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-1.5 text-[12px] text-[#ececec] placeholder:text-[#666]" id="provider-name" />
              <input type="text" placeholder="Base URL (https://api.openai.com/v1)" className="w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-1.5 text-[12px] text-[#ececec] placeholder:text-[#666]" id="provider-url" />
              <input type="text" placeholder="Model (gpt-4o / llama-3.3-70b)" className="w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-1.5 text-[12px] text-[#ececec] placeholder:text-[#666]" id="provider-model" />
              <input type="password" placeholder="API Key (empty for local)" className="w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-1.5 text-[12px] text-[#ececec] placeholder:text-[#666]" id="provider-key" />
              <button onClick={async () => {
                const n = (document.getElementById('provider-name') as HTMLInputElement)?.value;
                const u = (document.getElementById('provider-url') as HTMLInputElement)?.value;
                const m = (document.getElementById('provider-model') as HTMLInputElement)?.value;
                const k = (document.getElementById('provider-key') as HTMLInputElement)?.value;
                await api.setProvider({ providerName: n, baseUrl: u, model: m, apiKey: k });
                alert("Provider saved!");
              }} className="w-full rounded-lg bg-emerald-600 px-4 py-2 text-[12px] text-white hover:bg-emerald-700">Save Provider</button>
            </div>
          </div>
        </div>

        {/* Custom Provider (Turbo Mode) */}
        {powerMode === "turbo" && (
          <div>
            <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Cloud Provider</h3>
            <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4 space-y-3">
              <p className="text-[12px] text-[#888]">Connect any OpenAI-compatible API (OpenAI, Groq, Together, Ollama, etc.)</p>
              <div>
                <label className="text-[11px] text-[#888]">Provider Name</label>
                <input
                  type="text"
                  placeholder="openai / groq / together / ollama"
                  className="mt-1 w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-sm text-[#ececec] placeholder:text-[#666] focus:outline-none"
                  id="provider-name"
                />
              </div>
              <div>
                <label className="text-[11px] text-[#888]">Base URL</label>
                <input
                  type="text"
                  placeholder="https://api.openai.com/v1"
                  className="mt-1 w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-sm text-[#ececec] placeholder:text-[#666] focus:outline-none"
                  id="provider-url"
                />
              </div>
              <div>
                <label className="text-[11px] text-[#888]">Model</label>
                <input
                  type="text"
                  placeholder="gpt-4o / llama-3.3-70b / mixtral-8x7b"
                  className="mt-1 w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-sm text-[#ececec] placeholder:text-[#666] focus:outline-none"
                  id="provider-model"
                />
              </div>
              <div>
                <label className="text-[11px] text-[#888]">API Key</label>
                <input
                  type="password"
                  placeholder="sk-... (leave empty for local APIs like Ollama)"
                  className="mt-1 w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-sm text-[#ececec] placeholder:text-[#666] focus:outline-none"
                  id="provider-key"
                />
              </div>
              <button
                onClick={async () => {
                  const name = (document.getElementById('provider-name') as HTMLInputElement)?.value;
                  const url = (document.getElementById('provider-url') as HTMLInputElement)?.value;
                  const model = (document.getElementById('provider-model') as HTMLInputElement)?.value;
                  const key = (document.getElementById('provider-key') as HTMLInputElement)?.value;
                  await fetch('http://127.0.0.1:5000/api/provider', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ providerName: name, baseUrl: url, model: model, apiKey: key }),
                  });
                }}
                className="w-full rounded-lg bg-emerald-600 px-4 py-2 text-sm text-white hover:bg-emerald-700"
              >
                Save Provider
              </button>
            </div>
          </div>
        )}

        {/* Runtime Diagnostics */}
        <div>
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Runtime Diagnostics</h3>
          <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
            <p className="text-[11px] text-[#888] mb-3">Export runtime diagnostics for support or validation. Includes lifecycle state, cleanup telemetry, backend verdicts, and recent logs.</p>
            <button onClick={async () => {
              try {
                const data = await api.diagnosticsExport();
                const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `engram-diagnostics-${new Date().toISOString().slice(0,19).replace(/:/g,'-')}.json`;
                a.click();
                URL.revokeObjectURL(url);
              } catch (e) {
                alert('Failed to export diagnostics: ' + (e instanceof Error ? e.message : 'unknown error'));
              }
            }} className="w-full rounded-lg bg-white/[0.06] px-3 py-2 text-[12px] text-[#b4b4b4] hover:bg-white/[0.1]">
              Export Runtime Diagnostics
            </button>
          </div>
        </div>

        {/* Data */}
        <div>
          <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Data</h3>
          <div className="flex gap-3">
            <button className="flex-1 rounded-xl border border-white/[0.08] bg-white/[0.04] py-2 text-[13px] text-[#b4b4b4] hover:bg-white/[0.08]">Export All Data</button>
            <button className="flex-1 rounded-xl border border-red-900/30 bg-red-900/10 py-2 text-[13px] text-red-400 hover:bg-red-900/20">Delete All Data</button>
          </div>
        </div>
      </div>
    </div>
  );
}
