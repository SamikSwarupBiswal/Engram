const API_BASE = "http://127.0.0.1:5000";

export interface PlanningRejection {
  capabilityId: string;
  reasonCode: string;
  missingPrerequisites: string[];
  externalEffects: boolean;
}

export class ApiError extends Error {
  constructor(message: string, public rejection?: PlanningRejection, public status?: number) {
    super(message);
    this.name = "ApiError";
  }
}

export type AutomationTaskState = "Planned" | "AwaitingConfirmation" | "Running" | "Completed" | "Failed" | "Cancelled";

export interface AutomationTaskStatus {
  archived?: false;
  taskId: string;
  state: AutomationTaskState;
  pendingConfirmations: number;
  error?: string | null;
  memoryError?: string | null;
  plan?: AutomationTaskPlan;
}

export interface ArchivedAutomationTaskStatus {
  archived: true;
  taskId: string;
  state: AutomationTaskState;
  pendingConfirmations: 0;
  recordedAt: string;
  completedAt?: string | null;
  error?: string | null;
  fileChecksAt: string;
  artifacts: { fileId?: string | null; path: string; verifiedAt: string; currentState: "Unchanged" | "Changed" | "Missing" | "Unavailable" }[];
}

export type AutomationTaskSnapshot = AutomationTaskStatus | ArchivedAutomationTaskStatus;

export function isArchivedAutomationTaskStatus(value: unknown): value is ArchivedAutomationTaskStatus {
  if (!value || typeof value !== "object") return false;
  const task = value as Record<string, unknown>;
  const timestamp = (value: unknown) => typeof value === "string" && value.length <= 64 && Number.isFinite(Date.parse(value));
  return task.archived === true && typeof task.taskId === "string" && task.taskId.length > 0 && task.taskId.length <= 128 &&
    ["Planned", "AwaitingConfirmation", "Running", "Completed", "Failed", "Cancelled"].includes(task.state as string) &&
    task.pendingConfirmations === 0 && timestamp(task.recordedAt) && timestamp(task.fileChecksAt) &&
    (task.completedAt == null || timestamp(task.completedAt)) && (task.error == null || typeof task.error === "string") &&
    task.plan == null && Array.isArray(task.artifacts) && task.artifacts.length <= 5 && task.artifacts.every(artifact =>
      artifact && typeof artifact === "object" && (artifact.fileId == null || typeof artifact.fileId === "string") &&
      typeof artifact.path === "string" && artifact.path.length <= 32_768 && timestamp(artifact.verifiedAt) &&
      ["Unchanged", "Changed", "Missing", "Unavailable"].includes(artifact.currentState));
}

export interface AutomationTaskPlan {
  planId: string;
  goal: string;
  steps: Record<string, {
    id: string;
    dependsOn: string[];
    status?: string;
    action: {
      type: string;
      permission?: "Pending" | "Approved" | "Denied" | "AutoApproved";
      description: string;
      riskLevel: string;
      confirmationReason?: string | null;
      value?: string | null;
      target?: unknown;
      capabilityId?: string | null;
      capabilityArguments?: unknown;
      result?: string | null;
      error?: string | null;
    };
  }>;
  planning?: { model: string; provider: string; tool: string; fromCache: boolean } | null;
}

export function automationPlanReviewIdentity(task: AutomationTaskStatus): string {
  const plan = task.plan;
  if (!plan || plan.planId !== task.taskId || typeof plan.goal !== "string" || !plan.steps || typeof plan.steps !== "object" || Array.isArray(plan.steps))
    throw new Error("The task's reviewable plan is unavailable. Refresh the task before continuing.");
  const steps = Object.values(plan.steps);
  if (!steps.length || steps.length > 64 || steps.some(step => !step || typeof step.id !== "string" ||
      !Array.isArray(step.dependsOn) || step.dependsOn.some(id => typeof id !== "string") || !step.action ||
      typeof step.action.type !== "string" || typeof step.action.description !== "string" ||
      !["Low", "Medium", "High", "Critical"].includes(step.action.riskLevel) ||
      (step.action.confirmationReason != null && typeof step.action.confirmationReason !== "string") ||
      (step.status != null && typeof step.status !== "string") ||
      (step.action.result != null && typeof step.action.result !== "string") ||
      (step.action.error != null && typeof step.action.error !== "string")))
    throw new Error("The task's plan details could not be verified. Refresh the task before continuing.");
  if (plan.planning != null && (typeof plan.planning.model !== "string" || typeof plan.planning.provider !== "string" ||
      typeof plan.planning.tool !== "string" || typeof plan.planning.fromCache !== "boolean"))
    throw new Error("The task's planning record could not be verified. Refresh the task before continuing.");
  return JSON.stringify({ planId: plan.planId, goal: plan.goal, steps: steps.map(step => ({
    id: step.id, dependsOn: step.dependsOn, type: step.action.type, description: step.action.description,
    riskLevel: step.action.riskLevel, confirmationReason: step.action.confirmationReason,
    value: step.action.value, target: step.action.target, capabilityId: step.action.capabilityId,
    capabilityArguments: step.action.capabilityArguments,
  })), planning: plan.planning });
}

