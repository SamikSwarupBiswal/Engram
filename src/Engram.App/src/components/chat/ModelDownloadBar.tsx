import { useState, useEffect, useCallback } from "react";
import { api, type HealthResponse } from "../../lib/api";

interface ModelDownloadBarProps {
  onComplete: () => void;
  visible?: boolean;
  health: HealthResponse | null;
}

/**
 * ModelDownloadBar now reads from the unified /health endpoint.
 * It does NOT independently poll model status — that was the old fragmented approach.
 * 
 * State is RENDERED from backend truth, not INFERRED by frontend.
 */
export function ModelDownloadBar({ onComplete, visible = true, health }: ModelDownloadBarProps) {
  const [error, setError] = useState<string | null>(null);
  const [retrying, setRetrying] = useState(false);

  // React to lifecycle state changes
  useEffect(() => {
    if (!health) return;

    // Model is ready — signal completion
    if (health.isReady) {
      onComplete();
      return;
    }

    // Clear error when we're no longer in error state
    if (health.state !== "Error") {
      setError(null);
    }

    // Surface backend errors
    if (health.state === "Error" && health.error) {
      setError(health.error);
    }
  }, [health, onComplete]);

  const handleRetry = useCallback(async () => {
    setRetrying(true);
    setError(null);
    try {
      // Call lifecycle retry endpoint
      await fetch(`${"http://127.0.0.1:5000"}/api/health/retry`, { method: "POST" });
    } catch {
      setError("Retry failed — API not responding");
    } finally {
      setRetrying(false);
    }
  }, []);

  const handleLoadModel = useCallback(async () => {
    try {
      const result = await api.loadModel();
      if (result.loaded) {
        onComplete();
      } else {
        setError("Model download complete but load failed. Check GPU drivers.");
      }
    } catch {
      setError("Failed to load model");
    }
  }, [onComplete]);

  if (!visible || !health || health.isReady) return null;

  const state = health.state;
  const progress = health.progress;

  return (
    <div className="border-t border-white/[0.06] bg-[#1a1a1a] px-4 py-3">
      <div className="mx-auto max-w-[48rem]">
        {/* Starting / Detecting Backend */}
        {(state === "Starting" || state === "DetectingBackend") && (
          <div className="flex items-center gap-3">
            <div className="h-2 w-2 animate-pulse rounded-full bg-yellow-500" />
            <span className="text-[13px] text-[#b4b4b4]">
              {state === "Starting" ? "Starting Engram..." : `Detecting GPU backend...`}
            </span>
            <div className="ml-auto flex gap-1">
              <div className="h-1 w-1 animate-bounce rounded-full bg-yellow-400" style={{ animationDelay: "0ms" }} />
              <div className="h-1 w-1 animate-bounce rounded-full bg-yellow-400" style={{ animationDelay: "150ms" }} />
              <div className="h-1 w-1 animate-bounce rounded-full bg-yellow-400" style={{ animationDelay: "300ms" }} />
            </div>
          </div>
        )}

        {/* Backend Ready — waiting for model */}
        {state === "BackendReady" && (
          <div className="flex items-center gap-3">
            <div className="h-2 w-2 rounded-full bg-cyan-500" />
            <span className="text-[13px] text-[#ececec]">Engram</span>
            <span className="text-[11px] text-[#888]">
              — {health.backend} backend ready. Preparing model...
            </span>
          </div>
        )}

        {/* Downloading Model */}
        {state === "DownloadingModel" && (
          <div>
            <div className="mb-2 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <div className="h-2 w-2 animate-pulse rounded-full bg-emerald-500" />
                <span className="text-[13px] text-[#ececec]">Engram</span>
                <span className="text-[11px] text-[#888]">— Downloading model...</span>
              </div>
              <span className="text-[12px] font-medium text-emerald-400">{Math.round(progress)}%</span>
            </div>
            <div className="h-1.5 w-full overflow-hidden rounded-full bg-white/[0.06]">
              <div
                className="h-full rounded-full bg-gradient-to-r from-emerald-600 to-emerald-400 transition-all duration-500 ease-out"
                style={{ width: `${progress}%` }}
              />
            </div>
          </div>
        )}

        {/* Loading Model into Memory */}
        {state === "LoadingModel" && (
          <div className="flex items-center gap-3">
            <div className="h-2 w-2 animate-pulse rounded-full bg-blue-500" />
            <span className="text-[13px] text-[#ececec]">Engram</span>
            <span className="text-[11px] text-[#888]">
              — Loading model into {health.backend || "GPU"} memory...
            </span>
            <div className="ml-auto flex gap-1">
              <div className="h-1 w-1 animate-bounce rounded-full bg-blue-400" style={{ animationDelay: "0ms" }} />
              <div className="h-1 w-1 animate-bounce rounded-full bg-blue-400" style={{ animationDelay: "150ms" }} />
              <div className="h-1 w-1 animate-bounce rounded-full bg-blue-400" style={{ animationDelay: "300ms" }} />
            </div>
          </div>
        )}

        {/* Error State */}
        {state === "Error" && (
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="h-2 w-2 rounded-full bg-red-500" />
              <span className="text-[13px] text-red-400">{error || health.error || "Unknown error"}</span>
            </div>
            <div className="flex gap-2">
              <button
                onClick={handleRetry}
                disabled={retrying}
                className="rounded-lg border border-white/[0.08] px-3 py-1 text-[12px] text-[#b4b4b4] hover:bg-white/[0.06] disabled:opacity-50"
              >
                {retrying ? "Retrying..." : "Retry"}
              </button>
            </div>
          </div>
        )}

        {/* Degraded — CPU fallback */}
        {state === "Degraded" && (
          <div className="flex items-center gap-3">
            <div className="h-2 w-2 rounded-full bg-orange-500" />
            <span className="text-[13px] text-orange-400">
              GPU unavailable — running on CPU (slower)
            </span>
            <button
              onClick={handleLoadModel}
              className="ml-auto rounded-lg border border-white/[0.08] px-3 py-1 text-[12px] text-[#b4b4b4] hover:bg-white/[0.06]"
            >
              Load Model
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
