import { useState, useEffect } from "react";
import { api } from "../../lib/api";

type GwsStatus = { isAuthenticated: boolean; email: string | null; scopes: string[] };
type EmailMeta = { messageId: string; from: string; subject: string; date: string; snippet: string };
type EventMeta = { eventId: string; title: string; startTime: string; endTime: string; attendees: string[]; location: string };
type FileMeta = { fileId: string; name: string; mimeType: string; modifiedTime: string; sizeBytes: number; owner: string };

export function GoogleWorkspacePanel() {
  const [status, setStatus] = useState<GwsStatus | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [syncResult, setSyncResult] = useState<{ emailCount: number; eventCount: number; fileCount: number } | null>(null);
  const [emails, setEmails] = useState<EmailMeta[]>([]);
  const [events, setEvents] = useState<EventMeta[]>([]);
  const [files, setFiles] = useState<FileMeta[]>([]);
  const [activeTab, setActiveTab] = useState<"emails" | "events" | "files">("emails");
  const [clientId, setClientId] = useState("");
  const [clientSecret, setClientSecret] = useState("");
  const [authCode, setAuthCode] = useState("");
  const [showConnect, setShowConnect] = useState(false);

  useEffect(() => {
    api.gwsStatus().then(setStatus).catch(() => {});
  }, []);

  const handleConnect = async () => {
    if (!clientId || !clientSecret || !authCode) return;
    try {
      const result = await fetch("http://127.0.0.1:5000/api/gws/connect", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ code: authCode, clientId, clientSecret, redirectUri: "http://localhost:5000/callback" }),
      }).then(r => r.json());
      if (result.connected) {
        setStatus({ isAuthenticated: true, email: result.email, scopes: [] });
        setShowConnect(false);
      }
    } catch {}
  };

  const handleSync = async () => {
    setSyncing(true);
    try {
      const result = await api.gwsSync();
      if (result.success) {
        setSyncResult({ emailCount: result.emailCount, eventCount: result.eventCount, fileCount: result.fileCount });
        // Load detail data
        const [e, ev, f] = await Promise.all([
          fetch("http://127.0.0.1:5000/api/gws/emails").then(r => r.json()).catch(() => ({ emails: [] })),
          fetch("http://127.0.0.1:5000/api/gws/events").then(r => r.json()).catch(() => ({ events: [] })),
          fetch("http://127.0.0.1:5000/api/gws/files").then(r => r.json()).catch(() => ({ files: [] })),
        ]);
        setEmails(e.emails || []);
        setEvents(ev.events || []);
        setFiles(f.files || []);
      }
    } catch {}
    setSyncing(false);
  };

  const handleDisconnect = async () => {
    await api.gwsDisconnect();
    setStatus({ isAuthenticated: false, email: null, scopes: [] });
    setSyncResult(null);
    setEmails([]);
    setEvents([]);
    setFiles([]);
  };

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const mimeTypeLabel = (mime: string) => {
    if (mime.includes("document")) return "Doc";
    if (mime.includes("spreadsheet")) return "Sheet";
    if (mime.includes("presentation")) return "Slides";
    if (mime.includes("pdf")) return "PDF";
    if (mime.includes("image")) return "Image";
    if (mime.includes("folder")) return "Folder";
    return "File";
  };

  return (
    <div>
      <h3 className="mb-3 text-[13px] font-medium text-[#b4b4b4]">Google Workspace</h3>
      <div className="rounded-xl border border-white/[0.06] bg-[#2f2f2f]/50 p-4">
        {/* Not connected */}
        {!status?.isAuthenticated ? (
          <div>
            <p className="text-[12px] text-[#888] mb-3">
              Connect Google to ingest email, calendar, and drive metadata. Metadata only — never content.
            </p>
            <div className="flex items-center gap-2 mb-3">
              <div className="h-2 w-2 rounded-full bg-[#888]" />
              <span className="text-[13px] text-[#b4b4b4]">Not connected</span>
            </div>
            {!showConnect ? (
              <button onClick={() => setShowConnect(true)} className="w-full rounded-lg bg-emerald-600 px-4 py-2 text-[12px] text-white hover:bg-emerald-700">
                Connect Google Account
              </button>
            ) : (
              <div className="space-y-2">
                <input type="text" value={clientId} onChange={(e) => setClientId(e.target.value)} placeholder="Client ID" className="w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-1.5 text-[12px] text-[#ececec] placeholder:text-[#666]" />
                <input type="password" value={clientSecret} onChange={(e) => setClientSecret(e.target.value)} placeholder="Client Secret" className="w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-1.5 text-[12px] text-[#ececec] placeholder:text-[#666]" />
                <input type="text" value={authCode} onChange={(e) => setAuthCode(e.target.value)} placeholder="Authorization Code" className="w-full rounded-lg border border-white/[0.08] bg-[#212121] px-3 py-1.5 text-[12px] text-[#ececec] placeholder:text-[#666]" />
                <p className="text-[10px] text-[#666]">Get auth code from Google OAuth consent screen. Redirect URI: http://localhost:5000/callback</p>
                <div className="flex gap-2">
                  <button onClick={handleConnect} className="flex-1 rounded-lg bg-emerald-600 px-3 py-1.5 text-[12px] text-white hover:bg-emerald-700">Connect</button>
                  <button onClick={() => setShowConnect(false)} className="rounded-lg border border-white/[0.08] px-3 py-1.5 text-[12px] text-[#888]">Cancel</button>
                </div>
              </div>
            )}
          </div>
        ) : (
          <div>
            {/* Connected */}
            <div className="flex items-center gap-2 mb-3">
              <div className="h-2 w-2 rounded-full bg-emerald-500" />
              <span className="text-[13px] text-[#ececec]">{status.email}</span>
            </div>

            <div className="flex gap-2 mb-3">
              <button onClick={handleSync} disabled={syncing} className="flex-1 rounded-lg bg-emerald-600 px-3 py-1.5 text-[12px] text-white hover:bg-emerald-700 disabled:opacity-50">
                {syncing ? "Syncing..." : "Sync Now"}
              </button>
              <button onClick={handleDisconnect} className="rounded-lg border border-red-500/30 px-3 py-1.5 text-[12px] text-red-400 hover:bg-red-500/10">Disconnect</button>
            </div>

            {/* Sync counts */}
            {syncResult && (
              <div className="grid grid-cols-3 gap-2 mb-3">
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

            {/* Tabs */}
            {syncResult && (
              <div>
                <div className="flex gap-1 mb-2">
                  {(["emails", "events", "files"] as const).map((tab) => (
                    <button key={tab} onClick={() => setActiveTab(tab)} className={`rounded-lg px-3 py-1 text-[11px] ${activeTab === tab ? "bg-white/[0.08] text-[#ececec]" : "text-[#888] hover:bg-white/[0.04]"}`}>
                      {tab.charAt(0).toUpperCase() + tab.slice(1)}
                    </button>
                  ))}
                </div>

                {/* Email list */}
                {activeTab === "emails" && (
                  <div className="space-y-1 max-h-60 overflow-y-auto">
                    {emails.length === 0 ? <div className="text-[11px] text-[#888] py-2">No emails synced.</div> :
                      emails.map((e) => (
                        <div key={e.messageId} className="rounded-lg bg-white/[0.02] p-2">
                          <div className="text-[11px] text-[#ececec] font-medium">{e.subject}</div>
                          <div className="text-[10px] text-[#888]">{e.from} — {e.date}</div>
                          {e.snippet && <div className="text-[10px] text-[#666] mt-0.5">{e.snippet.slice(0, 80)}...</div>}
                        </div>
                      ))
                    }
                  </div>
                )}

                {/* Event list */}
                {activeTab === "events" && (
                  <div className="space-y-1 max-h-60 overflow-y-auto">
                    {events.length === 0 ? <div className="text-[11px] text-[#888] py-2">No events synced.</div> :
                      events.map((ev) => (
                        <div key={ev.eventId} className="rounded-lg bg-white/[0.02] p-2">
                          <div className="text-[11px] text-[#ececec] font-medium">{ev.title}</div>
                          <div className="text-[10px] text-[#888]">{ev.startTime}{ev.endTime ? ` — ${ev.endTime}` : ""}</div>
                          {ev.location && <div className="text-[10px] text-[#666]">{ev.location}</div>}
                          {ev.attendees.length > 0 && <div className="text-[10px] text-[#666]">{ev.attendees.length} attendees</div>}
                        </div>
                      ))
                    }
                  </div>
                )}

                {/* File list */}
                {activeTab === "files" && (
                  <div className="space-y-1 max-h-60 overflow-y-auto">
                    {files.length === 0 ? <div className="text-[11px] text-[#888] py-2">No files synced.</div> :
                      files.map((f) => (
                        <div key={f.fileId} className="rounded-lg bg-white/[0.02] p-2 flex items-center justify-between">
                          <div>
                            <div className="text-[11px] text-[#ececec]">{f.name}</div>
                            <div className="text-[10px] text-[#888]">{mimeTypeLabel(f.mimeType)} — {f.owner}</div>
                          </div>
                          <div className="text-[10px] text-[#666]">{formatSize(f.sizeBytes)}</div>
                        </div>
                      ))
                    }
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
