import { useState } from "react";
import { api, type DiscoveryAnswers } from "../../lib/api";

interface DiscoveryInterviewProps {
  onComplete: () => void;
  onSkip: () => void;
}

type Step = "welcome" | "name" | "goals" | "triggers" | "anxieties" | "priorities" | "antiGoals" | "warmup" | "confirm";

const STEPS: { id: Step; title: string; prompt: string; placeholder: string }[] = [
  {
    id: "name",
    title: "Your Name",
    prompt: "What should I call you?",
    placeholder: "e.g., Samik",
  },
  {
    id: "goals",
    title: "Goals",
    prompt: "What are your top goals right now? (one per line)",
    placeholder: "Build Engram\nGet healthier\nLearn Rust",
  },
  {
    id: "triggers",
    title: "Comfort Triggers",
    prompt: "What makes you feel 'handled' or safe? What should I prioritize? (one per line)",
    placeholder: "Clear communication\nProactive reminders\nNo surprises",
  },
  {
    id: "anxieties",
    title: "Recurring Anxieties",
    prompt: "What do you often worry about forgetting or missing? (one per line)",
    placeholder: "Deadlines\nImportant emails\nHealth appointments",
  },
  {
    id: "priorities",
    title: "Priorities",
    prompt: "What are your main life priority areas? (one per line)",
    placeholder: "Career: Ship Engram\nHealth: Exercise daily\nFinance: Save more",
  },
  {
    id: "antiGoals",
    title: "Anti-Goals",
    prompt: "What should I NEVER do? What behaviors annoy you? (one per line)",
    placeholder: "Don't suggest social media during work\nDon't send notifications after 10pm\nDon't repeat information I already know",
  },
];

const ALL_STEPS: Step[] = ["name", "goals", "triggers", "anxieties", "priorities", "antiGoals", "warmup"];

