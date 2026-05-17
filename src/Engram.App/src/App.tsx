import { useState, useCallback, useEffect } from "react";
import { Sidebar, type ChatSession } from "./components/sidebar/Sidebar";
import { ChatPanel } from "./components/chat/ChatPanel";
import { Titlebar } from "./components/layout/Titlebar";
import { api, checkApiHealth } from "./lib/api";
import { DiscoveryInterview } from "./components/discovery/DiscoveryInterview";
import type { SearchResult, WikiNodeSummary, RawEvent, StatusResponse, IdentityResponse, DriftAlert } from "./lib/api";

export type View = "chat" | "search" | "wiki" | "timeline" | "settings";

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
  const [apiOnline, setApiOnline] = useState<boolean | null>(null);
  const [discoveryDone, setDiscoveryDone] = useState<boolean | null>(null);

  useEffect(() => {
    checkApiHealth().then((online) => {
      setApiOnline(online);
      if (online) api.discoveryStatus().then(d => setDiscoveryDone(d.complete)).catch(() => setDiscoveryDone(false));
    });
    const interval = setInterval(() => checkApiHealth().then(setApiOnline), 15000);
    return () => clearInterval(interval);
  }, []);

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

// ─── Settings View ───
function SettingsView({ onRedoDiscovery }: { onRedoDiscovery?: () => void }) {
  const [powerMode, setPowerMode] = useState<"eco" | "turbo">("eco");
  const [status, setStatus] = useState<StatusResponse | null>(null);
  const [identity, setIdentity] = useState<IdentityResponse | null>(null);
  const [alerts, setAlerts] = useState<DriftAlert[]>([]);

  useEffect(() => {
    api.status().then(setStatus).catch(() => {});
    api.identity().then(setIdentity).catch(() => {});
    api.drift().then(d => setAlerts(d.alerts)).catch(() => {});
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
              {identity.comfortTriggers && identity.comfortTriggers.length > 0 && (
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
                onClick={() => setPowerMode(mode.id)}
                className={`flex-1 rounded-xl border p-4 text-left transition-colors ${
                  powerMode === mode.id ? "border-emerald-600/50 bg-emerald-900/20" : "border-white/[0.06] bg-[#2f2f2f]/50 hover:border-white/[0.12]"
                }`}
              >
                <div className="text-[13px] font-medium">{mode.label}</div>
                <div className="mt-1 text-[11px] text-[#888]">{mode.desc}</div>
              </button>
            ))}
          </div>
        </div>

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