export function automationOrderedSteps(task: AutomationTaskStatus) {
  automationPlanReviewIdentity(task);
  const entries = Object.entries(task.plan!.steps);
  const byId = new Map(entries.map(([key, step]) => [key.toLowerCase(), step]));
  if (byId.size !== entries.length || entries.some(([key, step]) => key.toLowerCase() !== step.id.toLowerCase()))
    throw new Error("The task's step identities could not be verified.");
  const visited = new Set<string>();
  const visiting = new Set<string>();
  const ordered: AutomationTaskPlan["steps"][string][] = [];
  const visit = (id: string) => {
    const key = id.toLowerCase();
    if (visited.has(key)) return;
    const step = byId.get(key);
    if (!step || visiting.has(key)) throw new Error("The task's dependencies could not be verified.");
    visiting.add(key);
    step.dependsOn.forEach(visit);
    visiting.delete(key);
    visited.add(key);
    ordered.push(step);
  };
  entries.forEach(([id]) => visit(id));
  return ordered;
}

export function automationApprovalBoundaryIdentity(task: AutomationTaskStatus): string {
  const steps = automationOrderedSteps(task);
  if (steps.some(step => !["Pending", "Executing", "Completed", "Failed", "Skipped", "RolledBack"].includes(step.status ?? "") ||
      !["Pending", "Approved", "Denied", "AutoApproved"].includes(step.action.permission ?? "")) ||
      task.pendingConfirmations !== steps.filter(step => step.action.permission === "Pending").length)
    throw new Error("The task's approval boundary could not be verified. Refresh its status.");
  return JSON.stringify({ plan: automationPlanReviewIdentity(task), state: task.state,
    pending: task.pendingConfirmations, steps: steps.map(step => ({ id: step.id, status: step.status, permission: step.action.permission })) });
}

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    const payload = await res.json().catch(() => null) as { error?: string; message?: string; rejection?: PlanningRejection } | null;
    throw new ApiError(payload?.error || payload?.message || `API ${res.status}: ${res.statusText}`, payload?.rejection, res.status);
  }
  return res.json();
}

function fetchAutomationTask(taskId: string, signal?: AbortSignal): Promise<AutomationTaskStatus>;
function fetchAutomationTask(taskId: string, signal: AbortSignal | undefined, conversationId: string): Promise<AutomationTaskSnapshot>;
async function fetchAutomationTask(taskId: string, signal?: AbortSignal, conversationId?: string): Promise<AutomationTaskSnapshot> {
  const query = conversationId == null ? "" : `?conversationId=${encodeURIComponent(conversationId)}`;
  const task = await apiFetch<AutomationTaskSnapshot>(`/api/automation/tasks/${encodeURIComponent(taskId)}${query}`, {
    signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(15_000)]) : AbortSignal.timeout(15_000),
  });
  if (!task || task.taskId !== taskId || !["Planned", "AwaitingConfirmation", "Running", "Completed", "Failed", "Cancelled"].includes(task.state) ||
      (task.archived !== undefined && task.archived !== false && (conversationId == null || !isArchivedAutomationTaskStatus(task))))
    throw new Error("The server returned an invalid task status.");
  return task;
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

export interface GovernanceActivity {
  entryId: string;
  timestamp: string;
  action: string;
  description: string;
  relatedNodeId: string;
  impactLevel: string;
}

export interface ReasonTrace {
  traceId: string;
  timestamp: string;
  triggerType: number; // 0=Intervention, 1=SalienceShift, 2=Pause, 3=Escalation, 4=ExecutionDecision
  targetEntityId: string;
  description: string;
  causalFactors: string[];
  systemComponent: string;
}

export interface TrustScore {
  domain: string;
  score: number;
  lastUpdatedAt: string;
  successStreak: number;
  overrideCount: number;
}

export interface TrustStatus {
  scores: Record<string, TrustScore>;
  grants: Record<string, string>;
  autonomyCeiling: number;
  interventionFrequencyMultiplier: number;
  homeostasisIndex?: number;
  homeostasisState?: string;
  semanticState?: string;
  annoyanceScore?: number;
  consecutiveFriction?: number;
  availablePacingTokens?: number;
  cognitiveDebtCount?: number;
  floorDetected?: boolean;
}

export interface ConstitutionalViolation {
  severity: number; // 0=C1, 1=C2, 2=C3, 3=C4, 4=C5
  violatingSubsystem: string;
  details: string;
  triggerAction: string;
  causalChain: string[];
  userResolution: string;
}