export function DiscoveryInterview({ onComplete, onSkip }: DiscoveryInterviewProps) {
  const [currentStep, setCurrentStep] = useState<Step>("welcome");
  const [answers, setAnswers] = useState<Partial<DiscoveryAnswers>>({
    goals: [],
    comfortTriggers: [],
    recurringAnxieties: [],
    preferences: [],
    priorities: [],
    antiGoals: [],
  });
  const [textInput, setTextInput] = useState("");
  const [saving, setSaving] = useState(false);

  // Trust warmup parameters state
  const [autonomyCeiling, setAutonomyCeiling] = useState<number>(0.5);
  const [maxDailyInterventions, setMaxDailyInterventions] = useState<number>(15);
  const [minConfidenceToEscalate, setMinConfidenceToEscalate] = useState<number>(0.7);

  const stepIndex = STEPS.findIndex((s) => s.id === currentStep);
  const step = STEPS[stepIndex];

  const allStepsIndex = ALL_STEPS.indexOf(currentStep);
  const progress = allStepsIndex >= 0 ? ((allStepsIndex + 1) / ALL_STEPS.length) * 100 : 0;
  const progressLabel = currentStep === "warmup" ? "Trust Warmup" : step?.title;

  const parseLines = (text: string) =>
    text
      .split("\n")
      .map((l) => l.trim())
      .filter((l) => l.length > 0);

  const handleNext = () => {
    const lines = parseLines(textInput);

    switch (currentStep) {
      case "welcome":
        setCurrentStep("name");
        break;
      case "name":
        setAnswers((a) => ({ ...a, displayName: textInput.trim() }));
        setTextInput("");
        setCurrentStep("goals");
        break;
      case "goals":
        setAnswers((a) => ({ ...a, goals: lines }));
        setTextInput("");
        setCurrentStep("triggers");
        break;
      case "triggers":
        setAnswers((a) => ({ ...a, comfortTriggers: lines }));
        setTextInput("");
        setCurrentStep("anxieties");
        break;
      case "anxieties":
        setAnswers((a) => ({ ...a, recurringAnxieties: lines }));
        setTextInput("");
        setCurrentStep("priorities");
        break;
      case "priorities":
        setAnswers((a) => ({
          ...a,
          priorities: lines.map((l) => {
            const [desc, cat] = l.split(":").map((s) => s.trim());
            return { description: desc || l, category: cat || "General" };
          }),
        }));
        setTextInput("");
        setCurrentStep("antiGoals");
        break;
      case "antiGoals":
        setAnswers((a) => ({
          ...a,
          antiGoals: lines.map((l) => ({
            description: l,
            severity: "Medium",
          })),
        }));
        setTextInput("");
        setCurrentStep("warmup");
        break;
      case "warmup":
        setCurrentStep("confirm");
        break;
      case "confirm":
        handleSave();
        break;
    }
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      await api.runDiscovery(answers as DiscoveryAnswers);
      // Update governance settings with choices selected during trust warmup onboarding
      await api.governanceUpdateSettings({
        retentionPolicies: [],
        sensitiveDomains: [],
        privacyZones: [],
        maxDailyInterventions,
        minConfidenceToEscalate,
        defaultTrustCeiling: autonomyCeiling
      }).catch((err) => console.error("Governance onboarding update failed", err));
      onComplete();
    } catch (e) {
      console.error("Discovery save failed:", e);
      // Still complete — the profile is saved locally
      onComplete();
    } finally {
      setSaving(false);
    }
  };

  const canProceed =
    currentStep === "welcome" ||
    currentStep === "warmup" ||
    currentStep === "confirm" ||
    textInput.trim().length > 0;

  return (
    <div className="flex h-full flex-col items-center justify-center bg-[#212121] p-6">
      <div className="w-full max-w-lg">
        {/* Progress bar */}
        {currentStep !== "welcome" && currentStep !== "confirm" && (
          <div className="mb-6">
            <div className="flex items-center justify-between mb-2">
              <span className="text-[11px] text-[#888]">{progressLabel}</span>
              <span className="text-[11px] text-[#666]">{Math.round(progress)}%</span>
            </div>
            <div className="h-1 w-full rounded-full bg-white/[0.06]">
              <div
                className="h-1 rounded-full bg-emerald-600 transition-all duration-300"
                style={{ width: `${progress}%` }}
              />
            </div>
          </div>
        )}

        {/* Welcome */}
        {currentStep === "welcome" && (
          <div className="text-center">
            <div className="mb-4 text-4xl">🧠</div>
            <h2 className="text-xl font-medium mb-2">Welcome to Engram</h2>
            <p className="text-sm text-[#b4b4b4] mb-1">
              Let's set up your personal memory layer. This takes about 2 minutes.
            </p>
            <p className="text-[13px] text-[#888] mb-8">
              I'll ask about your goals, preferences, and boundaries so I can
              help you better. You can change anything later in Settings.
            </p>
            <div className="flex gap-3 justify-center">
              <button
                onClick={handleNext}
                className="rounded-xl bg-emerald-600 px-6 py-2.5 text-sm font-medium text-white hover:bg-emerald-700"
              >
                Let's start
              </button>
              <button
                onClick={onSkip}
                className="rounded-xl border border-white/[0.08] bg-white/[0.04] px-6 py-2.5 text-sm text-[#b4b4b4] hover:bg-white/[0.08]"
              >
                Skip for now
              </button>
            </div>
          </div>
        )}

        {/* Input steps */}
        {step && currentStep !== "welcome" && currentStep !== "confirm" && (
          <div>
            <h2 className="text-lg font-medium mb-2">{step.title}</h2>
            <p className="text-sm text-[#b4b4b4] mb-4">{step.prompt}</p>
            <textarea
              className="w-full h-40 rounded-xl border border-white/[0.08] bg-[#2f2f2f] px-4 py-3 text-sm text-[#ececec] placeholder:text-[#666] focus:border-white/[0.2] focus:outline-none resize-none"
              placeholder={step.placeholder}
              value={textInput}
              onChange={(e) => setTextInput(e.target.value)}
              autoFocus
            />
            <div className="mt-4 flex justify-between">
              <button
                onClick={() => {
                  if (stepIndex > 0) {
                    setCurrentStep(STEPS[stepIndex - 1].id);
                  }
                }}
                className="rounded-xl px-4 py-2 text-sm text-[#888] hover:text-[#ececec]"
              >
                Back
              </button>
              <button
                onClick={handleNext}
                disabled={!canProceed}
                className="rounded-xl bg-emerald-600 px-6 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-40"
              >
                {stepIndex === STEPS.length - 1 ? "Review" : "Continue"}
              </button>
            </div>
          </div>
        )}

        {/* Trust Warmup Step */}
        {currentStep === "warmup" && (
          <div>
            <h2 className="text-lg font-medium mb-2">Trust Warmup & Autonomy</h2>
            <p className="text-sm text-[#b4b4b4] mb-5">
              Engram operates entirely locally and builds trust dynamically. Select your initial autonomy limits.
            </p>

            <div className="space-y-5">
              {/* Autonomy Ceiling */}
              <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <label className="block text-xs font-semibold text-[#ececec] uppercase tracking-wider mb-2">
                  Initial Autonomy Ceiling: {(autonomyCeiling * 100).toFixed(0)}%
                </label>
                <p className="text-[11px] text-[#888] mb-3 leading-relaxed">
                  Controls the maximum autonomy Engram has to perform operations (like filing web references or organizing concepts) before requesting explicit approval.
                </p>
                <div className="flex gap-2.5">
                  {[0.2, 0.5, 0.8].map((ceiling) => (
                    <button
                      key={ceiling}
                      type="button"
                      onClick={() => setAutonomyCeiling(ceiling)}
                      className={`flex-1 rounded-lg border py-2.5 text-xs font-semibold transition-all ${
                        autonomyCeiling === ceiling
                          ? "border-emerald-500 bg-emerald-950/20 text-emerald-400 font-semibold"
                          : "border-white/[0.06] bg-[#212121] text-[#888] hover:text-[#ececec]"
                      }`}
                    >
                      {ceiling === 0.2 ? "Conservative (20%)" : ceiling === 0.5 ? "Balanced (50%)" : "Autonomous (80%)"}
                    </button>
                  ))}
                </div>
              </div>

              {/* Max Daily Interventions */}
              <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <label className="block text-xs font-semibold text-[#ececec] uppercase tracking-wider mb-2">
                  Daily Interventions Cap: {maxDailyInterventions} / day
                </label>
                <p className="text-[11px] text-[#888] mb-3 leading-relaxed">
                  Defines the daily limit for notifications, prompts, or questions to prevent flow-state interruptions.
                </p>
                <div className="flex gap-2.5">
                  {[5, 15, 30].map((limit) => (
                    <button
                      key={limit}
                      type="button"
                      onClick={() => setMaxDailyInterventions(limit)}
                      className={`flex-1 rounded-lg border py-2.5 text-xs font-semibold transition-all ${
                        maxDailyInterventions === limit
                          ? "border-emerald-500 bg-emerald-950/20 text-emerald-400 font-semibold"
                          : "border-white/[0.06] bg-[#212121] text-[#888] hover:text-[#ececec]"
                      }`}
                    >
                      {limit === 5 ? "Calm (5)" : limit === 15 ? "Balanced (15)" : "Active (30)"}
                    </button>
                  ))}
                </div>
              </div>

              {/* Warmup Rate / Speed */}
              <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <label className="block text-xs font-semibold text-[#ececec] uppercase tracking-wider mb-2">
                  Warmup Rate: {minConfidenceToEscalate === 0.85 ? "Slow & Cautious" : minConfidenceToEscalate === 0.7 ? "Standard" : "Swift"}
                </label>
                <p className="text-[11px] text-[#888] mb-3 leading-relaxed">
                  Engram warms up capabilities over repeated successful runs. Cautious pace requires high confidence before auto-approving actions.
                </p>
                <div className="flex gap-2.5">
                  {[0.85, 0.7, 0.55].map((conf) => (
                    <button
                      key={conf}
                      type="button"
                      onClick={() => setMinConfidenceToEscalate(conf)}
                      className={`flex-1 rounded-lg border py-2.5 text-xs font-semibold transition-all ${
                        minConfidenceToEscalate === conf
                          ? "border-emerald-500 bg-emerald-950/20 text-emerald-400 font-semibold"
                          : "border-white/[0.06] bg-[#212121] text-[#888] hover:text-[#ececec]"
                      }`}
                    >
                      {conf === 0.85 ? "Cautious" : conf === 0.7 ? "Standard" : "Swift"}
                    </button>
                  ))}
                </div>
              </div>
            </div>

            <div className="mt-6 flex justify-between">
              <button
                onClick={() => setCurrentStep("antiGoals")}
                className="rounded-xl px-4 py-2 text-sm text-[#888] hover:text-[#ececec]"
              >
                Back
              </button>
              <button
                onClick={handleNext}
                className="rounded-xl bg-emerald-600 px-6 py-2.5 text-sm font-medium text-white hover:bg-emerald-700"
              >
                Continue
              </button>
            </div>
          </div>
        )}

        {/* Confirmation */}
        {currentStep === "confirm" && (
          <div>
            <h2 className="text-lg font-medium mb-4">Review Your Profile</h2>
            <div className="space-y-4">
              <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <div className="text-[11px] text-[#888] mb-1">Name</div>
                <div className="text-sm">{answers.displayName || "Not set"}</div>
              </div>
              <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <div className="text-[11px] text-[#888] mb-1">Goals ({answers.goals?.length || 0})</div>
                <div className="text-sm">{answers.goals?.join(", ") || "None"}</div>
              </div>
              <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <div className="text-[11px] text-[#888] mb-1">Comfort Triggers ({answers.comfortTriggers?.length || 0})</div>
                <div className="text-sm">{answers.comfortTriggers?.join(", ") || "None"}</div>
              </div>
              <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <div className="text-[11px] text-[#888] mb-1">Anxieties ({answers.recurringAnxieties?.length || 0})</div>
                <div className="text-sm">{answers.recurringAnxieties?.join(", ") || "None"}</div>
              </div>
              <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <div className="text-[11px] text-[#888] mb-1">Anti-Goals ({answers.antiGoals?.length || 0})</div>
                <div className="text-sm">{answers.antiGoals?.map((a) => a.description).join(", ") || "None"}</div>
              </div>
              <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
                <div className="text-[11px] text-[#888] mb-1">Trust & Autonomy Settings</div>
                <div className="text-sm">
                  Autonomy Ceiling: {autonomyCeiling * 100}% · Interventions Limit: {maxDailyInterventions}/day · Warmup Rate: {minConfidenceToEscalate === 0.85 ? "Cautious" : minConfidenceToEscalate === 0.7 ? "Standard" : "Swift"}
                </div>
              </div>
            </div>
            <div className="mt-6 flex justify-between">
              <button
                onClick={() => setCurrentStep("warmup")}
                className="rounded-xl px-4 py-2 text-sm text-[#888] hover:text-[#ececec]"
              >
                Edit
              </button>
              <button
                onClick={handleSave}
                disabled={saving}
                className="rounded-xl bg-emerald-600 px-6 py-2.5 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-40"
              >
                {saving ? "Saving..." : "Save Profile"}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
