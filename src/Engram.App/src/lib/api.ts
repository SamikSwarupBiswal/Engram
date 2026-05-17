const API_BASE = "http://127.0.0.1:5000";

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) throw new Error(`API ${res.status}: ${res.statusText}`);
  return res.json();
}

// ─── Types ───

export interface SearchResult {
  title: string;
  snippet: string;
  score: number;
  nodeId: string;
  nodeType: string;
  matchedFields: string[];
}

export interface SearchResponse {
  query: string;
  nodesSearched: number;
  duration: number;
  results: SearchResult[];
}

export interface WikiNodeSummary {
  nodeId: string;
  title: string;
  nodeType: string;
  salience: number;
  lastTouchedAt: string;
}

export interface WikiNode extends WikiNodeSummary {
  summary: string;
  facts: string[];
  openQuestions: string[];
  sourceEvents: string[];
  links: string[];
}

export interface BriefResult {
  type: string;
  content: string;
  generatedAt: string;
}

export interface RawEvent {
  eventId: string;
  eventType: string;
  capturedAt: string;
  source: string;
  activeWindow: string;
  textPreview: string;
}

export interface EventsResponse {
  count: number;
  events: RawEvent[];
}

export interface StatusResponse {
  workspace: string;
  tier: string;
  cloudEnabled: boolean;
  rawEvents: number;
  wikiNodes: number;
  isCapturing: boolean;
}

export interface IdentityResponse {
  discovered: boolean;
  name?: string;
  goals?: string[];
  comfortTriggers?: string[];
  recurringAnxieties?: string[];
  preferences?: string[];
}

export interface DriftAlert {
  alertId: string;
  description: string;
  severity: string;
  status: string;
  detectedAt: string;
}

export interface DriftResponse {
  count: number;
  alerts: DriftAlert[];
}

export interface WikiResponse {
  count: number;
  nodes: WikiNodeSummary[];
}

// ─── Types (extended) ───

export interface DiscoveryAnswers {
  displayName: string;
  goals: string[];
  comfortTriggers: string[];
  recurringAnxieties: string[];
  preferences: string[];
  priorities: { description: string; category: string }[];
  antiGoals: { description: string; severity: string; context?: string }[];
}

// ─── API Methods ───

export const api = {
  search: (query: string, limit = 20) =>
    apiFetch<SearchResponse>(`/api/search?q=${encodeURIComponent(query)}&limit=${limit}`),

  wiki: () => apiFetch<WikiResponse>("/api/wiki"),

  wikiNode: (nodeId: string) =>
    apiFetch<WikiNode>(`/api/wiki/${encodeURIComponent(nodeId)}`),

  brief: (time: "morning" | "evening" = "morning") =>
    apiFetch<BriefResult>(`/api/brief?time=${time}`),

  events: (params?: { source?: string; from?: string; to?: string; limit?: number }) => {
    const q = new URLSearchParams();
    if (params?.source) q.set("source", params.source);
    if (params?.from) q.set("from", params.from);
    if (params?.to) q.set("to", params.to);
    if (params?.limit) q.set("limit", String(params.limit));
    return apiFetch<EventsResponse>(`/api/events?${q.toString()}`);
  },

  status: () => apiFetch<StatusResponse>("/api/status"),

  identity: () => apiFetch<IdentityResponse>("/api/identity"),

  drift: () => apiFetch<DriftResponse>("/api/drift"),

  chat: (messages: { role: string; content: string }[]) =>
    apiFetch<{ choices: { message: { content: string }[] }[] }>("/v1/chat/completions", {
      method: "POST",
      body: JSON.stringify({ messages }),
    }),

  // Discovery
  discoveryStatus: () =>
    apiFetch<{ complete: boolean }>("/api/discovery/status"),

  runDiscovery: (answers: DiscoveryAnswers) =>
    apiFetch<{ complete: boolean; goals: number; priorities: number; antiGoals: number }>("/api/discovery", {
      method: "POST",
      body: JSON.stringify(answers),
    }),

  // Identity CRUD
  updateIdentity: (profile: { displayName: string; goals: string[]; comfortTriggers: string[]; recurringAnxieties: string[]; preferences: string[] }) =>
    apiFetch<{ saved: boolean }>("/api/identity", {
      method: "PUT",
      body: JSON.stringify(profile),
    }),

  antiGoals: () =>
    apiFetch<{ count: number; antiGoals: unknown[] }>("/api/identity/anti-goals"),

  priorities: () =>
    apiFetch<{ count: number; priorities: unknown[] }>("/api/identity/priorities"),

  // Intervention
  checkIntervention: (request: { action: string; context: string; category: string }) =>
    apiFetch<{ allowed: boolean; reason: string; confidence: number; severity: string | null }>("/api/intervention/check", {
      method: "POST",
      body: JSON.stringify(request),
    }),
};

// ─── Health Check ───

export async function checkApiHealth(): Promise<boolean> {
  try {
    const res = await fetch(`${API_BASE}/`, { signal: AbortSignal.timeout(3000) });
    return res.ok;
  } catch {
    return false;
  }
}
