import { useState, useEffect, useCallback } from "react";
import { api } from "../../lib/api";

interface ModelDownloadBarProps {
  onComplete: () => void;
}

type DownloadState = "checking" | "not-downloaded" | "downloading" | "loading" | "ready" | "error";

export function ModelDownloadBar({ onComplete }: ModelDownloadBarProps) {
  const [state, setState] = useState<DownloadState>("checking");
  const [progress, setProgress] = useState(0);
  const [statusText, setStatusText] = useState("Checking model status...");
  const [error, setError] = useState<string | null>(null);

  // Check model status on mount
  useEffect(() => {
    checkModel();
  }, []);

  const checkModel = async () => {
    try {
      const status = await api.modelStatus();
      if (status.isReady) {
        setState("ready");
        onComplete();
        return;
      }
      if (status.isLoading) {
        setState("loading");
        setStatusText("Loading model into memory...");
        return;
      }
      if (status.state === "Ready") {
        // Model downloaded but not loaded — load it
        setState("loading");
        setStatusText("Loading model into memory...");
        const result = await api.loadModel();
        if (result.loaded) {
          setState("ready");
          onComplete();
        } else {
          setState("error");
          setError("Failed to load model");
        }
        return;
      }
      // Model not downloaded — auto-start download
      setState("downloading");
      setStatusText("Starting download...");
      startDownload();
    } catch {
      setState("error");
      setError("Cannot connect to API");
    }
  };

  const startDownload = useCallback(async () => {
    setState("downloading");
    setProgress(0);
    setStatusText("Starting download...");
    setError(null);

    try {
      await api.downloadModel();

      // Poll for progress
      const pollInterval = setInterval(async () => {
        try {
          const status = await api.modelStatus();
          if (status.state === "Ready") {
            clearInterval(pollInterval);
            setProgress(100);
            setStatusText("Download complete! Loading model...");

            // Auto-load after download
            setState("loading");
            const result = await api.loadModel();
            if (result.loaded) {
              setState("ready");
              onComplete();
            } else {
              setState("error");
              setError("Downloaded but failed to load");
            }
            return;
          }

          // Estimate progress from file size
          if (status.sizeBytes > 0) {
            const percent = Math.min(99, (status.sizeBytes / (2.3 * 1024 * 1024 * 1024)) * 100);
            setProgress(percent);
            const mb = Math.round(status.sizeBytes / (1024 * 1024));
            setStatusText(`Downloading... ${mb} MB`);
          }
        } catch {
          // API might be busy
        }
      }, 2000);

      // Cleanup on unmount
      return () => clearInterval(pollInterval);
    } catch (err) {
      setState("error");
      setError("Download failed. Check your internet connection.");
    }
  }, [onComplete]);

  // Don't show if ready
  if (state === "ready") return null;

  return (
    <div className="border-t border-white/[0.06] bg-[#1a1a1a] px-4 py-3">
      <div className="mx-auto max-w-[48rem]">
        {/* Checking state */}
        {state === "checking" && (
          <div className="flex items-center gap-3">
            <div className="h-2 w-2 animate-pulse rounded-full bg-yellow-500" />
            <span className="text-[13px] text-[#b4b4b4]">{statusText}</span>
          </div>
        )}

        {/* Not downloaded — auto-downloading */}
        {state === "not-downloaded" && (
          <div className="flex items-center gap-3">
            <div className="h-2 w-2 animate-pulse rounded-full bg-emerald-500" />
            <span className="text-[13px] text-[#ececec]">Engram</span>
            <span className="text-[11px] text-[#888]">— Preparing model download...</span>
          </div>
        )}

        {/* Downloading — progress bar */}
        {state === "downloading" && (
          <div>
            <div className="mb-2 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <div className="h-2 w-2 animate-pulse rounded-full bg-emerald-500" />
                <span className="text-[13px] text-[#ececec]">Engram</span>
                <span className="text-[11px] text-[#888]">— {statusText}</span>
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

        {/* Loading into memory */}
        {state === "loading" && (
          <div className="flex items-center gap-3">
            <div className="h-2 w-2 animate-pulse rounded-full bg-blue-500" />
            <span className="text-[13px] text-[#ececec]">Engram</span>
            <span className="text-[11px] text-[#888]">— {statusText}</span>
            <div className="ml-auto flex gap-1">
              <div className="h-1 w-1 animate-bounce rounded-full bg-blue-400" style={{ animationDelay: "0ms" }} />
              <div className="h-1 w-1 animate-bounce rounded-full bg-blue-400" style={{ animationDelay: "150ms" }} />
              <div className="h-1 w-1 animate-bounce rounded-full bg-blue-400" style={{ animationDelay: "300ms" }} />
            </div>
          </div>
        )}

        {/* Error */}
        {state === "error" && (
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="h-2 w-2 rounded-full bg-red-500" />
              <span className="text-[13px] text-red-400">{error}</span>
            </div>
            <button
              onClick={state === "error" ? startDownload : checkModel}
              className="rounded-lg border border-white/[0.08] px-3 py-1 text-[12px] text-[#b4b4b4] hover:bg-white/[0.06]"
            >
              Retry
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
