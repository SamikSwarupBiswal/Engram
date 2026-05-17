import { useState, useRef, useEffect, useCallback } from "react";

interface Message {
  id: string;
  role: "user" | "assistant";
  content: string;
  timestamp: string;
}

import { api } from "../../lib/api";

const WELCOME_MESSAGE: Message = {
  id: "welcome",
  role: "assistant",
  content:
    "Welcome to Engram. I'm your personal semantic memory layer. I can help you search your wiki, generate briefs, or just chat about what's on your mind. What would you like to do?",
  timestamp: new Date().toISOString(),
};

function getSessionKey(sessionId: string | null) {
  return `engram-chat-${sessionId || "default"}`;
}

function loadMessages(sessionId: string | null): Message[] {
  try {
    const stored = localStorage.getItem(getSessionKey(sessionId));
    if (stored) {
      const parsed = JSON.parse(stored) as Message[];
      if (parsed.length > 0) return parsed;
    }
  } catch {}
  return [WELCOME_MESSAGE];
}

function saveMessages(sessionId: string | null, messages: Message[]) {
  try {
    localStorage.setItem(getSessionKey(sessionId), JSON.stringify(messages.slice(-200)));
  } catch {}
}

interface ChatPanelProps {
  sessionId: string | null;
  onFirstMessage?: (title: string) => void;
}

export function ChatPanel({ sessionId, onFirstMessage }: ChatPanelProps) {
  const [messages, setMessages] = useState<Message[]>(() => loadMessages(sessionId));
  const [input, setInput] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const hasNotifiedRef = useRef(false);

  // Reload messages when session changes
  useEffect(() => {
    setMessages(loadMessages(sessionId));
    hasNotifiedRef.current = false;
  }, [sessionId]);

  // Persist on change
  useEffect(() => {
    saveMessages(sessionId, messages);
  }, [sessionId, messages]);

  // Auto-scroll
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  // Auto-resize textarea
  useEffect(() => {
    const el = textareaRef.current;
    if (el) {
      el.style.height = "auto";
      el.style.height = Math.min(el.scrollHeight, 128) + "px";
    }
  }, [input]);

  const handleSend = useCallback(async () => {
    if (!input.trim() || isStreaming) return;

    const userContent = input.trim();
    const userMessage: Message = {
      id: Date.now().toString(),
      role: "user",
      content: userContent,
      timestamp: new Date().toISOString(),
    };

    const updated = [...messages, userMessage];
    setMessages(updated);
    setInput("");
    setIsStreaming(true);

    // Notify parent of first user message (for session title)
    if (!hasNotifiedRef.current && onFirstMessage) {
      hasNotifiedRef.current = true;
      onFirstMessage(userContent.slice(0, 50) + (userContent.length > 50 ? "..." : ""));
    }

    try {
      const chatMessages = [
        ...updated.slice(-10).map((m) => ({ role: m.role, content: m.content })),
        { role: "user", content: userContent },
      ];

      // Check token budget before sending
      const estimatedInput = chatMessages.reduce((sum, m) => sum + m.content.length, 0) / 4;
      const tokenCheck = await api.checkTokens("gemini-flash", Math.round(estimatedInput), 500);
      if (!tokenCheck.allowed) {
        setMessages((prev) => [
          ...prev,
          {
            id: (Date.now() + 1).toString(),
            role: "assistant",
            content: `Token budget reached. ${tokenCheck.reason || "Upgrade to Pro or buy more tokens in Settings."}`,
            timestamp: new Date().toISOString(),
          },
        ]);
        return;
      }

      const data = await api.chat(chatMessages);
      const assistantContent = (data.choices?.[0]?.message as unknown as { content: string })?.content ?? "No response.";
      setMessages((prev) => [
        ...prev,
        {
          id: (Date.now() + 1).toString(),
          role: "assistant",
          content: assistantContent,
          timestamp: new Date().toISOString(),
        },
      ]);
    } catch (err) {
      setMessages((prev) => [
        ...prev,
        {
          id: (Date.now() + 1).toString(),
          role: "assistant",
          content: "I'm getting ready! The model is still loading. Try again in a moment.",
          timestamp: new Date().toISOString(),
        },
      ]);
    } finally {
      setIsStreaming(false);
    }
  }, [input, isStreaming, messages, onFirstMessage]);

  const formatTime = (ts: string) => {
    try {
      return new Date(ts).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    } catch {
      return "";
    }
  };

  return (
    <div className="flex h-full flex-col bg-[#212121]">
      {/* Messages */}
      <div className="flex-1 overflow-y-auto">
        <div className="mx-auto max-w-[48rem] space-y-4 px-4 py-6">
          {messages.map((msg) => (
            <div key={msg.id} className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}>
              <div
                className={`max-w-[85%] rounded-3xl px-5 py-3 text-[15px] leading-relaxed ${
                  msg.role === "user"
                    ? "bg-[#2f2f2f] text-[#ececec]"
                    : "text-[#ececec]"
                }`}
              >
                <div className="whitespace-pre-wrap">{msg.content}</div>
                <div className={`mt-1.5 text-[11px] ${msg.role === "user" ? "text-[#888]" : "text-[#666]"}`}>
                  {formatTime(msg.timestamp)}
                </div>
              </div>
            </div>
          ))}
          {isStreaming && (
            <div className="flex justify-start">
              <div className="rounded-3xl px-5 py-3 text-[15px] text-[#888]">
                <span className="animate-pulse">Thinking...</span>
              </div>
            </div>
          )}
          <div ref={messagesEndRef} />
        </div>
      </div>

      {/* Input */}
      <div className="border-t border-white/[0.04] bg-[#212121] p-4">
        <div className="mx-auto max-w-[48rem]">
          <div className="flex items-end gap-2 rounded-3xl border border-white/[0.08] bg-[#2f2f2f] px-4 py-3 focus-within:border-white/[0.2]">
            <textarea
              ref={textareaRef}
              className="max-h-32 min-h-[1.5rem] flex-1 resize-none bg-transparent text-[15px] text-[#ececec] placeholder:text-[#888] focus:outline-none"
              placeholder="Message Engram"
              rows={1}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  handleSend();
                }
              }}
            />
            <button
              onClick={handleSend}
              disabled={!input.trim() || isStreaming}
              className="flex h-8 w-8 items-center justify-center rounded-full bg-white/[0.1] text-[#ececec] hover:bg-white/[0.2] disabled:opacity-30"
            >
              <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
                <path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z" />
              </svg>
            </button>
          </div>
          <div className="mt-2 text-center text-[11px] text-[#666]">
            Engram can make mistakes. Check important info.
          </div>
        </div>
      </div>
    </div>
  );
}
