import type { ApiClient } from "../../api/client";
import type { FeedbackAnswers } from "./reply/replyTypes";
import { CONVERSATION_NAME_MAX_LENGTH, normalizeConversationName } from "./conversationTitle";
import {
  TRANSPORT_ERROR_REASON,
  buildErrorTurn,
  buildInterviewAnswerRequest,
  buildRaffaTurnFromMessage,
  buildRaffaTurnFromReply,
  buildYouTurn,
  createConversationAndAsk,
  deriveConversationTitle,
  markInterviewAnswered,
  nextTurnId,
  pendingInterview,
  type AskTurnView,
} from "./askViewModel";

/**
 * Parallel Ask sessions. Every conversation the user has touched in this tab keeps its own thread,
 * its own "Raffa is answering" flag and its own unread flag here, **outside** `AskRoute`: a reply
 * lands in the conversation that asked for it, whichever conversation (or screen) is on screen when
 * it arrives. That is what lets the user leave a chat that is still thinking, open another one (or
 * a new chat) and keep asking there -- two sessions running side by side -- instead of the reply
 * being written into whatever thread happened to be mounted when the request resolved.
 *
 * Keys: a conversation that exists on the server is keyed by its id. A new chat that has not been
 * created yet is keyed by a draft key (`draftSessionKey`, one per `/ask` history entry); the moment
 * its `POST /api/conversations` resolves, the session is **promoted** to the new id and the draft
 * key keeps resolving to it (`resolveSessionKey`), so the screen still showing the draft follows it
 * to `/ask/<id>` without a blank frame.
 *
 * Framework-free (a plain subscribe/snapshot store, read through `useSyncExternalStore` by
 * `AskSessionsContext.tsx`) so it can be exercised without rendering anything.
 */
export interface AskSession {
  key: string;
  /** `null` only while a new chat's own create call is still in flight (or failed). */
  conversationId: string | null;
  turns: readonly AskTurnView[];
  title: string | null;
  /** The name the user gave the chat, as the server last reported it (`null`: automatic title).
   * A rename made in this tab is read through `customTitleFor`, which prefers it. */
  customTitle: string | null;
  /** The conversation's persisted `scopeContractId` (NW-78 binding chip), once known. */
  boundContractId: string | null;
  /** A question was sent and its reply has not landed yet. */
  pending: boolean;
  /** A reply landed while this session was not on screen (or the tab was hidden). */
  unread: boolean;
  /** ADR-030 D5: the feedback offers answered in this tab, so a card never re-opens after "Send". */
  feedbackDone: ReadonlySet<string>;
}

export interface AskSessionsSnapshot {
  sessions: ReadonlyMap<string, AskSession>;
  /** draft key -> the conversation id it was promoted to. */
  promotions: ReadonlyMap<string, string>;
  /** conversation id -> the name given in this tab (`null`: cleared), ahead of any server read. */
  customTitles: ReadonlyMap<string, string | null>;
  /** The session currently on screen, `null` when no Ask thread is mounted. */
  viewingKey: string | null;
  /** Bumped whenever the server-side conversation list may have changed (created, answered). */
  listVersion: number;
}

export interface AskReplyEvent {
  conversationId: string | null;
  title: string | null;
  /** `false` for a transport/server failure rather than a reply. */
  ok: boolean;
  /** The session was on screen, in a visible tab, when the reply landed. */
  seen: boolean;
}

export interface AskSendInput {
  key: string;
  apiClient: ApiClient;
  tenantId: string;
  text: string;
  /** An explicit interview option click; otherwise a pending interview is answered as free text. */
  interviewAnswer?: { messageId: string; questionKey: string; optionKey: string | null };
  /** `/ask?scope=<contractId>`: only read when this send creates the conversation. */
  scopeContractId?: string;
}

export interface AskFeedbackInput {
  key: string;
  apiClient: ApiClient;
  tenantId: string;
  messageId: string;
  answers: FeedbackAnswers;
}

export interface AskRenameInput {
  apiClient: ApiClient;
  tenantId: string;
  conversationId: string;
  /** The new name; blank or `null` clears it back to the automatic title. */
  name: string | null;
}