export interface AuditEntry {
  entryId: string;
  timestamp: string;
  data: string; // Serialized ConstitutionalViolation
  previousHash: string;
  hash: string;
}

export interface AuditResponse {
  state: string;
  entries: AuditEntry[];
  integrityValid: boolean;
}

export interface PrivacyZoneRule {
  ruleName: string;
  excludedPathPattern: string;
  excludedAppProcess: string;
}

export interface SensitiveDomainRule {
  domainName: string;
  suppressInterventions: boolean;
  suppressPropagation: boolean;
}

export interface RetentionPolicyRule {
  domain: string;
  retentionWindow: string; // serialized TimeSpan
  autoExpire?: boolean;
}

export interface GovernanceConfig {
  retentionPolicies: RetentionPolicyRule[];
  sensitiveDomains: SensitiveDomainRule[];
  privacyZones: PrivacyZoneRule[];
  maxDailyInterventions: number;
  minConfidenceToEscalate: number;
  defaultTrustCeiling: number;
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

  automationTask: fetchAutomationTask,

  approveAutomationTask: (taskId: string, signal?: AbortSignal, expectedStepId?: string) =>
    apiFetch<AutomationTaskStatus>(`/api/automation/tasks/${encodeURIComponent(taskId)}/approve${expectedStepId ? `?expectedStepId=${encodeURIComponent(expectedStepId)}` : ""}`, {
      method: "POST", signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(30_000)]) : AbortSignal.timeout(30_000),
    }),

  executeAutomationTask: (taskId: string, signal?: AbortSignal) =>
    apiFetch<AutomationTaskStatus>(`/api/automation/tasks/${encodeURIComponent(taskId)}/execute`, {
      method: "POST", signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(30_000)]) : AbortSignal.timeout(30_000),
    }),

  cancelAutomationTask: (taskId: string, signal?: AbortSignal) =>
    apiFetch<AutomationTaskStatus>(`/api/automation/tasks/${encodeURIComponent(taskId)}/cancel`, {
      method: "POST", signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(30_000)]) : AbortSignal.timeout(30_000),
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

  // Governance & Trust
  governanceActivity: () =>
    apiFetch<GovernanceActivity[]>("/api/governance/activity"),

  governanceTraces: (entityId?: string) => {
    const path = entityId ? `/api/governance/traces?entityId=${encodeURIComponent(entityId)}` : "/api/governance/traces";
    return apiFetch<ReasonTrace[]>(path);
  },

  governanceTrust: () =>
    apiFetch<TrustStatus>("/api/governance/trust"),

  governanceForget: (nodeId: string) =>
    apiFetch<{ forgotten: boolean }>("/api/governance/forget", {
      method: "POST",
      body: JSON.stringify({ nodeId }),
    }),

  governanceDispute: (nodeId: string, claimId: string, correctedValue: string) =>
    apiFetch<{ disputed: boolean }>("/api/governance/dispute", {
      method: "POST",
      body: JSON.stringify({ nodeId, claimId, correctedValue }),
    }),

  governanceUpdateSettings: (config: any) =>
    apiFetch<{ updated: boolean }>("/api/governance/settings", {
      method: "POST",
      body: JSON.stringify(config),
    }),

  governanceRecover: (resolutionDetail: string) =>
    apiFetch<{ state: string }>("/api/governance/recover", {
      method: "POST",
      body: JSON.stringify({ resolutionDetail }),
    }),

  governanceAudit: () =>
    apiFetch<AuditResponse>("/api/governance/audit"),

  // D5 Deployment & Stability
  deploymentStatus: () =>
    apiFetch<{ systemVersion: string; schemaVersion: number; capabilityVersion: number; lastUpdatedAt: string }>("/api/deployment/status"),

  deploymentPreflight: (targetSystemVersion: string, targetSchemaVersion: number) =>
    apiFetch<{ success: boolean; message: string; targetSystemVersion: string; targetSchemaVersion: number; behavioralTrustDiff: Record<string, string> }>("/api/deployment/upgrade/preflight", {
      method: "POST",
      body: JSON.stringify({ targetSystemVersion, targetSchemaVersion }),
    }),

  deploymentUpgrade: (targetSystemVersion: string, targetSchemaVersion: number) =>
    apiFetch<{ success: boolean; message: string; confidence: string; errors: string[] }>("/api/deployment/upgrade/execute", {
      method: "POST",
      body: JSON.stringify({ targetSystemVersion, targetSchemaVersion }),
    }),

  deploymentRollback: (snapshotPath: string) =>
    apiFetch<{ success: boolean; message: string }>("/api/deployment/upgrade/rollback", {
      method: "POST",
      body: JSON.stringify({ snapshotPath }),
    }),

  governanceDegradations: () =>
    apiFetch<{ courtesyState: string; schedulingLatencyMs: number; isThermalThrottling: boolean; activeDegradations: Record<string, string>; explanation: string }>("/api/governance/degradations"),

  governanceLineage: (entityId: string) =>
    apiFetch<{ traceId: string; timestamp: string; component: string; triggerType: string; narrative: string; factors: string[] }[]>(`/api/governance/lineage?entityId=${encodeURIComponent(entityId)}`),

  diagnosticsSupportBundle: () =>
    apiFetch<{ success: boolean; bundlePath: string }>("/api/diagnostics/support-bundle", { method: "POST" }),
};

