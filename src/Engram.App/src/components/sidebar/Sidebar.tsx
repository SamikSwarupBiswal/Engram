import { useState } from "react";
import type { View } from "../../App";

export interface ChatSession {
  id: string;
  title: string;
  lastMessage: string;
  timestamp: string;
}

interface SidebarProps {
  activeView: View;
  onViewChange: (view: View) => void;
  collapsed: boolean;
  onToggleCollapse: () => void;
  sessions: ChatSession[];
  activeSessionId: string | null;
  onNewChat: () => void;
  onSelectSession: (id: string) => void;
  onDeleteSession: (id: string) => void;
}

export function Sidebar({
  activeView,
  onViewChange,
  collapsed,
  onToggleCollapse,
  sessions,
  activeSessionId,
  onNewChat,
  onSelectSession,
  onDeleteSession,
}: SidebarProps) {
  const [showMore, setShowMore] = useState(false);
  const [hoveredSession, setHoveredSession] = useState<string | null>(null);

  if (collapsed) {
    return (
      <aside className="flex w-[52px] flex-col items-center border-r border-white/[0.06] bg-[#171717] py-2">
        <button
          onClick={onToggleCollapse}
          className="flex h-9 w-9 items-center justify-center rounded-lg text-[#b4b4b4] hover:bg-white/[0.08]"
          title="Open sidebar"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <rect x="3" y="3" width="18" height="18" rx="2" />
            <line x1="9" y1="3" x2="9" y2="21" />
          </svg>
        </button>
        <button
          onClick={onNewChat}
          className="mt-2 flex h-9 w-9 items-center justify-center rounded-lg text-[#b4b4b4] hover:bg-white/[0.08]"
          title="New chat"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M12 5v14M5 12h14" />
          </svg>
        </button>
      </aside>
    );
  }

  // Group sessions by time
  const today = sessions.filter(s => isToday(s.timestamp));
  const yesterday = sessions.filter(s => isYesterday(s.timestamp));
  const older = sessions.filter(s => !isToday(s.timestamp) && !isYesterday(s.timestamp));

  return (
    <aside className="flex w-[260px] flex-col bg-[#171717] text-sm">
      {/* Header */}
      <div className="flex items-center justify-between px-3 pt-2 pb-1">
        <button
          onClick={onToggleCollapse}
          className="flex h-9 w-9 items-center justify-center rounded-lg text-[#b4b4b4] hover:bg-white/[0.08]"
          title="Close sidebar"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <rect x="3" y="3" width="18" height="18" rx="2" />
            <line x1="9" y1="3" x2="9" y2="21" />
          </svg>
        </button>
      </div>

      {/* New Chat */}
      <div className="px-2 pb-1">
        <button
          onClick={onNewChat}
          className="flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-[#ececec] hover:bg-white/[0.08]"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <rect x="3" y="3" width="18" height="18" rx="2" />
            <path d="M12 8v8M8 12h8" />
          </svg>
          New chat
        </button>
      </div>

      {/* Search */}
      <div className="px-2 pb-1">
        <button className="flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-[#b4b4b4] hover:bg-white/[0.08]">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="11" cy="11" r="8" />
            <line x1="21" y1="21" x2="16.65" y2="16.65" />
          </svg>
          Search chats
        </button>
      </div>

      {/* More menu */}
      <div className="px-2 pb-2">
        <button
          onClick={() => setShowMore(!showMore)}
          className="flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-[#b4b4b4] hover:bg-white/[0.08]"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="12" cy="5" r="1" fill="currentColor" />
            <circle cx="12" cy="12" r="1" fill="currentColor" />
            <circle cx="12" cy="19" r="1" fill="currentColor" />
          </svg>
          More
          <svg
            width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
            className={`ml-auto transition-transform ${showMore ? "rotate-180" : ""}`}
          >
            <polyline points="6 9 12 15 18 9" />
          </svg>
        </button>

        {showMore && (
          <div className="ml-2 mt-0.5 space-y-0.5">
            {[
              { id: "search" as View, label: "Search Memory", icon: "🔍" },
              { id: "wiki" as View, label: "Wiki", icon: "📚" },
              { id: "timeline" as View, label: "Timeline", icon: "📅" },
              { id: "archive" as View, label: "Archive", icon: "🗄️" },
              { id: "research" as View, label: "Research", icon: "🔬" },
              { id: "automation" as View, label: "Automation", icon: "🤖" },
              { id: "governance" as View, label: "Governance & Safety", icon: "🛡️" },
            ].map((item) => (
              <button
                key={item.id}
                onClick={() => { onViewChange(item.id); setShowMore(false); }}
                className={`flex w-full items-center gap-3 rounded-lg px-3 py-2 ${
                  activeView === item.id
                    ? "bg-white/[0.1] text-[#ececec]"
                    : "text-[#b4b4b4] hover:bg-white/[0.06]"
                }`}
              >
                <span className="text-sm">{item.icon}</span>
                {item.label}
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="mx-3 border-t border-white/[0.06]" />

      {/* Chat History */}
      <div className="flex-1 overflow-y-auto px-2 py-2">
        {sessions.length === 0 ? (
          <div className="px-3 py-4 text-center text-xs text-[#666]">
            No conversations yet
          </div>
        ) : (
          <>
            {today.length > 0 && (
              <SessionGroup
                label="Today"
                sessions={today}
                activeSessionId={activeSessionId}
                hoveredSession={hoveredSession}
                onSelect={onSelectSession}
                onDelete={onDeleteSession}
                onHover={setHoveredSession}
              />
            )}
            {yesterday.length > 0 && (
              <SessionGroup
                label="Yesterday"
                sessions={yesterday}
                activeSessionId={activeSessionId}
                hoveredSession={hoveredSession}
                onSelect={onSelectSession}
                onDelete={onDeleteSession}
                onHover={setHoveredSession}
              />
            )}
            {older.length > 0 && (
              <SessionGroup
                label="Previous 7 days"
                sessions={older}
                activeSessionId={activeSessionId}
                hoveredSession={hoveredSession}
                onSelect={onSelectSession}
                onDelete={onDeleteSession}
                onHover={setHoveredSession}
              />
            )}
          </>
        )}
      </div>

      {/* User Profile Footer */}
      <button
        onClick={() => onViewChange("settings")}
        className="flex items-center gap-3 border-t border-white/[0.06] px-3 py-3 hover:bg-white/[0.06]"
      >
        <div className="flex h-8 w-8 items-center justify-center rounded-full bg-[#303030] text-xs font-medium text-white">
          SB
        </div>
        <div className="flex-1 text-left">
          <div className="text-[13px] text-[#ececec]">Samik Swarup Biswal</div>
          <div className="text-[11px] text-[#b4b4b4]">Free Tier</div>
        </div>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#b4b4b4" strokeWidth="2">
          <circle cx="12" cy="12" r="3" />
          <path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-2 2 2 2 0 01-2-2v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83 0 2 2 0 010-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 01-2-2 2 2 0 012-2h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 010-2.83 2 2 0 012.83 0l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 012-2 2 2 0 012 2v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 0 2 2 0 010 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 012 2 2 2 0 01-2 2h-.09a1.65 1.65 0 00-1.51 1z" />
        </svg>
      </button>
    </aside>
  );
}

function SessionGroup({ label, sessions, activeSessionId, hoveredSession, onSelect, onDelete, onHover }: {
  label: string;
  sessions: ChatSession[];
  activeSessionId: string | null;
  hoveredSession: string | null;
  onSelect: (id: string) => void;
  onDelete: (id: string) => void;
  onHover: (id: string | null) => void;
}) {
  return (
    <div className="mb-1">
      <div className="px-3 py-1.5 text-[11px] font-medium text-[#888]">{label}</div>
      {sessions.map((session) => (
        <div
          key={session.id}
          className="group relative"
          onMouseEnter={() => onHover(session.id)}
          onMouseLeave={() => onHover(null)}
        >
          <button
            onClick={() => onSelect(session.id)}
            className={`flex w-full items-center rounded-lg px-3 py-2 text-left ${
              activeSessionId === session.id
                ? "bg-white/[0.1] text-[#ececec]"
                : "text-[#b4b4b4] hover:bg-white/[0.06]"
            }`}
          >
            <span className="flex-1 truncate text-[13px]">{session.title}</span>
          </button>
          {hoveredSession === session.id && (
            <button
              onClick={(e) => { e.stopPropagation(); onDelete(session.id); }}
              className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-[#888] hover:text-[#ececec]"
              title="Delete"
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <polyline points="3 6 5 6 21 6" />
                <path d="M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2" />
              </svg>
            </button>
          )}
        </div>
      ))}
    </div>
  );
}

function isToday(ts: string): boolean {
  const d = new Date(ts);
  const now = new Date();
  return d.toDateString() === now.toDateString();
}

function isYesterday(ts: string): boolean {
  const d = new Date(ts);
  const yesterday = new Date();
  yesterday.setDate(yesterday.getDate() - 1);
  return d.toDateString() === yesterday.toDateString();
}