export interface AskSessionStore {
  getSnapshot(): AskSessionsSnapshot;
  subscribe(listener: () => void): () => void;
  onReply(listener: (event: AskReplyEvent) => void): () => void;
  /** Seeds a resumed conversation from `GET /api/conversations/{id}`; never overwrites a live one. */
  hydrate(
    conversationId: string,
    seed: { turns: readonly AskTurnView[]; title: string | null; boundContractId: string | null; customTitle?: string | null },
  ): void;
  /** Which session is on screen; opening a session marks its reply read. */
  setViewing(key: string | null): void;
  /** The tab became visible again: the session on screen has been seen. */
  markViewingSeen(): void;
  /** Sends one message. Returns `null` (and does nothing) while that session is still answering. */
  send(input: AskSendInput): Promise<void> | null;
  submitFeedback(input: AskFeedbackInput): Promise<{ ok: boolean }>;
  /**
   * Renames a chat -- any of the caller's, open in this tab or only listed in the rail. The name
   * shows everywhere at once (rail, chat header, reply notices) and is rolled back if the server
   * refuses it.
   */
  rename(input: AskRenameInput): Promise<{ ok: boolean; error: string | null }>;
  /** Drops a deleted conversation; a reply still in flight for it is discarded. */
  forget(conversationId: string): void;
  /** Unsent composer text, kept per session so switching chats never loses a half-typed question. */
  saveComposerText(key: string, text: string): void;
  composerText(key: string): string;
}

/** One draft per `/ask` history entry: "+ New chat" pushes a new entry, so it always gets a blank one. */
export function draftSessionKey(locationKey: string): string {
  return `draft:${locationKey}`;
}

export function resolveSessionKey(snapshot: AskSessionsSnapshot, key: string): string {
  return snapshot.promotions.get(key) ?? key;
}

/** The chat's name as this tab knows it: a rename made here, else the server's `customTitle`. */
export function customTitleFor(snapshot: AskSessionsSnapshot, conversationId: string, serverValue: string | null | undefined): string | null {
  return snapshot.customTitles.has(conversationId) ? (snapshot.customTitles.get(conversationId) ?? null) : (serverValue ?? null);
}

export function unreadSessionCount(snapshot: AskSessionsSnapshot): number {
  let count = 0;
  for (const session of snapshot.sessions.values()) {
    if (session.unread) count += 1;
  }
  return count;
}

function blankSession(key: string): AskSession {
  return {
    key,
    conversationId: null,
    turns: [],
    title: null,
    customTitle: null,
    boundContractId: null,
    pending: false,
    unread: false,
    feedbackDone: new Set(),
  };
}

function documentHidden(): boolean {
  return typeof document !== "undefined" && document.visibilityState === "hidden";
}

