import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import AskRoute from "../../../src/routes/ask";
import AskReplyNotifier from "../../../src/routes/ask/AskReplyNotifier";
import { AskSessionsProvider } from "../../../src/routes/ask/AskSessionsContext";
import RailNav from "../../../src/components/shell/RailNav";
import { DocumentViewerProvider } from "../../../src/routes/documents/viewer/DocumentViewerOverlay";
import type { ApiClient, ConversationReplyBody, ConversationSummaryBody, PostMessageResult } from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

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

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

/**
 * A server with two conversations to hand out: `conv-a` answers only when the test says so,
 * `conv-b` answers at once. `listConversations` returns whatever has been created so far, the way
 * the real list does.
 */
function parallelServer() {
  const postA = deferred<PostMessageResult>();
  const created: ConversationSummaryBody[] = [];
  const ids = ["conv-a", "conv-b"];
  const apiClient = {
    listWorkspaces: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      workspaces: [{ id: WORKSPACE_ID, name: "Acme Procurement", createdAt: "2026-01-01T00:00:00Z", role: "Admin", contractCount: 1 }],
      error: null,
    }),
    listDocuments: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      page: { items: [], page: 1, pageSize: 1, totalCount: 0, counts: { all: 0, needsAttention: 0, needsReview: 0, processing: 0, rejected: 0 } },
      error: null,
    }),
    getPortfolio: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, portfolio: { items: [], page: 1, pageSize: 100, totalCount: 0 }, error: null }),
    getCapabilities: vi.fn().mockResolvedValue({ ok: false, statusCode: 503, catalog: null, error: null }),
    getWorkspaceSettings: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, settings: null, error: null }),
    getContract360: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, contract: null, error: null }),
    getConversation: vi.fn(),
    deleteConversation: vi.fn(),
    renameConversation: vi.fn(async (_tenant: string, id: string, title: string | null) => {
      const row = created.find((conversation) => conversation.id === id)!;
      row.customTitle = title;
      return { ok: true, statusCode: 200, conversation: { ...row }, error: null };
    }),
    listConversations: vi.fn(async () => ({ ok: true, statusCode: 200, conversations: [...created].reverse(), error: null })),
    createConversation: vi.fn(async () => {
      const id = ids.shift()!;
      created.push({ id, title: "New chat", scopeContractId: null, updatedAt: "2026-09-08T00:00:00Z" });
      return { ok: true, statusCode: 201, conversation: { id, title: "New chat", scopeContractId: null, updatedAt: "2026-09-08T00:00:00Z" }, error: null };
    }),
    postMessage: vi.fn((_tenant: string, conversationId: string) =>
      conversationId === "conv-a"
        ? postA.promise
        : Promise.resolve({ ok: true, statusCode: 200, reply: reply(conversationId, "Answer B."), error: null }),
    ),
  } as unknown as ApiClient;
  return { apiClient, answerA: (markdown: string) => postA.resolve({ ok: true, statusCode: 200, reply: reply("conv-a", markdown), error: null }) };
}

function PathProbe() {
  return <div data-testid="path">{useLocation().pathname}</div>;
}

/** The signed-in shell's Ask half: one session store shared by the rail, the screen and the notices. */
function renderShell(apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={["/ask"]}>
      <DocumentViewerProvider apiClient={apiClient}>
        <AskSessionsProvider>
          <PathProbe />
          <RailNav
            workspaceName="Acme Procurement"
            role="admin"
            userLabel="user@example.test"
            kbReady
            validatedContractCount={1}
            documentCounts={null}
            onSignOut={vi.fn()}
            apiClient={apiClient}
          />
          <Routes>
            <Route path="/ask" element={<AskRoute apiClient={apiClient} />} />
            <Route path="/ask/:conversationId" element={<AskRoute apiClient={apiClient} />} />
          </Routes>
          <AskReplyNotifier />
        </AskSessionsProvider>
      </DocumentViewerProvider>
    </MemoryRouter>,
  );
}

function railRow(container: HTMLElement, conversationId: string): HTMLElement | null {
  return container.querySelector(`a.shell-rail-conv-item[href="/ask/${conversationId}"]`);
}

let visibility: DocumentVisibilityState = "visible";

