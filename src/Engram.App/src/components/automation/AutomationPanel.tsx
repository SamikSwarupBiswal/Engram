import { useState } from "react";
import "../../lib/api";

type Action = { type: string; description: string; value?: string; selector?: string };
type Plan = { planId: string; goal: string; status: string; actions: { actionId: string; type: string; description: string; permission: string; status: string }[]; progress: number };

export function AutomationPanel() {
  const [goal, setGoal] = useState("");
  const [plan, setPlan] = useState<Plan | null>(null);
  const [actions, setActions] = useState<Action[]>([{ type: "navigate", description: "Go to URL", value: "" }]);
  const [log, setLog] = useState<{ actionId: string; type: string; description: string; status: string; result: string | null; error: string | null }[]>([]);

  const addAction = () => setActions([...actions, { type: "click", description: "", value: "" }]);
  const removeAction = (i: number) => setActions(actions.filter((_, idx) => idx !== i));
  const updateAction = (i: number, field: keyof Action, val: string) => {
    const next = [...actions];
    next[i] = { ...next[i], [field]: val };
    setActions(next);
  };

  const handleCreatePlan = async () => {
    if (!goal.trim()) return;
    try {
      const res = await fetch("http://127.0.0.1:5000/api/automation/plan", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ goal, actions }),
      }).then(r => r.json());
      setPlan(res);
    } catch {}
  };

  const handleApproveAll = async () => {
    if (!plan) return;
    try {
      await fetch("http://127.0.0.1:5000/api/automation/approve-all", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(plan),
      }).then(r => r.json());
      // Update local plan
      setPlan({
        ...plan,
        actions: plan.actions.map(a => ({ ...a, permission: a.permission === "pending" ? "approved" : a.permission })),
      });
    } catch {}
  };

  const handleExecute = async () => {
    if (!plan) return;
    try {
      const res = await fetch("http://127.0.0.1:5000/api/automation/execute", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(plan),
      }).then(r => r.json());
      setPlan(res);
      // Load log
      const logRes = await fetch("http://127.0.0.1:5000/api/automation/log").then(r => r.json());
      setLog(logRes.log || []);
    } catch {}
  };

  const permColor = (p: string) => {
    switch (p) {
      case "approved": return "text-emerald-400";
      case "autoApproved": return "text-emerald-400";
      case "denied": return "text-red-400";
      default: return "text-yellow-400";
    }
  };

  const statusColor = (s: string) => {
    switch (s) {
      case "completed": return "text-emerald-400";
      case "running": return "text-blue-400";
      case "failed": return "text-red-400";
      case "denied": return "text-[#888]";
      default: return "text-[#888]";
    }
  };

  return (
    <div className="flex h-full flex-col">
      <div className="border-b border-white/[0.06] px-4 py-3">
        <h2 className="text-[14px] font-medium text-[#ececec]">Automation</h2>
        <p className="text-[11px] text-[#888]">Plan and execute desktop actions with approval</p>
      </div>

      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        {/* Plan creator */}
        <div>
          <input type="text" value={goal} onChange={(e) => setGoal(e.target.value)} placeholder="Goal (e.g. Fill out login form)" className="w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-2 text-sm text-[#ececec] placeholder:text-[#666] mb-2" />

          <div className="text-[11px] text-[#888] mb-1">Actions:</div>
          {actions.map((a, i) => (
            <div key={i} className="flex gap-1 mb-1">
              <select value={a.type} onChange={(e) => updateAction(i, "type", e.target.value)} className="rounded border border-white/[0.08] bg-[#212121] px-2 py-1 text-[11px] text-[#ececec]">
                {["navigate", "click", "type", "keyPress", "wait", "screenshot", "scroll"].map(t => <option key={t} value={t}>{t}</option>)}
              </select>
              <input type="text" value={a.description} onChange={(e) => updateAction(i, "description", e.target.value)} placeholder="Description" className="flex-1 rounded border border-white/[0.08] bg-[#212121] px-2 py-1 text-[11px] text-[#ececec] placeholder:text-[#666]" />
              <input type="text" value={a.value || ""} onChange={(e) => updateAction(i, "value", e.target.value)} placeholder="Value/URL" className="flex-1 rounded border border-white/[0.08] bg-[#212121] px-2 py-1 text-[11px] text-[#ececec] placeholder:text-[#666]" />
              {actions.length > 1 && <button onClick={() => removeAction(i)} className="text-[11px] text-red-400 px-1">×</button>}
            </div>
          ))}
          <div className="flex gap-2 mt-2">
            <button onClick={addAction} className="rounded-lg border border-white/[0.08] px-3 py-1 text-[11px] text-[#888] hover:bg-white/[0.04]">+ Add Action</button>
            <button onClick={handleCreatePlan} disabled={!goal.trim()} className="rounded-lg bg-emerald-600 px-4 py-1 text-[11px] text-white hover:bg-emerald-700 disabled:opacity-50">Create Plan</button>
          </div>
        </div>

        {/* Plan view */}
        {plan && (
          <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
            <div className="mb-2 flex items-center justify-between">
              <span className="text-[13px] text-[#ececec]">{plan.goal}</span>
              <span className={`text-[11px] ${statusColor(plan.status)}`}>{plan.status}</span>
            </div>

            <div className="space-y-1 mb-3">
              {plan.actions.map((a) => (
                <div key={a.actionId} className="flex items-center gap-2 text-[12px]">
                  <span className={`text-[10px] ${permColor(a.permission)}`}>[{a.permission}]</span>
                  <span className="text-[#888]">{a.type}</span>
                  <span className="text-[#b4b4b4]">{a.description}</span>
                  <span className={`text-[10px] ${statusColor(a.status)}`}>{a.status}</span>
                </div>
              ))}
            </div>

            <div className="flex gap-2">
              <button onClick={handleApproveAll} className="rounded-lg bg-emerald-600 px-3 py-1 text-[11px] text-white hover:bg-emerald-700">Approve All</button>
              <button onClick={handleExecute} className="rounded-lg bg-blue-600 px-3 py-1 text-[11px] text-white hover:bg-blue-700">Execute</button>
              <button onClick={() => setPlan(null)} className="rounded-lg border border-white/[0.08] px-3 py-1 text-[11px] text-[#888]">Clear</button>
            </div>
          </div>
        )}

        {/* Action log */}
        {log.length > 0 && (
          <div>
            <div className="text-[11px] text-[#888] mb-1">Action Log ({log.length})</div>
            {log.map((entry) => (
              <div key={entry.actionId} className="rounded-lg bg-white/[0.02] p-2 mb-1">
                <div className="flex items-center gap-2 text-[11px]">
                  <span className="text-[#888]">{entry.type}</span>
                  <span className="text-[#b4b4b4]">{entry.description}</span>
                  <span className={`text-[10px] ${statusColor(entry.status)}`}>{entry.status}</span>
                </div>
                {entry.result && <div className="text-[10px] text-[#666] mt-0.5">{entry.result}</div>}
                {entry.error && <div className="text-[10px] text-red-400 mt-0.5">{entry.error}</div>}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