export function createAskSessionStore(): AskSessionStore {
  let snapshot: AskSessionsSnapshot = { sessions: new Map(), promotions: new Map(), customTitles: new Map(), viewingKey: null, listVersion: 0 };
  const listeners = new Set<() => void>();
  const replyListeners = new Set<(event: AskReplyEvent) => void>();
  const composerDrafts = new Map<string, string>();
  // The latest rename per chat: an older one settling late never overwrites a newer name.
  const renameSeq = new Map<string, number>();

  function commit(next: Partial<AskSessionsSnapshot>) {
    snapshot = { ...snapshot, ...next };
    for (const listener of listeners) listener();
  }

  function withSession(key: string, update: (session: AskSession) => AskSession | null, extra: Partial<AskSessionsSnapshot> = {}) {
    const current = snapshot.sessions.get(key);
    if (!current) return;
    const updated = update(current);
    if (updated === null) return;
    const sessions = new Map(snapshot.sessions);
    sessions.set(key, updated);
    commit({ ...extra, sessions });
  }

  function promote(draftKey: string, conversationId: string, boundContractId: string | null) {
    const draft = snapshot.sessions.get(draftKey);
    if (!draft) return;
    const sessions = new Map(snapshot.sessions);
    sessions.delete(draftKey);
    sessions.set(conversationId, { ...draft, key: conversationId, conversationId, boundContractId });
    const promotions = new Map(snapshot.promotions);
    promotions.set(draftKey, conversationId);
    const text = composerDrafts.get(draftKey);
    if (text !== undefined) {
      composerDrafts.delete(draftKey);
      composerDrafts.set(conversationId, text);
    }
    commit({
      sessions,
      promotions,
      viewingKey: snapshot.viewingKey === draftKey ? conversationId : snapshot.viewingKey,
      listVersion: snapshot.listVersion + 1,
    });
  }

  function land(key: string, turn: AskTurnView, ok: boolean) {
    const session = snapshot.sessions.get(key);
    if (!session) return;
    const seen = snapshot.viewingKey === key && !documentHidden();
    withSession(key, (current) => ({ ...current, turns: [...current.turns, turn], pending: false, unread: !seen }), {
      listVersion: snapshot.listVersion + 1,
    });
    const name = session.conversationId === null ? null : customTitleFor(snapshot, session.conversationId, session.customTitle);
    const event: AskReplyEvent = { conversationId: session.conversationId, title: name ?? session.title, ok, seen };
    for (const listener of replyListeners) listener(event);
  }

  return {
    getSnapshot: () => snapshot,

    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },

    onReply(listener) {
      replyListeners.add(listener);
      return () => replyListeners.delete(listener);
    },

    hydrate(conversationId, seed) {
      if (snapshot.sessions.has(conversationId)) return;
      const sessions = new Map(snapshot.sessions);
      sessions.set(conversationId, { ...blankSession(conversationId), conversationId, ...seed, customTitle: seed.customTitle ?? null });
      commit({ sessions });
    },

    setViewing(key) {
      const resolved = key === null ? null : resolveSessionKey(snapshot, key);
      const clearUnread = resolved !== null && snapshot.sessions.get(resolved)?.unread === true && !documentHidden();
      if (resolved === snapshot.viewingKey && !clearUnread) return;
      if (clearUnread) {
        withSession(resolved, (current) => ({ ...current, unread: false }), { viewingKey: resolved });
        return;
      }
      commit({ viewingKey: resolved });
    },

    markViewingSeen() {
      const key = snapshot.viewingKey;
      if (key === null || documentHidden()) return;
      withSession(key, (current) => (current.unread ? { ...current, unread: false } : null));
    },

    send({ key, apiClient, tenantId, text, interviewAnswer, scopeContractId }) {
      const sessionKey = resolveSessionKey(snapshot, key);
      const existing = snapshot.sessions.get(sessionKey) ?? blankSession(sessionKey);
      if (existing.pending) return null;

      const pending =
        interviewAnswer ??
        (() => {
          const found = pendingInterview(existing.turns);
          return found ? { ...found, optionKey: null } : null;
        })();

      const turns = pending === null ? existing.turns : markInterviewAnswered(existing.turns, pending.messageId);
      const sessions = new Map(snapshot.sessions);
      sessions.set(sessionKey, {
        ...existing,
        turns: [...turns, buildYouTurn(nextTurnId(), text)],
        title: existing.title ?? deriveConversationTitle(text),
        pending: true,
        unread: false,
      });
      commit({ sessions });

      const run = async () => {
        let landingKey = sessionKey;
        try {
          if (existing.conversationId === null) {
            // AC-1: "a question creates a conversation ... then posts the message". The session
            // moves to the new id as soon as it exists, so the rail lists it (still answering) and
            // the user can leave for another chat while the reply is being written.
            const result = await createConversationAndAsk(apiClient, tenantId, text, scopeContractId, (created) => {
              promote(sessionKey, created.id, created.scopeContractId);
              landingKey = created.id;
            });
            land(
              landingKey,
              result.ok ? buildRaffaTurnFromReply(nextTurnId(), result.reply) : buildErrorTurn(nextTurnId(), result.reason),
              result.ok,
            );
            return;
          }

          const request = pending === null ? { question: text } : buildInterviewAnswerRequest(text, pending.messageId, pending.questionKey, pending.optionKey);
          const result = await apiClient.postMessage(tenantId, existing.conversationId, request);
          const ok = result.ok && result.reply !== null && result.reply !== undefined;
          land(
            landingKey,
            ok && result.reply ? buildRaffaTurnFromReply(nextTurnId(), result.reply) : buildErrorTurn(nextTurnId(), result.error ?? TRANSPORT_ERROR_REASON),
            ok,
          );
        } catch {
          land(landingKey, buildErrorTurn(nextTurnId(), TRANSPORT_ERROR_REASON), false);
        }
      };
      return run();
    },

    async submitFeedback({ key, apiClient, tenantId, messageId, answers }) {
      const sessionKey = resolveSessionKey(snapshot, key);
      const conversationId = snapshot.sessions.get(sessionKey)?.conversationId ?? null;
      if (conversationId === null) return { ok: false };

      const result = await apiClient.postConversationFeedback(tenantId, conversationId, { messageId, answers });
      if (!result.ok) return { ok: false };

      const message = result.result?.message ?? null;
      withSession(conversationId, (current) => ({
        ...current,
        feedbackDone: new Set(current.feedbackDone).add(messageId),
        turns: message ? [...current.turns, buildRaffaTurnFromMessage(message)] : current.turns,
      }));
      return { ok: true };
    },

    async rename({ apiClient, tenantId, conversationId, name }) {
      const next = normalizeConversationName(name);
      if (next !== null && next.length > CONVERSATION_NAME_MAX_LENGTH) {
        return { ok: false, error: `A chat name is at most ${CONVERSATION_NAME_MAX_LENGTH} characters.` };
      }

      const hadOverride = snapshot.customTitles.has(conversationId);
      const previous = snapshot.customTitles.get(conversationId) ?? null;
      const seq = (renameSeq.get(conversationId) ?? 0) + 1;
      renameSeq.set(conversationId, seq);

      const optimistic = new Map(snapshot.customTitles);
      optimistic.set(conversationId, next);
      commit({ customTitles: optimistic });

      let result: Awaited<ReturnType<ApiClient["renameConversation"]>>;
      try {
        result = await apiClient.renameConversation(tenantId, conversationId, next);
      } catch {
        result = { ok: false, statusCode: null, conversation: null, error: null };
      }
      if (renameSeq.get(conversationId) !== seq) return { ok: result.ok, error: result.ok ? null : result.error };

      if (!result.ok || !result.conversation) {
        const rolledBack = new Map(snapshot.customTitles);
        if (hadOverride) rolledBack.set(conversationId, previous);
        else rolledBack.delete(conversationId);
        commit({ customTitles: rolledBack });
        return { ok: false, error: result.error ?? "The chat could not be renamed." };
      }

      const saved = result.conversation.customTitle ?? null;
      const customTitles = new Map(snapshot.customTitles);
      customTitles.set(conversationId, saved);
      const session = snapshot.sessions.get(conversationId);
      const sessions = session ? new Map(snapshot.sessions).set(conversationId, { ...session, customTitle: saved }) : snapshot.sessions;
      commit({ customTitles, sessions, listVersion: snapshot.listVersion + 1 });
      return { ok: true, error: null };
    },

    forget(conversationId) {
      if (snapshot.customTitles.has(conversationId)) {
        const customTitles = new Map(snapshot.customTitles);
        customTitles.delete(conversationId);
        commit({ customTitles });
      }
      if (!snapshot.sessions.has(conversationId)) return;
      const sessions = new Map(snapshot.sessions);
      sessions.delete(conversationId);
      composerDrafts.delete(conversationId);
      commit({ sessions, viewingKey: snapshot.viewingKey === conversationId ? null : snapshot.viewingKey });
    },

    saveComposerText(key, text) {
      const sessionKey = resolveSessionKey(snapshot, key);
      if (text === "") composerDrafts.delete(sessionKey);
      else composerDrafts.set(sessionKey, text);
    },

    composerText(key) {
      return composerDrafts.get(resolveSessionKey(snapshot, key)) ?? "";
    },
  };
}
