import { useState, useEffect } from "react";
import { api } from "../../lib/api";

export function GoogleWorkspacePanel() {
  const [status, setStatus] = useState<{ isAuthenticated: boolean; email: string | null; scopes: string[] } | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [syncResult, setSyncResult] = useState<{ emailCount: number; eventCount: number; fileCount: number } | null>(null);

  useEffect(() => {
    api.gwsStatus().then(setStatus).catch(() => {});
  }, []);

  const handleSync = async () => {
    setSyncing(true);
    try {
      const result = await api.gwsSync();
      if (result.success) {
        setSyncResult({ emailCount: result.emailCount, eventCount: result.eventCount, fileCount: result.fileCount });
      }
    } catch {}
    setSyncing(false);
  };

  const handleDisconnect = async () => {
    await api.gwsDisconnect();
    setStatus({ isAuthenticated: false, email: null, scopes: [] });
    setSyncResult(null);
  };

  return (
    <div>
      <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Google Workspace</h3>
      <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
        {!status?.isAuthenticated ? (
          <div>
            <p className="text-[12px] text-[#888] mb-3">
              Connect your Google account to ingest email, calendar, and drive metadata.
              Engram reads metadata only — never email bodies or file contents.
            </p>
            <div className="flex items-center gap-2 mb-3">
              <div className="h-2 w-2 rounded-full bg-[#888]" />
              <span className="text-[13px] text-[#b4b4b4]">Not connected</span>
            </div>
            <p className="text-[11px] text-[#666]">
              To connect: set up Google OAuth credentials in google-credentials.json,
              then use the API endpoint /api/gws/connect with your auth code.
            </p>
          </div>
        ) : (
          <div>
            <div className="flex items-center gap-2 mb-3">
              <div className="h-2 w-2 rounded-full bg-emerald-500" />
              <span className="text-[13px] text-[#ececec]">{status.email}</span>
            </div>

            <div className="flex gap-2 mb-3">
              <button
                onClick={handleSync}
                disabled={syncing}
                className="flex-1 rounded-lg bg-emerald-600 px-3 py-1.5 text-[12px] text-white hover:bg-emerald-700 disabled:opacity-50"
              >
                {syncing ? "Syncing..." : "Sync Now"}
              </button>
              <button
                onClick={handleDisconnect}
                className="rounded-lg border border-red-500/30 px-3 py-1.5 text-[12px] text-red-400 hover:bg-red-500/10"
              >
                Disconnect
              </button>
            </div>

            {syncResult && (
              <div className="grid grid-cols-3 gap-2">
                <div className="rounded-lg bg-white/[0.04] p-2 text-center">
                  <div className="text-sm font-medium">{syncResult.emailCount}</div>
                  <div className="text-[10px] text-[#888]">Emails</div>
                </div>
                <div className="rounded-lg bg-white/[0.04] p-2 text-center">
                  <div className="text-sm font-medium">{syncResult.eventCount}</div>
                  <div className="text-[10px] text-[#888]">Events</div>
                </div>
                <div className="rounded-lg bg-white/[0.04] p-2 text-center">
                  <div className="text-sm font-medium">{syncResult.fileCount}</div>
                  <div className="text-[10px] text-[#888]">Files</div>
                </div>
              </div>
            )}

            <div className="mt-3 text-[11px] text-[#666]">
              Scopes: {status.scopes.map(s => s.split("/").pop()).join(", ")}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
