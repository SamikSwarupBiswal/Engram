import { useState, useEffect, useCallback, useRef } from "react";
import { api } from "../../lib/api";

interface ModelDownloadBarProps {
  onComplete: () => void;
  visible?: boolean;
}

type DownloadState = "checking" | "downloading" | "loading" | "ready" | "error";

export function ModelDownloadBar({ onComplete, visible = true }: ModelDownloadBarProps) {
  const [state, setState] = useState<DownloadState>("checking");
  const [progress, setProgress] = useState(0);
  const [statusText, setStatusText] = useState("Checking model status...");
  const [error, setError] = useState<string | null>(null);
  const pollIntervalRef = useRef<number | null>(null);

  const stopPolling = () => {
    if (pollIntervalRef.current !== null) {
      window.clearInterval(pollIntervalRef.current);
      pollIntervalRef.current = null;
    }
  };

  const pollDownload = useCallback(() => {
    if (pollIntervalRef.current !== null) return;

    pollIntervalRef.current = window.setInterval(async () => {
      try {
        const status = await api.modelStatus();
        if (status.downloadError) {
          stopPolling();
          setState("error");
          setError(status.downloadError);
          return;
        }

        if (status.isReady) {
          stopPolling();
          setState("ready");
          onComplete();
          return;
        }

        if (status.state === "Ready") {
          stopPolling();
          setProgress(100);
          setStatusText("Download complete. Loading model...");
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

        if (status.sizeBytes > 0 || status.progress > 0) {
          const percent = Math.min(99, status.progress * 100);
          const mb = Math.round(status.sizeBytes / (1024 * 1024));
          setProgress(percent);
          setStatusText(mb > 0 ? `Downloading... ${mb} MB` : "Downloading...");
        }
      } catch {
        // API may be busy while the model is downloading or loading.
      }
    }, 2000);
  }, [onComplete]);

  const startDownload = useCallback(async () => {
    setState("downloading");
    setProgress(0);
    setStatusText("Starting download...");
    setError(null);

    try {
      await api.downloadModel();
      pollDownload();
    } catch {
      setState("error");
      setError("Download failed. Check your internet connection.");
    }
  }, [pollDownload]);

  const checkModel = useCallback(async () => {
    try {
      const status = await api.modelStatus();
      if (status.isReady) {
        stopPolling();
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

      if (status.downloadError) {
        stopPolling();
        setState("error");
        setError(status.downloadError);
        return;
      }

      if (status.downloadInProgress || status.state === "PartialDownload") {
        setState("downloading");
        setProgress(Math.min(99, status.progress * 100));
        if (status.sizeBytes > 0) {
          const mb = Math.round(status.sizeBytes / (1024 * 1024));
          setStatusText(`Downloading... ${mb} MB`);
        } else {
          setStatusText("Downloading...");
        }
        pollDownload();
        return;
      }

      void startDownload();
    } catch {
      setState("error");
      setError("Cannot connect to API");
    }
  }, [onComplete, pollDownload, startDownload]);

  useEffect(() => {
    void checkModel();
    return () => stopPolling();
  }, [checkModel]);

  if (!visible || state === "ready") return null;

  return (
    <div className="border-t border-white/[0.06] bg-[#1a1a1a] px-4 py-3">
      <div className="mx-auto max-w-[48rem]">
        {state === "checking" && (
          <div className="flex items-center gap-3">
            <div className="h-2 w-2 animate-pulse rounded-full bg-yellow-500" />
            <span className="text-[13px] text-[#b4b4b4]">{statusText}</span>
          </div>
        )}

        {state === "downloading" && (
          <div>
            <div className="mb-2 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <div className="h-2 w-2 animate-pulse rounded-full bg-emerald-500" />
                <span className="text-[13px] text-[#ececec]">Engram</span>
                <span className="text-[11px] text-[#888]">- {statusText}</span>
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

        {state === "loading" && (
          <div className="flex items-center gap-3">
            <div className="h-2 w-2 animate-pulse rounded-full bg-blue-500" />
            <span className="text-[13px] text-[#ececec]">Engram</span>
            <span className="text-[11px] text-[#888]">- {statusText}</span>
            <div className="ml-auto flex gap-1">
              <div className="h-1 w-1 animate-bounce rounded-full bg-blue-400" style={{ animationDelay: "0ms" }} />
              <div className="h-1 w-1 animate-bounce rounded-full bg-blue-400" style={{ animationDelay: "150ms" }} />
              <div className="h-1 w-1 animate-bounce rounded-full bg-blue-400" style={{ animationDelay: "300ms" }} />
            </div>
          </div>
        )}

        {state === "error" && (
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="h-2 w-2 rounded-full bg-red-500" />
              <span className="text-[13px] text-red-400">{error}</span>
            </div>
            <button
              onClick={() => void startDownload()}
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