export async function runReviewedAutomationTask(
  reviewed: AutomationTaskStatus,
  confirm: boolean,
  signal: AbortSignal,
): Promise<AutomationTaskStatus> {
  const reviewedIdentity = automationPlanReviewIdentity(reviewed);
  const current = await api.automationTask(reviewed.taskId, signal);
  signal.throwIfAborted();
  if (automationPlanReviewIdentity(current) !== reviewedIdentity)
    throw new Error("The task plan changed. Review the refreshed plan before confirming it.");
  if (confirm) {
    if (automationApprovalBoundaryIdentity(current) !== automationApprovalBoundaryIdentity(reviewed))
      throw new Error("The approval boundary changed. Review the next action before confirming it.");
    if (current.state !== "AwaitingConfirmation" || current.pendingConfirmations <= 0)
      throw new Error("This task is no longer awaiting confirmation. Refresh its status before continuing.");
    const ordered = automationOrderedSteps(current);
    const next = ordered.find(step => step.status !== "Completed");
    if (!next || next.action.permission !== "Pending")
      throw new Error("The next action is not awaiting approval. Refresh the task.");
    const approved = await api.approveAutomationTask(current.taskId, signal, next.id);
    signal.throwIfAborted();
    if (approved.taskId !== current.taskId || approved.state !== "Planned" || approved.pendingConfirmations !== current.pendingConfirmations - 1 ||
        automationPlanReviewIdentity(approved) !== reviewedIdentity ||
        automationOrderedSteps(approved).some((step, index) => {
          const previous = ordered[index];
          return step.id !== previous.id || step.status !== previous.status ||
            step.action.permission !== (step.id === next.id ? "Approved" : previous.action.permission);
        }))
      throw new Error("The approval result could not be verified. Refresh the task before continuing.");
  } else if (current.state !== "Planned") {
    throw new Error("This task is not ready to execute. Refresh its status before continuing.");
  }
  signal.throwIfAborted();
  const started = await api.executeAutomationTask(current.taskId, signal);
  if (started.taskId !== current.taskId || !["Planned", "Running", "AwaitingConfirmation", "Completed", "Failed", "Cancelled"].includes(started.state))
    throw new Error("Execution was requested, but its status could not be verified. Refresh the task.");
  return started;
}

export async function watchAutomationTask(
  taskId: string,
  onUpdate: (task: AutomationTaskSnapshot) => void,
  onError: (message: string) => void,
  signal: AbortSignal,
  conversationId?: string,
): Promise<void> {
  let failures = 0;
  while (!signal.aborted) {
    let delay = 1000;
    let task: AutomationTaskSnapshot | undefined;
    try {
      task = conversationId == null ? await api.automationTask(taskId, signal) : await api.automationTask(taskId, signal, conversationId);
    } catch (error) {
      if (signal.aborted) return;
      const reason = error instanceof Error ? error.message : "Unknown status request failure";
      const retryable = error instanceof ApiError
        ? error.status === 429 || (error.status !== undefined && error.status >= 500 && error.status <= 599)
        : error instanceof TypeError || (error instanceof Error && ["TimeoutError", "AbortError"].includes(error.name));
      if (!retryable) {
        onError(`Status updates stopped: ${reason}. Refresh status after resolving this error.`);
        return;
      }
      failures++;
      delay = Math.min(10_000, 1000 * 2 ** Math.min(failures - 1, 4));
      onError(failures >= 3
        ? `Status is temporarily unavailable after ${failures} failed checks: ${reason}. Retrying the status check in ${delay / 1000} seconds.`
        : `Status check failed (${failures}): ${reason}. Retrying the status check in ${delay / 1000} seconds.`);
    }
    if (signal.aborted) return;
    if (task) {
      onUpdate(task);
      failures = 0;
      if (task.archived || ["Completed", "Failed", "Cancelled"].includes(task.state)) return;
      if (task.state === "AwaitingConfirmation") delay = 5000;
    }
    await new Promise<void>((resolve) => {
      const finish = () => {
        clearTimeout(timer);
        signal.removeEventListener("abort", finish);
        resolve();
      };
      const timer = setTimeout(finish, delay);
      signal.addEventListener("abort", finish, { once: true });
      if (signal.aborted) finish();
    });
  }
}

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
