import { useState, useEffect } from "react";
import { api, type AuditEntry, type ReasonTrace, type TrustScore, type GovernanceActivity, type ConstitutionalViolation } from "../../lib/api";

export function GovernancePanel() {
  const [auditState, setAuditState] = useState<string>("Operational");
  const [auditEntries, setAuditEntries] = useState<AuditEntry[]>([]);
  const [integrityValid, setIntegrityValid] = useState<boolean>(true);
  const [trustScores, setTrustScores] = useState<Record<string, TrustScore>>({});
  const [autonomyCeiling, setAutonomyCeiling] = useState<number>(1.0);
  const [interventionFreq, setInterventionFreq] = useState<number>(1.0);
  
  // Homeostasis & Pacing indicators
  const [homeostasisIndex, setHomeostasisIndex] = useState<number>(1.0);
  const [homeostasisState, setHomeostasisState] = useState<string>("Optimal");
  const [semanticState, setSemanticState] = useState<string>("System running at full cognitive fidelity.");
  const [annoyanceScore, setAnnoyanceScore] = useState<number>(0.0);
  const [consecutiveFriction, setConsecutiveFriction] = useState<number>(0);
  const [availablePacingTokens, setAvailablePacingTokens] = useState<number>(5);
  const [cognitiveDebtCount, setCognitiveDebtCount] = useState<number>(0);
  const [floorDetected, setFloorDetected] = useState<boolean>(false);
  
  const [traces, setTraces] = useState<ReasonTrace[]>([]);
  const [activity, setActivity] = useState<GovernanceActivity[]>([]);
  
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  // Forms state
  const [forgetNodeId, setForgetNodeId] = useState("");
  const [disputeNodeId, setDisputeNodeId] = useState("");
  const [disputeClaimId, setDisputeClaimId] = useState("");
  const [disputeCorrectedValue, setDisputeCorrectedValue] = useState("");
  const [recoveryDetail, setRecoveryDetail] = useState("");
  const [submittingForget, setSubmittingForget] = useState(false);
  const [submittingDispute, setSubmittingDispute] = useState(false);
  const [submittingRecovery, setSubmittingRecovery] = useState(false);
  const [activeSovereigntyTab, setActiveSovereigntyTab] = useState<"forget" | "dispute">("forget");

  const loadData = async () => {
    setLoading(true);
    try {
      const [auditData, trustData, tracesData, activityData] = await Promise.all([
        api.governanceAudit().catch(() => ({ state: "Operational", entries: [], integrityValid: true })),
        api.governanceTrust().catch(() => ({
          scores: {},
          grants: {},
          autonomyCeiling: 1.0,
          interventionFrequencyMultiplier: 1.0,
          homeostasisIndex: 1.0,
          homeostasisState: "Optimal",
          semanticState: "System running at full cognitive fidelity.",
          annoyanceScore: 0.0,
          consecutiveFriction: 0,
          availablePacingTokens: 5,
          cognitiveDebtCount: 0,
          floorDetected: false
        })),
        api.governanceTraces().catch(() => []),
        api.governanceActivity().catch(() => [])
      ]);

      setAuditState(auditData.state);
      setAuditEntries(auditData.entries || []);
      setIntegrityValid(auditData.integrityValid);
      
      setTrustScores(trustData.scores || {});
      setAutonomyCeiling(trustData.autonomyCeiling);
      setInterventionFreq(trustData.interventionFrequencyMultiplier);

      // Homeostasis & fatigue
      setHomeostasisIndex(trustData.homeostasisIndex ?? 1.0);
      setHomeostasisState(trustData.homeostasisState ?? "Optimal");
      setSemanticState(trustData.semanticState ?? "System running at full cognitive fidelity.");
      setAnnoyanceScore(trustData.annoyanceScore ?? 0.0);
      setConsecutiveFriction(trustData.consecutiveFriction ?? 0);
      setAvailablePacingTokens(trustData.availablePacingTokens ?? 5);
      setCognitiveDebtCount(trustData.cognitiveDebtCount ?? 0);
      setFloorDetected(trustData.floorDetected ?? false);
      
      setTraces(tracesData);
      setActivity(activityData);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load governance metrics");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
    const interval = setInterval(loadData, 10000); // refresh every 10s
    return () => clearInterval(interval);
  }, []);

  const handleForget = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!forgetNodeId.trim()) return;
    setSubmittingForget(true);
    try {
      await api.governanceForget(forgetNodeId.trim());
      alert(`Forgetting initiated for node: ${forgetNodeId}`);
      setForgetNodeId("");
      loadData();
    } catch (err) {
      alert("Forget operation failed: " + (err instanceof Error ? err.message : "unknown error"));
    } finally {
      setSubmittingForget(false);
    }
  };

  const handleDispute = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!disputeNodeId.trim() || !disputeClaimId.trim() || !disputeCorrectedValue.trim()) return;
    setSubmittingDispute(true);
    try {
      await api.governanceDispute(disputeNodeId.trim(), disputeClaimId.trim(), disputeCorrectedValue.trim());
      alert(`Dispute registered successfully for claim ${disputeClaimId} on node ${disputeNodeId}`);
      setDisputeNodeId("");
      setDisputeClaimId("");
      setDisputeCorrectedValue("");
      loadData();
    } catch (err) {
      alert("Dispute operation failed: " + (err instanceof Error ? err.message : "unknown error"));
    } finally {
      setSubmittingDispute(false);
    }
  };

  const handleRecover = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!recoveryDetail.trim()) return;
    setSubmittingRecovery(true);
    try {
      const result = await api.governanceRecover(recoveryDetail.trim());
      alert(`Bypass submitted. Current state: ${result.state}`);
      setRecoveryDetail("");
      loadData();
    } catch (err) {
      alert("Recovery bypass failed: " + (err instanceof Error ? err.message : "unknown error"));
    } finally {
      setSubmittingRecovery(false);
    }
  };

  const getSeverityBadgeColor = (severity: string | number) => {
    const s = String(severity);
    if (s.includes("C5") || s === "4") return "bg-red-950/60 text-red-400 border-red-500/30";
    if (s.includes("C4") || s === "3") return "bg-orange-950/60 text-orange-400 border-orange-500/30";
    if (s.includes("C3") || s === "2") return "bg-yellow-950/60 text-yellow-400 border-yellow-500/30";
    if (s.includes("C2") || s === "1") return "bg-blue-950/60 text-blue-400 border-blue-500/30";
    return "bg-zinc-900/60 text-zinc-400 border-zinc-500/20";
  };

  const getSeverityLabel = (severity: string | number) => {
    const s = String(severity);
    if (s === "0") return "C1";
    if (s === "1") return "C2";
    if (s === "2") return "C3";
    if (s === "3") return "C4";
    if (s === "4") return "C5";
    return s;
  };

  const getTriggerTypeLabel = (type: number) => {
    switch (type) {
      case 0: return "Intervention Triggered";
      case 1: return "Salience Shift";
      case 2: return "Task Paused";
      case 3: return "Escalation Alert";
      case 4: return "Execution Decision";
      default: return "Cognitive Occurrence";
    }
  };

  const parseViolationData = (data: string): ConstitutionalViolation | null => {
    try {
      return JSON.parse(data) as ConstitutionalViolation;
    } catch {
      return null;
    }
  };

  if (loading) {
    return (
      <div className="flex h-full flex-col items-center justify-center p-6 text-[#ececec]">
        <div className="flex flex-col items-center gap-3">
          <div className="h-6 w-6 animate-spin rounded-full border-2 border-white/[0.08] border-t-emerald-500" />
          <div className="text-xs text-[#888]">Loading Governance Systems...</div>
        </div>
      </div>
    );
  }

  const isFrozen = auditState === "Frozen" || auditState === "RecoveryPending" || auditState === "AuditRequired";

  return (
    <div className="flex h-full flex-col overflow-y-auto p-6 text-[#ececec]">
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold tracking-tight">Governance & Safety</h2>
          <p className="text-xs text-[#888]">Engram's local safety constitution, cognitive boundaries, and trust monitoring.</p>
        </div>
        <button onClick={loadData} className="rounded-lg bg-white/[0.06] px-3 py-1.5 text-xs text-[#b4b4b4] hover:bg-white/[0.1] transition-colors">
          Refresh Data
        </button>
      </div>

      {error && (
        <div className="mb-6 rounded-xl bg-red-900/20 border border-red-900/30 px-4 py-3 text-sm text-red-400">
          {error}
        </div>
      )}

      {/* Emergency Freeze Bypass Console */}
      {isFrozen && (
        <div className="mb-8 rounded-2xl border border-red-500/30 bg-red-950/20 p-5 shadow-lg shadow-red-950/10 animate-pulse">
          <div className="flex items-start gap-4">
            <span className="text-2xl">🚨</span>
            <div className="flex-1">
              <h3 className="text-sm font-semibold text-red-400">OPERATIONAL FREEZE ACTIVE</h3>
              <p className="mt-1 text-xs text-[#b4b4b4] leading-relaxed">
                The execution layer has been suspended due to a critical safety constitution breach (State: <span className="font-mono text-white underline">{auditState}</span>).
                All outbound operations, file writes, and browser automations are blocked until manual human resolution is supplied.
              </p>
              <form onSubmit={handleRecover} className="mt-4 flex gap-2">
                <input
                  type="text"
                  placeholder="Enter manual audit resolution notes..."
                  value={recoveryDetail}
                  onChange={(e) => setRecoveryDetail(e.target.value)}
                  className="flex-1 rounded-lg border border-red-500/20 bg-[#212121] px-3 py-2 text-xs text-[#ececec] focus:border-red-500/40 focus:outline-none placeholder:text-[#555]"
                  disabled={submittingRecovery}
                  required
                />
                <button
                  type="submit"
                  disabled={submittingRecovery}
                  className="rounded-lg bg-red-600 px-4 py-2 text-xs font-medium text-white hover:bg-red-700 disabled:opacity-50 transition-colors"
                >
                  {submittingRecovery ? "Bypassing..." : "Resolve & Resume"}
                </button>
              </form>
            </div>
          </div>
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-2">
        {/* Left Column: Metrics and Sovereignty */}
        <div className="space-y-6">
          {/* Status & Calibration meters */}
          <div className="rounded-2xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5 backdrop-blur-md">
            <h3 className="mb-4 text-[13px] font-medium text-[#b4b4b4] uppercase tracking-wider">Cognitive Calibration</h3>
            
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="rounded-xl bg-white/[0.03] p-4 border border-white/[0.02]">
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs text-[#888]">Autonomy Ceiling</span>
                  <span className="text-xs font-mono text-emerald-400">{(autonomyCeiling * 100).toFixed(0)}%</span>
                </div>
                <div className="h-2 w-full rounded-full bg-white/[0.06] overflow-hidden">
                  <div
                    className="h-full bg-emerald-500 transition-all duration-500"
                    style={{ width: `${autonomyCeiling * 100}%` }}
                  />
                </div>
                <span className="mt-1.5 block text-[10px] text-[#666]">Limits how deep Engram can execute actions autonomously.</span>
              </div>

              <div className="rounded-xl bg-white/[0.03] p-4 border border-white/[0.02]">
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs text-[#888]">Intervention Throttle</span>
                  <span className="text-xs font-mono text-blue-400">{(interventionFreq * 100).toFixed(0)}%</span>
                </div>
                <div className="h-2 w-full rounded-full bg-white/[0.06] overflow-hidden">
                  <div
                    className="h-full bg-blue-500 transition-all duration-500"
                    style={{ width: `${interventionFreq * 100}%` }}
                  />
                </div>
                <span className="mt-1.5 block text-[10px] text-[#666]">Scales down alert frequency under high user friction or flow state.</span>
              </div>
            </div>

            {/* Granular trust score table */}
            <div className="mt-5">
              <h4 className="text-xs text-[#888] mb-2 font-medium">Domain Trust Scores</h4>
              {Object.keys(trustScores).length === 0 ? (
                <p className="text-xs text-[#666] italic py-2">No active scores recorded yet.</p>
              ) : (
                <div className="space-y-2 max-h-48 overflow-y-auto">
                  {Object.entries(trustScores).map(([domain, data]) => (
                    <div key={domain} className="flex items-center justify-between rounded-lg bg-white/[0.02] p-2.5 border border-white/[0.04]">
                      <div>
                        <div className="text-xs font-medium font-mono text-[#ececec]">{domain}</div>
                        <div className="text-[10px] text-[#666]">Streak: {data.successStreak} successes · Overrides: {data.overrideCount}</div>
                      </div>
                      <div className="flex items-center gap-2">
                        <div className="h-1.5 w-16 rounded-full bg-white/[0.06] overflow-hidden">
                          <div
                            className={`h-full transition-all ${data.score > 0.7 ? "bg-emerald-500" : data.score > 0.4 ? "bg-yellow-500" : "bg-red-500"}`}
                            style={{ width: `${data.score * 100}%` }}
                          />
                        </div>
                        <span className={`text-xs font-mono font-medium ${data.score > 0.7 ? "text-emerald-400" : data.score > 0.4 ? "text-yellow-400" : "text-red-400"}`}>
                          {data.score.toFixed(2)}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          {/* Metabolic Homeostasis & Pacing */}
          <div className="rounded-2xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5 backdrop-blur-md">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-[13px] font-medium text-[#b4b4b4] uppercase tracking-wider">Metabolic Homeostasis & Pacing</h3>
              {floorDetected && (
                <span className="rounded-full bg-red-950/60 border border-red-500/30 px-2.5 py-0.5 text-[9px] font-semibold text-red-400 animate-pulse">
                  FLOOR DETECTED
                </span>
              )}
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <div className="rounded-xl bg-white/[0.03] p-4 border border-white/[0.02] sm:col-span-2">
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs text-[#888]">Metabolic State</span>
                  <span className={`text-xs font-mono font-medium ${
                    homeostasisState === "Optimal" ? "text-emerald-400" : homeostasisState === "Congested" ? "text-yellow-400" : "text-red-400 animate-pulse"
                  }`}>
                    {homeostasisState}
                  </span>
                </div>
                <p className="mt-1 text-[11px] text-[#b4b4b4] leading-relaxed">
                  {semanticState}
                </p>
              </div>

              <div className="rounded-xl bg-white/[0.03] p-4 border border-white/[0.02]">
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs text-[#888]">Homeostasis Index</span>
                  <span className="text-xs font-mono text-emerald-400">{(homeostasisIndex * 100).toFixed(0)}%</span>
                </div>
                <div className="h-2 w-full rounded-full bg-white/[0.06] overflow-hidden mt-1.5">
                  <div
                    className={`h-full transition-all duration-500 ${
                      homeostasisIndex > 0.8 ? "bg-emerald-500" : homeostasisIndex > 0.4 ? "bg-yellow-500" : "bg-red-500"
                    }`}
                    style={{ width: `${homeostasisIndex * 100}%` }}
                  />
                </div>
                <span className="mt-1.5 block text-[10px] text-[#666]">Recovery/capacity scaling factor under local resource load.</span>
              </div>

              <div className="rounded-xl bg-white/[0.03] p-4 border border-white/[0.02]">
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs text-[#888]">Pacing & Friction</span>
                  <span className="text-xs font-mono text-blue-400">{availablePacingTokens} / 5 tokens</span>
                </div>
                <div className="text-[10px] text-[#666] leading-relaxed mt-1">
                  Annoyance Score: <span className="font-mono text-[#ececec]">{annoyanceScore.toFixed(1)}/10.0</span> <br />
                  Consecutive Friction: <span className="font-mono text-[#ececec]">{consecutiveFriction}</span>
                </div>
                <span className="mt-1.5 block text-[10px] text-[#666]">Friction decays trust / increases alert silence thresholds.</span>
              </div>

              <div className="rounded-xl bg-white/[0.03] p-4 border border-white/[0.02] sm:col-span-2">
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs text-[#888]">Cognitive Debt Queue</span>
                  <span className="text-xs font-mono text-yellow-400">{cognitiveDebtCount} tasks</span>
                </div>
                <span className="mt-1.5 block text-[10px] text-[#666]">Deferred background reflections and narrative synthesis waiting for system idle state.</span>
              </div>
            </div>
          </div>

          {/* Memory Sovereignty System */}
          <div className="rounded-2xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5 backdrop-blur-md">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-[13px] font-medium text-[#b4b4b4] uppercase tracking-wider">Memory Sovereignty</h3>
              <div className="flex gap-1 bg-white/[0.04] p-0.5 rounded-lg border border-white/[0.04]">
                <button
                  onClick={() => setActiveSovereigntyTab("forget")}
                  className={`px-2 py-1 text-[11px] rounded-md font-medium transition-colors ${activeSovereigntyTab === "forget" ? "bg-white/[0.06] text-white" : "text-[#888] hover:text-[#b4b4b4]"}`}
                >
                  Forget Node
                </button>
                <button
                  onClick={() => setActiveSovereigntyTab("dispute")}
                  className={`px-2 py-1 text-[11px] rounded-md font-medium transition-colors ${activeSovereigntyTab === "dispute" ? "bg-white/[0.06] text-white" : "text-[#888] hover:text-[#b4b4b4]"}`}
                >
                  Dispute Claim
                </button>
              </div>
            </div>

            {activeSovereigntyTab === "forget" ? (
              <form onSubmit={handleForget} className="space-y-3">
                <p className="text-[11px] text-[#888] leading-relaxed">
                  Permanently deletes a node by ID. This executes a <strong>true forget</strong>: removing node content, scrubbing links/edges from other nodes, purges claims, and installs a placeholder `HistoricalDeletionEnvelope`.
                </p>
                <div className="flex gap-2">
                  <input
                    type="text"
                    placeholder="Enter wiki node ID (e.g. project_alpha)..."
                    value={forgetNodeId}
                    onChange={(e) => setForgetNodeId(e.target.value)}
                    className="flex-1 rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-xs text-[#ececec] focus:border-white/[0.2] focus:outline-none placeholder:text-[#555]"
                    disabled={submittingForget}
                    required
                  />
                  <button
                    type="submit"
                    disabled={submittingForget || !forgetNodeId.trim()}
                    className="rounded-lg bg-red-600/20 px-4 py-2 text-xs font-medium text-red-400 hover:bg-red-600/30 disabled:opacity-50 border border-red-500/20 transition-colors animate-pulse-slow"
                  >
                    {submittingForget ? "Purging..." : "Purge Memory"}
                  </button>
                </div>
              </form>
            ) : (
              <form onSubmit={handleDispute} className="space-y-3">
                <p className="text-[11px] text-[#888] leading-relaxed">
                  Contests a specific semantic claim on a node. Immediately sets claim confidence to 0%, prompts narrative rollback to recalculate derivative weights, and preserves the correction as a future alignment constraint.
                </p>
                <div className="grid gap-2 sm:grid-cols-3">
                  <input
                    type="text"
                    placeholder="Node ID"
                    value={disputeNodeId}
                    onChange={(e) => setDisputeNodeId(e.target.value)}
                    className="rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-xs text-[#ececec] focus:border-white/[0.2] focus:outline-none placeholder:text-[#555]"
                    disabled={submittingDispute}
                    required
                  />
                  <input
                    type="text"
                    placeholder="Claim ID"
                    value={disputeClaimId}
                    onChange={(e) => setDisputeClaimId(e.target.value)}
                    className="rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-xs text-[#ececec] focus:border-white/[0.2] focus:outline-none placeholder:text-[#555]"
                    disabled={submittingDispute}
                    required
                  />
                  <input
                    type="text"
                    placeholder="Correct Value"
                    value={disputeCorrectedValue}
                    onChange={(e) => setDisputeCorrectedValue(e.target.value)}
                    className="rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-xs text-[#ececec] focus:border-white/[0.2] focus:outline-none placeholder:text-[#555]"
                    disabled={submittingDispute}
                    required
                  />
                </div>
                <button
                  type="submit"
                  disabled={submittingDispute || !disputeNodeId.trim() || !disputeClaimId.trim() || !disputeCorrectedValue.trim()}
                  className="w-full rounded-lg bg-blue-600/20 px-4 py-2 text-xs font-medium text-blue-400 hover:bg-blue-600/30 disabled:opacity-50 border border-blue-500/20 transition-colors"
                >
                  {submittingDispute ? "Submitting Dispute..." : "Submit Reality Dispute"}
                </button>
              </form>
            )}
          </div>

          {/* Causal Reason Traces */}
          <div className="rounded-2xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5 backdrop-blur-md">
            <h3 className="mb-4 text-[13px] font-medium text-[#b4b4b4] uppercase tracking-wider">Causal Reason Traces</h3>
            {traces.length === 0 ? (
              <p className="text-xs text-[#666] italic py-4 text-center">No reason traces recorded yet.</p>
            ) : (
              <div className="space-y-3 max-h-72 overflow-y-auto pr-1">
                {traces.map((trace) => (
                  <div key={trace.traceId} className="rounded-xl bg-white/[0.02] p-3 border border-white/[0.04]">
                    <div className="flex items-center justify-between mb-1.5">
                      <span className="text-[10px] font-mono text-[#888]">{new Date(trace.timestamp).toLocaleString()}</span>
                      <span className={`rounded-full px-2 py-0.5 text-[9px] border font-medium ${getSeverityBadgeColor(trace.triggerType)}`}>
                        {getTriggerTypeLabel(trace.triggerType)}
                      </span>
                    </div>
                    <div className="text-xs font-semibold text-[#ececec]">{trace.description}</div>
                    <div className="mt-1 text-[10px] text-[#666]">Component: {trace.systemComponent} · Target: {trace.targetEntityId}</div>
                    {trace.causalFactors.length > 0 && (
                      <div className="mt-2">
                        <div className="text-[9px] text-[#888] font-medium mb-1">Causal Factors:</div>
                        <ul className="list-disc pl-3 text-[10px] text-[#b4b4b4] space-y-0.5">
                          {trace.causalFactors.map((f, i) => (
                            <li key={i}>{f}</li>
                          ))}
                        </ul>
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Right Column: Audit Trail & Activity */}
        <div className="space-y-6">
          {/* Safety Constitution Audit Trail */}
          <div className="rounded-2xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5 backdrop-blur-md">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-[13px] font-medium text-[#b4b4b4] uppercase tracking-wider">Constitutional Audit</h3>
              <div className="flex items-center gap-1.5">
                <span className={`h-1.5 w-1.5 rounded-full ${integrityValid ? "bg-emerald-500" : "bg-red-500 animate-ping"}`} />
                <span className={`text-[10px] font-mono font-medium ${integrityValid ? "text-emerald-400" : "text-red-400"}`}>
                  {integrityValid ? "VERIFIED OK" : "INTEGRITY COMPROMISED"}
                </span>
              </div>
            </div>

            {auditEntries.length === 0 ? (
              <p className="text-xs text-[#666] italic py-8 text-center">Audit log is currently empty.</p>
            ) : (
              <div className="space-y-3 max-h-[350px] overflow-y-auto pr-1">
                {auditEntries.slice().reverse().map((entry) => {
                  const violation = parseViolationData(entry.data);
                  return (
                    <div key={entry.entryId} className="rounded-xl bg-white/[0.02] p-3 border border-white/[0.04]">
                      <div className="flex items-center justify-between mb-1.5">
                        <span className="text-[10px] font-mono text-[#888]">{new Date(entry.timestamp).toLocaleString()}</span>
                        {violation && (
                          <span className={`rounded-full px-2 py-0.5 text-[9px] border font-medium ${getSeverityBadgeColor(violation.severity)}`}>
                            {getSeverityLabel(violation.severity)}
                          </span>
                        )}
                      </div>

                      {violation ? (
                        <div className="space-y-2">
                          <div className="text-xs font-semibold text-[#ececec]">
                            {violation.violatingSubsystem}: {violation.details}
                          </div>
                          {violation.triggerAction && (
                            <div className="text-[10px] text-[#888]">
                              Trigger action: <span className="font-mono text-[#b4b4b4]">{violation.triggerAction}</span>
                            </div>
                          )}
                          {violation.causalChain && violation.causalChain.length > 0 && (
                            <div className="rounded bg-black/20 p-2 text-[10px] text-[#888] font-mono">
                              <div className="font-semibold text-[9px] mb-1 text-[#666]">Chain of logic:</div>
                              {violation.causalChain.map((step, idx) => (
                                <div key={idx}>→ {step}</div>
                              ))}
                            </div>
                          )}
                          {violation.userResolution && (
                            <div className="text-[10px] border-t border-white/[0.04] pt-1.5 text-emerald-400">
                              Resolution: <span className="text-[#b4b4b4]">{violation.userResolution}</span>
                            </div>
                          )}
                        </div>
                      ) : (
                        <div className="text-xs font-mono text-[#888] break-all">{entry.data}</div>
                      )}

                      <div className="mt-2 text-[8px] font-mono text-[#555] break-all">
                        Block Hash: {entry.hash.slice(0, 16)}...
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          {/* Semantic Activity Feed */}
          <div className="rounded-2xl border border-white/[0.06] bg-[#2f2f2f]/50 p-5 backdrop-blur-md">
            <h3 className="mb-4 text-[13px] font-medium text-[#b4b4b4] uppercase tracking-wider">Semantic Activity Feed</h3>
            {activity.length === 0 ? (
              <p className="text-xs text-[#666] italic py-8 text-center">No activity recorded yet.</p>
            ) : (
              <div className="space-y-3 max-h-72 overflow-y-auto pr-1">
                {activity.slice().reverse().map((act) => (
                  <div key={act.entryId} className="rounded-xl bg-white/[0.02] p-3 border border-[#3c3c3c]/10 flex items-start gap-3">
                    <span className={`mt-1 h-1.5 w-1.5 rounded-full flex-shrink-0 ${
                      act.impactLevel === "High" ? "bg-red-500 animate-pulse" : act.impactLevel === "Medium" ? "bg-yellow-500" : "bg-[#888]"
                    }`} />
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center justify-between">
                        <span className="text-[10px] text-[#b4b4b4] font-medium">{act.action}</span>
                        <span className="text-[9px] text-[#666] font-mono">{new Date(act.timestamp).toLocaleTimeString()}</span>
                      </div>
                      <p className="mt-1 text-xs text-[#888] leading-relaxed break-words">{act.description}</p>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
