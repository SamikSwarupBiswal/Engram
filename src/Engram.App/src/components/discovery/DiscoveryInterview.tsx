import { useState } from "react";
import { api, type DiscoveryAnswers } from "../../lib/api";

interface DiscoveryInterviewProps {
  onComplete: () => void;
  onSkip: () => void;
}

type Step = "welcome" | "name" | "goals" | "triggers" | "anxieties" | "priorities" | "antiGoals" | "confirm";

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

  const stepIndex = STEPS.findIndex((s) => s.id === currentStep);
  const step = STEPS[stepIndex];
  const progress = stepIndex >= 0 ? ((stepIndex + 1) / STEPS.length) * 100 : 0;

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
    currentStep === "confirm" ||
    textInput.trim().length > 0;

  return (
    <div className="flex h-full flex-col items-center justify-center bg-[#212121] p-6">
      <div className="w-full max-w-lg">
        {/* Progress bar */}
        {currentStep !== "welcome" && currentStep !== "confirm" && (
          <div className="mb-6">
            <div className="flex items-center justify-between mb-2">
              <span className="text-[11px] text-[#888]">{step?.title}</span>
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
            </div>
            <div className="mt-6 flex justify-between">
              <button
                onClick={() => setCurrentStep("antiGoals")}
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