class FakeNotification {
  static permission: NotificationPermission = "default";
  static requestPermission = vi.fn(async () => {
    FakeNotification.permission = "granted";
    return "granted" as NotificationPermission;
  });
  static instances: FakeNotification[] = [];
  onclick: (() => void) | null = null;
  close = vi.fn();
  constructor(
    public title: string,
    public options?: NotificationOptions,
  ) {
    FakeNotification.instances.push(this);
  }
}

describe("parallel Ask sessions (two chats at once, reply-ready notices, rail highlight)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
    document.title = "Raffa.ai";
    visibility = "visible";
    Object.defineProperty(document, "visibilityState", { configurable: true, get: () => visibility });
    FakeNotification.permission = "default";
    FakeNotification.instances = [];
    FakeNotification.requestPermission.mockClear();
    vi.stubGlobal("Notification", FakeNotification);
    vi.spyOn(window, "focus").mockImplementation(() => undefined);
    // jsdom never focuses its document; the tests say when the window is away.
    vi.spyOn(document, "hasFocus").mockImplementation(() => visibility === "visible");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    delete (document as { visibilityState?: DocumentVisibilityState }).visibilityState;
  });

  it("keeps chat A answering while the user opens a new chat B, then lands A's reply in A and flags it", async () => {
    const { apiClient, answerA } = parallelServer();
    const user = userEvent.setup();
    const { container } = renderShell(apiClient);

    await user.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "What is our liability cap with Atlassian?{Enter}");
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent("/ask/conv-a"));
    expect(screen.getByText(/authorising scope/i)).toBeInTheDocument();
    // The browser is asked, once, inside the send gesture.
    expect(FakeNotification.requestPermission).toHaveBeenCalledTimes(1);
    // The rail lists A at once, still answering.
    await waitFor(() => expect(railRow(container, "conv-a")?.querySelector(".shell-rail-conv-status.is-pending")).not.toBeNull());

    // "+ New chat" while A is still thinking: a blank chat, not A's spinner.
    await user.click(screen.getByRole("button", { name: "+ New chat" }));
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent(/^\/ask$/));
    expect(screen.getByText("What do you want to know?")).toBeInTheDocument();
    expect(screen.queryByText(/authorising scope/i)).not.toBeInTheDocument();

    await user.type(screen.getByRole("textbox", { name: /ask raffa a question/i }), "Which contracts renew this quarter?{Enter}");
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent("/ask/conv-b"));
    expect(await screen.findByText("Answer B.")).toBeInTheDocument();

    // A answers while B is on screen: never written into B's thread.
    answerA("Two times the fees paid.");
    const toast = await screen.findByText("Reply ready");
    expect(screen.queryByText("Two times the fees paid.")).not.toBeInTheDocument();
    const notice = toast.closest(".ask-reply-toast") as HTMLElement;
    expect(within(notice).getByText("What is our liability cap with Atlassian?")).toBeInTheDocument();
    await waitFor(() => expect(railRow(container, "conv-a")).toHaveClass("is-unread"));
    expect(railRow(container, "conv-a")?.querySelector(".shell-rail-conv-status.is-pending")).toBeNull();
    expect(railRow(container, "conv-b")).not.toHaveClass("is-unread");
    expect(document.title).toBe("(1) Raffa.ai");

    // One click opens A with its reply -- from this tab's session, not a re-fetch.
    await user.click(within(notice).getByRole("button", { name: "Open chat" }));
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent("/ask/conv-a"));
    expect(await screen.findByText("Two times the fees paid.")).toBeInTheDocument();
    expect(screen.queryByText("Answer B.")).not.toBeInTheDocument();
    expect(apiClient.getConversation).not.toHaveBeenCalled();
    await waitFor(() => expect(railRow(container, "conv-a")).not.toHaveClass("is-unread"));
    expect(screen.queryByText("Reply ready")).not.toBeInTheDocument();
    expect(document.title).toBe("Raffa.ai");
  });

  it("opening the flagged chat from the rail clears its highlight and its notice", async () => {
    const { apiClient, answerA } = parallelServer();
    const user = userEvent.setup();
    const { container } = renderShell(apiClient);

    await user.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "First question{Enter}");
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent("/ask/conv-a"));
    await user.click(screen.getByRole("button", { name: "+ New chat" }));
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent(/^\/ask$/));

    answerA("Answer A.");
    await screen.findByText("Reply ready");
    await waitFor(() => expect(railRow(container, "conv-a")).toHaveClass("is-unread"));

    await user.click(railRow(container, "conv-a")!);
    expect(await screen.findByText("Answer A.")).toBeInTheDocument();
    expect(railRow(container, "conv-a")).not.toHaveClass("is-unread");
    expect(screen.queryByText("Reply ready")).not.toBeInTheDocument();
  });

  it("sends a system notification when the reply lands while the tab is hidden, and clicking it opens the chat", async () => {
    const { apiClient, answerA } = parallelServer();
    const user = userEvent.setup();
    const { container } = renderShell(apiClient);

    await user.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "First question{Enter}");
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent("/ask/conv-a"));
    await user.click(screen.getByRole("button", { name: "+ New chat" }));
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent(/^\/ask$/));

    visibility = "hidden";
    document.dispatchEvent(new Event("visibilitychange"));
    answerA("Answer A.");

    await waitFor(() => expect(FakeNotification.instances).toHaveLength(1));
    const [notification] = FakeNotification.instances;
    expect(notification.title).toBe("Raffa.ai answered");
    expect(notification.options?.body).toBe("First question");
    expect(document.title).toBe("(1) Raffa.ai");

    visibility = "visible";
    document.dispatchEvent(new Event("visibilitychange"));
    notification.onclick?.();
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent("/ask/conv-a"));
    expect(await screen.findByText("Answer A.")).toBeInTheDocument();
    await waitFor(() => expect(railRow(container, "conv-a")).not.toHaveClass("is-unread"));
    expect(notification.close).toHaveBeenCalled();
  });

  it("a reply that lands on the open chat while the tab is hidden stays unread until the user comes back", async () => {
    const { apiClient, answerA } = parallelServer();
    const user = userEvent.setup();
    const { container } = renderShell(apiClient);

    await user.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "First question{Enter}");
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent("/ask/conv-a"));

    visibility = "hidden";
    answerA("Answer A.");
    expect(await screen.findByText("Answer A.")).toBeInTheDocument();
    await waitFor(() => expect(railRow(container, "conv-a")).toHaveClass("is-unread"));
    expect(document.title).toBe("(1) Raffa.ai");
    // The user was away: the chat left on screen is announced too, but gets no in-app notice.
    expect(FakeNotification.instances.map((notification) => notification.title)).toEqual(["Raffa.ai answered"]);

    visibility = "visible";
    document.dispatchEvent(new Event("visibilitychange"));
    await waitFor(() => expect(railRow(container, "conv-a")).not.toHaveClass("is-unread"));
    expect(document.title).toBe("Raffa.ai");
    expect(screen.queryByText("Reply ready")).not.toBeInTheDocument();
  });

  it("stays quiet about a reply the user is watching land", async () => {
    const { apiClient, answerA } = parallelServer();
    const user = userEvent.setup();
    const { container } = renderShell(apiClient);

    await user.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "First question{Enter}");
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent("/ask/conv-a"));
    answerA("Answer A.");

    expect(await screen.findByText("Answer A.")).toBeInTheDocument();
    expect(railRow(container, "conv-a")).not.toHaveClass("is-unread");
    expect(screen.queryByText("Reply ready")).not.toBeInTheDocument();
    expect(FakeNotification.instances).toHaveLength(0);
    expect(document.title).toBe("Raffa.ai");
  });

  it("renaming a chat in the rail retitles it everywhere: its header, and the notice when it answers", async () => {
    const { apiClient, answerA } = parallelServer();
    const user = userEvent.setup();
    const { container } = renderShell(apiClient);

    await user.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "What is our liability cap with Atlassian?{Enter}");
    await waitFor(() => expect(railRow(container, "conv-a")).not.toBeNull());

    await user.click(screen.getByRole("button", { name: /^Rename Ask Raffa/ }));
    const field = screen.getByRole("textbox", { name: /^Rename Ask Raffa/ });
    await user.clear(field);
    await user.type(field, "Atlassian cap{Enter}");

    expect(screen.getByRole("heading", { name: "Atlassian cap" })).toBeInTheDocument();
    expect(railRow(container, "conv-a")).toHaveTextContent("Atlassian cap");

    await user.click(screen.getByRole("button", { name: "+ New chat" }));
    await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent(/^\/ask$/));
    answerA("Two times the fees paid.");

    const notice = (await screen.findByText("Reply ready")).closest(".ask-reply-toast") as HTMLElement;
    expect(within(notice).getByText("Atlassian cap")).toBeInTheDocument();
  });
});

