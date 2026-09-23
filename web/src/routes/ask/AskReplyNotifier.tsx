import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { unreadSessionCount, type AskReplyEvent } from "./askSessions";
import { useAskSessionStore, useAskSessionsSnapshot } from "./AskSessionsContext";
import "./replyNotifier.css";

interface ReplyToast {
  id: number;
  conversationId: string;
  title: string | null;
  ok: boolean;
}

const TOAST_LIFETIME_MS = 8000;
const MAX_TOASTS = 3;
const TITLE_BADGE = /^\(\d+\) /;

let toastCounter = 0;

function tabIsAway(): boolean {
  return document.visibilityState === "hidden" || !document.hasFocus();
}

/**
 * Asks, once, for permission to show system notifications. Called from the Ask composer's own
 * send handler -- browsers only honour the request inside a user gesture -- and a no-op wherever
 * the Notification API is missing or the user has already answered.
 */
export function requestReplyNotificationPermission(): void {
  if (typeof Notification === "undefined" || Notification.permission !== "default") return;
  try {
    void Notification.requestPermission()?.catch(() => undefined);
  } catch {
    // Older Safari only supports the callback form; the in-app notice below still works.
  }
}

function showSystemNotification(event: AskReplyEvent, open: () => void): void {
  if (typeof Notification === "undefined" || Notification.permission !== "granted") return;
  try {
    const notification = new Notification(event.ok ? "Raffa.ai answered" : "Raffa.ai could not answer", {
      body: event.title ?? "Your question in Ask Raffa",
      tag: `raffa-ask-${event.conversationId}`,
    });
    notification.onclick = () => {
      window.focus();
      open();
      notification.close();
    };
  } catch {
    // Some mobile browsers only allow notifications from a service worker.
  }
}

/**
 * "Your reply is ready" for Ask sessions running in the background (`askSessions.ts`). When a reply
 * lands in a chat the user is not looking at, three things happen, the same way a background
 * session reports back in Claude Code:
 *
 * - an in-app notice (top right) names the chat and opens it in one click. It goes away on its own,
 *   on "Dismiss", or as soon as that chat is opened some other way (the rail);
 * - a system notification, when the tab is hidden or the window unfocused and the user has allowed
 *   them -- then also for the chat left on screen, since the user is not looking at it;
 * - the tab title counts the chats with an unread reply -- `(2) Raffa.ai` -- until they are opened.
 *
 * The rail highlights the same chats (`RailNav.tsx`), so the signal survives the notice timing out.
 */
export default function AskReplyNotifier() {
  const store = useAskSessionStore();
  const snapshot = useAskSessionsSnapshot(store);
  const navigate = useNavigate();
  const [toasts, setToasts] = useState<readonly ReplyToast[]>([]);
  const [tabVisible, setTabVisible] = useState(() => document.visibilityState !== "hidden");

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const openConversation = useCallback(
    (conversationId: string) => {
      setToasts((current) => current.filter((toast) => toast.conversationId !== conversationId));
      navigate(`/ask/${conversationId}`);
    },
    [navigate],
  );

  useEffect(
    () =>
      store.onReply((event) => {
        const conversationId = event.conversationId;
        if (conversationId === null) return;
        // The user is in another tab or app: tell them even about the chat left on screen.
        if (tabIsAway()) showSystemNotification(event, () => openConversation(conversationId));
        if (event.seen) return;
        toastCounter += 1;
        const toast: ReplyToast = { id: toastCounter, conversationId, title: event.title, ok: event.ok };
        setToasts((current) => [...current.filter((existing) => existing.conversationId !== conversationId), toast].slice(-MAX_TOASTS));
      }),
    [store, openConversation],
  );

  useEffect(() => {
    const onVisibilityChange = () => setTabVisible(document.visibilityState !== "hidden");
    document.addEventListener("visibilitychange", onVisibilityChange);
    return () => document.removeEventListener("visibilitychange", onVisibilityChange);
  }, []);

  // A notice only starts counting down while the tab is visible, so one that arrived while the
  // user was away is still there when they come back.
  useEffect(() => {
    if (!tabVisible || toasts.length === 0) return;
    const timers = toasts.map((toast) => window.setTimeout(() => dismiss(toast.id), TOAST_LIFETIME_MS));
    return () => timers.forEach((timer) => window.clearTimeout(timer));
  }, [toasts, tabVisible, dismiss]);

  const unread = unreadSessionCount(snapshot);
  useEffect(() => {
    const base = document.title.replace(TITLE_BADGE, "");
    document.title = unread > 0 ? `(${unread}) ${base}` : base;
  }, [unread]);
  useEffect(
    () => () => {
      document.title = document.title.replace(TITLE_BADGE, "");
    },
    [],
  );

  // Only chats still unread: opening one from the rail (or reading it on return to the tab) clears
  // its notice too.
  const visibleToasts = toasts.filter((toast) => snapshot.sessions.get(toast.conversationId)?.unread === true);

  return (
    <div className="ask-reply-toasts" role="status" aria-live="polite">
      {visibleToasts.map((toast) => (
        <div key={toast.id} className="ask-reply-toast">
          <div className="ask-reply-toast-kicker">{toast.ok ? "Reply ready" : "Reply failed"}</div>
          <div className="ask-reply-toast-title">{toast.title ?? "Ask Raffa"}</div>
          <div className="ask-reply-toast-actions">
            <button type="button" className="btn btn-secondary ask-reply-toast-open" onClick={() => openConversation(toast.conversationId)}>
              Open chat
            </button>
            <button type="button" className="btn btn-ghost ask-reply-toast-dismiss" onClick={() => dismiss(toast.id)}>
              Dismiss
            </button>
          </div>
        </div>
      ))}
    </div>
  );
}
