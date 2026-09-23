import { describe, expect, it, vi } from "vitest";
import type { ApiClient, ConversationReplyBody, CreateConversationResult, PostMessageResult } from "../../../src/api/client";
import type { AskTurnView } from "../../../src/routes/ask/askViewModel";
import { createAskSessionStore, draftSessionKey, resolveSessionKey, unreadSessionCount, type AskReplyEvent } from "../../../src/routes/ask/askSessions";

const TENANT = "tenant-1";

function reply(conversationId: string, answerMarkdown: string): ConversationReplyBody {
  return {
    conversationId,
    messageId: `msg-${conversationId}`,
    kind: "answer",
    answerMarkdown,
    citations: [],
    actions: [],
    provenance: { sources: ["tenant"], modelId: "fixture", promptVersion: "answer-v2.1", inputHash: "abc" },
    followUps: [],
    payload: null,
  };
}

function created(id: string, scopeContractId: string | null = null): CreateConversationResult {
  return { ok: true, statusCode: 201, conversation: { id, title: "New chat", scopeContractId, updatedAt: "2026-09-08T00:00:00Z" }, error: null };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

/** Only the two calls a send makes; the store never reaches anything else. */
function apiClient(overrides: Partial<Pick<ApiClient, "createConversation" | "postMessage" | "postConversationFeedback">>): ApiClient {
  return {
    createConversation: vi.fn(),
    postMessage: vi.fn(),
    postConversationFeedback: vi.fn(),
    ...overrides,
  } as unknown as ApiClient;
}

/** Whether any turn of the thread carries `text` (reply bodies are view-model shaped, not wire). */
function mentions(turns: readonly AskTurnView[], text: string): boolean {
  return JSON.stringify(turns).includes(text);
}

describe("askSessions -- parallel Ask sessions", () => {
  it("promotes a new chat to its conversation id as soon as it is created, before the reply lands", async () => {
    const post = deferred<PostMessageResult>();
    const client = apiClient({ createConversation: vi.fn().mockResolvedValue(created("conv-a")), postMessage: vi.fn().mockReturnValue(post.promise) });
    const store = createAskSessionStore();
    const draft = draftSessionKey("entry-1");
    store.setViewing(draft);

    const done = store.send({ key: draft, apiClient: client, tenantId: TENANT, text: "What is our Atlassian cap?" });
    expect(store.getSnapshot().sessions.get(draft)?.pending).toBe(true);

    await vi.waitFor(() => expect(resolveSessionKey(store.getSnapshot(), draft)).toBe("conv-a"));
    const session = store.getSnapshot().sessions.get("conv-a")!;
    expect(session.conversationId).toBe("conv-a");
    expect(session.pending).toBe(true);
    expect(session.title).toBe("What is our Atlassian cap?");
    expect(store.getSnapshot().viewingKey).toBe("conv-a");
    expect(store.getSnapshot().sessions.has(draft)).toBe(false);

    post.resolve({ ok: true, statusCode: 200, reply: reply("conv-a", "Two times fees."), error: null });
    await done;
    expect(store.getSnapshot().sessions.get("conv-a")?.pending).toBe(false);
    expect(store.getSnapshot().sessions.get("conv-a")?.unread).toBe(false);
  });

  it("lands each reply in the session that asked it, while the user has moved to another one", async () => {
    const postA = deferred<PostMessageResult>();
    const postB = deferred<PostMessageResult>();
    const client = apiClient({
      createConversation: vi.fn().mockResolvedValueOnce(created("conv-a")).mockResolvedValueOnce(created("conv-b")),
      postMessage: vi.fn((_tenant: string, conversationId: string) => (conversationId === "conv-a" ? postA.promise : postB.promise)),
    });
    const store = createAskSessionStore();
    const events: AskReplyEvent[] = [];
    store.onReply((event) => events.push(event));

    store.setViewing(draftSessionKey("entry-1"));
    const doneA = store.send({ key: draftSessionKey("entry-1"), apiClient: client, tenantId: TENANT, text: "First question" });
    await vi.waitFor(() => expect(store.getSnapshot().sessions.has("conv-a")).toBe(true));

    // "+ New chat" while A is still answering: B runs in parallel.
    store.setViewing(draftSessionKey("entry-2"));
    const doneB = store.send({ key: draftSessionKey("entry-2"), apiClient: client, tenantId: TENANT, text: "Second question" });
    await vi.waitFor(() => expect(store.getSnapshot().sessions.has("conv-b")).toBe(true));
    expect(store.getSnapshot().sessions.get("conv-a")?.pending).toBe(true);
    expect(store.getSnapshot().sessions.get("conv-b")?.pending).toBe(true);

    postA.resolve({ ok: true, statusCode: 200, reply: reply("conv-a", "Answer A"), error: null });
    await doneA;
    const a = store.getSnapshot().sessions.get("conv-a")!;
    expect(mentions(a.turns, "Answer A")).toBe(true);
    expect(a.unread).toBe(true);
    expect(mentions(store.getSnapshot().sessions.get("conv-b")!.turns, "Answer A")).toBe(false);
    expect(events).toEqual([{ conversationId: "conv-a", title: "First question", ok: true, seen: false }]);
    expect(unreadSessionCount(store.getSnapshot())).toBe(1);

    postB.resolve({ ok: true, statusCode: 200, reply: reply("conv-b", "Answer B"), error: null });
    await doneB;
    expect(store.getSnapshot().sessions.get("conv-b")?.unread).toBe(false);
    expect(events[1]).toEqual({ conversationId: "conv-b", title: "Second question", ok: true, seen: true });

    // Opening A reads its reply.
    store.setViewing("conv-a");
    expect(store.getSnapshot().sessions.get("conv-a")?.unread).toBe(false);
    expect(unreadSessionCount(store.getSnapshot())).toBe(0);
  });

  it("refuses a second question in a session that is still answering", async () => {
    const post = deferred<PostMessageResult>();
    const postMessage = vi.fn().mockReturnValue(post.promise);
    const store = createAskSessionStore();
    store.hydrate("conv-a", { turns: [], title: "Chat", boundContractId: null });
    const client = apiClient({ postMessage });

    expect(store.send({ key: "conv-a", apiClient: client, tenantId: TENANT, text: "One" })).not.toBeNull();
    expect(store.send({ key: "conv-a", apiClient: client, tenantId: TENANT, text: "Two" })).toBeNull();
    expect(postMessage).toHaveBeenCalledTimes(1);
  });

  it("lands a transport failure as an error turn and reports it as not ok", async () => {
    const store = createAskSessionStore();
    const events: AskReplyEvent[] = [];
    store.onReply((event) => events.push(event));
    store.hydrate("conv-a", { turns: [], title: "Chat", boundContractId: null });
    const client = apiClient({ postMessage: vi.fn().mockRejectedValue(new Error("offline")) });

    await store.send({ key: "conv-a", apiClient: client, tenantId: TENANT, text: "Hello" });

    const session = store.getSnapshot().sessions.get("conv-a")!;
    expect(session.pending).toBe(false);
    const last = session.turns.at(-1);
    expect(last?.role === "raffa" && last.reply.kind === "error").toBe(true);
    expect(events[0].ok).toBe(false);
  });

  it("drops the reply of a conversation deleted while it was answering", async () => {
    const post = deferred<PostMessageResult>();
    const store = createAskSessionStore();
    const events: AskReplyEvent[] = [];
    store.onReply((event) => events.push(event));
    store.hydrate("conv-a", { turns: [], title: "Chat", boundContractId: null });

    const done = store.send({ key: "conv-a", apiClient: apiClient({ postMessage: vi.fn().mockReturnValue(post.promise) }), tenantId: TENANT, text: "Hi" });
    store.forget("conv-a");
    post.resolve({ ok: true, statusCode: 200, reply: reply("conv-a", "Late"), error: null });
    await done;

    expect(store.getSnapshot().sessions.has("conv-a")).toBe(false);
    expect(events).toEqual([]);
  });

  it("never lets a resume fetch overwrite a session this tab already holds", () => {
    const store = createAskSessionStore();
    store.hydrate("conv-a", { turns: [], title: "Live", boundContractId: "contract-1" });
    store.hydrate("conv-a", { turns: [], title: "Stale", boundContractId: null });

    expect(store.getSnapshot().sessions.get("conv-a")).toMatchObject({ title: "Live", boundContractId: "contract-1" });
  });

  it("keeps unsent composer text per session, following a draft to the conversation it became", async () => {
    const store = createAskSessionStore();
    const draft = draftSessionKey("entry-1");
    store.saveComposerText("conv-b", "half-typed");
    store.saveComposerText(draft, "follow-up");
    const client = apiClient({
      createConversation: vi.fn().mockResolvedValue(created("conv-a")),
      postMessage: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, reply: reply("conv-a", "Ok"), error: null }),
    });

    await store.send({ key: draft, apiClient: client, tenantId: TENANT, text: "Question" });

    expect(store.composerText("conv-b")).toBe("half-typed");
    expect(store.composerText("conv-a")).toBe("follow-up");
    expect(store.composerText(draft)).toBe("follow-up");
  });
});
