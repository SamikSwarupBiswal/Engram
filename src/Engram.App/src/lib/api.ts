const API_BASE = "http://127.0.0.1:5000";

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) throw new Error(`API ${res.status}: ${res.statusText}`);
  return res.json();
}

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

export interface DiscoveryAnswers {
  displayName: string;
  goals: string[];
  comfortTriggers: string[];
  recurringAnxieties: string[];
  preferences: string[];
  priorities: { description: string; category: string }[];
  antiGoals: { description: string; severity: string; context?: string }[];
}

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

  // Drift actions
  acceptDrift: (alertId: string) =>
    apiFetch<{ status: string }>(`/api/drift/${alertId}/accept`, { method: "POST" }),

  dismissDrift: (alertId: string) =>
    apiFetch<{ status: string }>(`/api/drift/${alertId}/dismiss`, { method: "POST" }),

  convertDrift: (alertId: string) =>
    apiFetch<{ status: string }>(`/api/drift/${alertId}/convert`, { method: "POST" }),

  driftStats: () =>
    apiFetch<{ total: number; pending: number; dismissed: number; accepted: number; converted: number }>("/api/drift/stats"),

  // Salience
  salience: () =>
    apiFetch<{ count: number; nodes: { nodeId: string; title: string; nodeType: string; salience: number; shouldArchive: boolean; lastTouchedAt: string }[] }>("/api/salience"),

  // Archive
  archiveList: () =>
    apiFetch<{ count: number; nodes: { nodeId: string; title: string; nodeType: string; salience: number; lastTouchedAt: string }[] }>("/api/archive"),

  archiveStale: () =>
    apiFetch<{ archived: number; nodeIds: string[] }>("/api/archive/stale", { method: "POST" }),

  restoreArchive: (nodeId: string) =>
    apiFetch<{ restored: boolean }>(`/api/archive/${nodeId}/restore`, { method: "POST" }),

  archiveCandidates: () =>
    apiFetch<{ count: number; nodes: { nodeId: string; title: string; nodeType: string; salience: number; lastTouchedAt: string }[] }>("/api/archive/candidates"),

  // Model management
  modelStatus: () =>
    apiFetch<{ model: string; description: string; state: string; path: string; sizeBytes: number; progress: number; gpu: { backend: string; device: string; vramMb: number; layers: number }; isReady: boolean; isLoading: boolean; downloadInProgress: boolean; downloadError: string | null }>("/api/model/status"),

  downloadModel: () =>
    apiFetch<{ status: string }>("/api/model/download", { method: "POST" }),

  loadModel: () =>
    apiFetch<{ loaded: boolean; isReady: boolean; gpu: string }>("/api/model/load", { method: "POST" }),

  unloadModel: () =>
    apiFetch<{ unloaded: boolean }>("/api/model/unload", { method: "POST" }),

  // Power mode
  getPowerMode: () =>
    apiFetch<{ mode: string; localReady: boolean }>("/api/power-mode"),

  setPowerMode: (mode: "eco" | "turbo") =>
    apiFetch<{ mode: string }>("/api/power-mode", {
      method: "POST",
      body: JSON.stringify({ mode }),
    }),

  // Token budget
  tokenStatus: () =>
    apiFetch<{ tier: string; monthlyAllowance: number; tokensRemaining: number; tokensUsedThisMonth: number; bonusTokens: number; cycleStart: string; cycleEnd: string; usageByProvider: Record<string, number>; history: { timestamp: string; provider: string; inputTokens: number; outputTokens: number; proTokensCost: number; balanceAfter: number }[]; usagePercent: number; daysRemaining: number }>("/api/tokens"),

  checkTokens: (provider: string, inputTokens: number, outputTokens: number) =>
    apiFetch<{ allowed: boolean; cost: number; reason: string | null; remainingAfter: number }>("/api/tokens/check", {
      method: "POST",
      body: JSON.stringify({ provider, inputTokens, outputTokens }),
    }),

  buyTokenPack: (size: "small" | "large") =>
    apiFetch<{ added: number; remaining: number }>("/api/tokens/pack", {
      method: "POST",
      body: JSON.stringify({ size }),
    }),

  setTier: (tier: "free" | "pro") =>
    apiFetch<{ tier: string; monthlyAllowance: number; tokensRemaining: number }>("/api/tokens/tier", {
      method: "POST",
      body: JSON.stringify({ tier }),
    }),

  tokenPricing: () =>
    apiFetch<{ plans: { name: string; price: string; tokens: number; period: string }[]; packs: { name: string; tokens: number; price: string }[]; rates: { provider: string; inputCost: string; outputCost: string; description: string }[] }>("/api/tokens/pricing"),

  // Visual Perception
  perceptionStatus: () =>
    apiFetch<{ isRunning: boolean; framesProcessed: number; eventsGenerated: number; ocrAvailable: boolean }>("/api/perception/status"),

  perceptionStart: () =>
    apiFetch<{ started: boolean }>("/api/perception/start", { method: "POST" }),

  perceptionStop: () =>
    apiFetch<{ stopped: boolean }>("/api/perception/stop", { method: "POST" }),

  perceptionCapture: () =>
    apiFetch<{ frame: { activeWindowTitle: string; activeWindowProcess: string; width: number; height: number; success: boolean; extractedText: string | null; stateChanges: { type: string; description: string; oldValue: string | null; newValue: string | null }[] }; events: { timestamp: string; type: string; description: string; activeWindow: string }[] }>("/api/perception/capture", { method: "POST" }),

  // Layout Snap
  layoutSnapResearch: (browserProcess?: string, editorProcess?: string) =>
    apiFetch<{ snapped: boolean }>("/api/layout/snap-research", {
      method: "POST",
      body: JSON.stringify({ browserProcess, editorProcess }),
    }),

  layoutSnapLeft: () =>
    apiFetch<{ snapped: boolean }>("/api/layout/snap-left", { method: "POST" }),

  layoutSnapRight: () =>
    apiFetch<{ snapped: boolean }>("/api/layout/snap-right", { method: "POST" }),

  layoutMaximize: () =>
    apiFetch<{ maximized: boolean }>("/api/layout/maximize", { method: "POST" }),

  layoutRestore: () =>
    apiFetch<{ restored: boolean }>("/api/layout/restore", { method: "POST" }),

  // Security
  securityStatus: () =>
    apiFetch<{ encryptionConfigured: boolean }>("/api/security/status"),

  securitySetup: (password: string) =>
    apiFetch<{ success: boolean; salt: string }>("/api/security/setup", {
      method: "POST",
      body: JSON.stringify({ password }),
    }),

  securityUnlock: (password: string) =>
    apiFetch<{ unlocked: boolean }>("/api/security/unlock", {
      method: "POST",
      body: JSON.stringify({ password }),
    }),

  securityExport: () =>
    apiFetch<{ success: boolean; outputPath: string; fileCount: number; totalBytes: number }>("/api/security/export", { method: "POST" }),

  securityDelete: () =>
    apiFetch<{ success: boolean; fileCount: number; directoriesDeleted: string[] }>("/api/security/delete", { method: "POST" }),

  // Research Agent
  researchStart: (query: string) =>
    apiFetch<{ runId: string; query: string; status: string; steps: { stepId: string; type: string; description: string; status: string; output: string | null }[]; sources: { sourceId: string; url: string; title: string; domain: string; citationIndex: number }[]; summary: string | null; progress: number }>("/api/research/start", {
      method: "POST",
      body: JSON.stringify({ query }),
    }),

  researchList: () =>
    apiFetch<{ count: number; runs: { runId: string; query: string; status: string; progress: number; createdAt: string }[] }>("/api/research"),

  researchGet: (runId: string) =>
    apiFetch<{ runId: string; query: string; status: string; steps: { stepId: string; type: string; description: string; status: string; output: string | null }[]; sources: { sourceId: string; url: string; title: string; domain: string; citationIndex: number }[]; summary: string | null; progress: number; error: string | null }>(`/api/research/${runId}`),

  researchResume: (runId: string) =>
    apiFetch<{ runId: string; status: string }>(`/api/research/${runId}/resume`, { method: "POST" }),

  researchCancel: (runId: string) =>
    apiFetch<{ cancelled: boolean }>(`/api/research/${runId}/cancel`, { method: "POST" }),

  // Google Workspace
  gwsStatus: () =>
    apiFetch<{ isAuthenticated: boolean; email: string | null; scopes: string[]; expiresAt: string | null }>("/api/gws/status"),

  gwsSync: () =>
    apiFetch<{ success: boolean; email: string | null; emailCount: number; eventCount: number; fileCount: number; errors: string[] }>("/api/gws/sync", { method: "POST" }),

  gwsDisconnect: () =>
    apiFetch<{ disconnected: boolean }>("/api/gws/disconnect", { method: "POST" }),

  // Provider config
  getProvider: () =>
    apiFetch<{ hasCustomProvider: boolean; providerName: string; baseUrl: string; model: string; hasApiKey: boolean }>("/api/provider"),

  setProvider: (config: { apiKey?: string; baseUrl?: string; model?: string; providerName?: string }) =>
    apiFetch<{ saved: boolean }>("/api/provider", {
      method: "POST",
      body: JSON.stringify(config),
    }),

  // Diagnostics Export
  diagnosticsExport: () =>
    apiFetch<Record<string, unknown>>("/api/diagnostics/export"),
};

export interface StartupMetrics {
  backendDetectionMs: number | null;
  modelDownloadMs: number | null;
  modelLoadMs: number | null;
  totalStartupMs: number;
  readyAt: string | null;
  errorAt: string | null;
  degradationReason: string | null;
  degradationFrom: string;
}

export interface HealthResponse {
  state: string;           // Starting | DetectingBackend | BackendReady | DownloadingModel | LoadingModel | Ready | Error | Degraded | Offline
  backend: string | null;
  modelLoaded: boolean;
  modelName: string | null;
  progress: number;        // 0-100
  error: string | null;
  uptimeSeconds: number;
  retryCount: number;
  isReady: boolean;
  canAcceptRequests: boolean;
  stateHistory: string[];
  metadata: Record<string, string>;
  metrics: StartupMetrics;
}

export async function checkApiHealth(): Promise<HealthResponse | null> {
  try {
    const res = await fetch(`${API_BASE}/api/health`, { signal: AbortSignal.timeout(3000) });
    if (!res.ok) return null;
    return res.json();
  } catch {
    return null;
  }
}
