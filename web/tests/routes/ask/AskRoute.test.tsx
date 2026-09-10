import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation, useParams } from "react-router-dom";
import AskRoute from "../../../src/routes/ask";
import type {
  ApiClient,
  ConversationReplyBody,
  CreateConversationResult,
  GetPortfolioResult,
  ListDocumentsResult,
  PostMessageResult,
} from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const CONVERSATION_ID = "22222222-2222-2222-2222-222222222222";

function emptyPortfolio(): GetPortfolioResult {
  return { ok: true, statusCode: 200, portfolio: { items: [], page: 1, pageSize: 100, totalCount: 0 }, error: null };
}

function validatedPortfolio(): GetPortfolioResult {
  return {
    ok: true,
    statusCode: 200,
    portfolio: {
      items: [
        {
          contractId: "contract-1",
          supplierId: null,
          supplierName: null,
          type: "Msa",
          annualSpend: 500000,
          startDate: "2025-01-01",
          endDate: "2026-01-01",
          renewalDate: "2026-01-01",
          cancellationDeadline: "2025-11-01",
          autoRenewal: true,
          status: "completed",
          risk: "Low",
        },
      ],
      page: 1,
      pageSize: 100,
      totalCount: 1,
    },
    error: null,
  };
}

function emptyDocuments(): ListDocumentsResult {
  return { ok: true, statusCode: 200, page: { items: [], page: 1, pageSize: 1, totalCount: 0 }, error: null };
}

function emptyCatalog(): Awaited<ReturnType<ApiClient["getCapabilities"]>> {
  return {
    ok: true,
    statusCode: 200,
    catalog: {
      version: "capabilities-v2.0",
      capabilities: [
        {
          key: "ask",
          title: "Ask Raffa",
          routePattern: "/ask",
          description: "…",
          exampleQuestions: ["What can Raffa do?", "When does this contract expire?"],
          roleGate: "any",
          availability: "always",
          howTo: [],
        },
      ],
    },
    error: null,
  };
}

function answerReply(overrides: Partial<ConversationReplyBody> = {}): ConversationReplyBody {
  return {
    conversationId: CONVERSATION_ID,
    messageId: "msg-1",
    kind: "answer",
    answerMarkdown: "Salesforce ends on **15 January 2027** [1].",
    citations: [
      {
        n: 1,
        corpus: "tenant",
        title: "Salesforce · MSA 2024",
        subtitle: "p.12 §8.4",
        snippet: "automatically renew for successive twelve (12) month periods",
        documentId: "doc-1",
        contractId: "contract-1",
        page: 12,
        section: "8.4",
        previewUrl: null,
        href: "/contracts/contract-1",
        recordId: null,
      },
    ],
    actions: [{ label: "Open Contract 360 →", href: "/contracts/contract-1", kind: "navigate" }],
    provenance: { sources: ["tenant"], modelId: "fixture", promptVersion: "answer-v2.1", inputHash: "abc" },
    followUps: ["Where can I push on the renewal?"],
    ...overrides,
  };
}

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listDocuments: vi.fn().mockResolvedValue(emptyDocuments()),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    getPortfolio: vi.fn().mockResolvedValue(validatedPortfolio()),
    // AC-5's own scoped-new-chat effect (index.tsx) calls this unconditionally whenever `?scope=` is
    // present, so a bare, unresolved vi.fn() would throw the moment that effect calls .then() on it
    // -- same "resolved default required" reasoning tests/components/shell/WorkspaceShellApp.test.tsx's
    // own mockApiClient() already documents for this identical method. Every other test in this file
    // never sends `?scope=`, so this default never fires for them.
    getContract360: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, contract: null, error: "No contract found." }),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn(),
    validateDocument: vi.fn(),
    postRenewalAction: vi.fn(),
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askRaffa: vi.fn(),
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    listConversations: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations: [], error: null }),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn().mockResolvedValue(emptyCatalog()),
    getMarketRecord: vi.fn(),
    ...overrides,
  };
}

function createdConversation(id = CONVERSATION_ID): CreateConversationResult {
  return { ok: true, statusCode: 201, conversation: { id, title: "New chat", scopeContractId: null, updatedAt: "2026-09-08T00:00:00Z" }, error: null };
}

function postedReply(reply: ConversationReplyBody = answerReply()): PostMessageResult {
  return { ok: true, statusCode: 200, reply, error: null };
}

/** Stand-in for ../contracts/contract360/index.tsx -- proves AskRoute navigates with the exact
 * `?page=`/`state.from` shape that screen's own `resolveHighlightedClauseId`/`resolveBackLink`
 * read, without pulling that whole screen's own fetch machinery into this suite. */
function Contract360Stub() {
  const { contractId } = useParams<{ contractId: string }>();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? "none";
  return (
    <div>
      CONTRACT_360 contractId={contractId} search={location.search} from={from}
    </div>
  );
}

function PathProbe() {
  const location = useLocation();
  return <div data-testid="path">{location.pathname}</div>;
}

function renderAsk(apiClient: ApiClient, initialEntry: { pathname: string; state?: unknown } | string = "/ask") {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <PathProbe />
      <Routes>
        <Route path="/ask" element={<AskRoute apiClient={apiClient} />} />
        <Route path="/ask/:conversationId" element={<AskRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<Contract360Stub />} />
        <Route path="/renewals" element={<div>RENEWALS SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("AskRoute (V2, task E13/F09/US01/T04)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    renderAsk(mockApiClient());

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
  });

  describe("AC-1/R-ASK-10: off state (0 validated contracts, from the shell hook)", () => {
    it("shows the fixed headline, the 'upload first' reason, and 'Upload a contract' when no document exists at all", async () => {
      renderAsk(mockApiClient({ getPortfolio: vi.fn().mockResolvedValue(emptyPortfolio()), listDocuments: vi.fn().mockResolvedValue(emptyDocuments()) }));

      expect(await screen.findByText("Ask needs at least one validated contract.")).toBeInTheDocument();
      expect(screen.getByText(/upload a contract first/i)).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Upload a contract" })).toHaveAttribute("href", "/documents");
    });

    it("shows the 'still processing' reason and 'Go to Documents' when a document exists but none is validated", async () => {
      const listDocuments = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, page: { items: [], page: 1, pageSize: 1, totalCount: 1 }, error: null });
      renderAsk(mockApiClient({ getPortfolio: vi.fn().mockResolvedValue(emptyPortfolio()), listDocuments }));

      expect(await screen.findByText(/still processing or waiting for review/i)).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Go to Documents" })).toHaveAttribute("href", "/documents");
    });

    it("never calls createConversation/postMessage while off", async () => {
      const createConversation = vi.fn();
      renderAsk(mockApiClient({ getPortfolio: vi.fn().mockResolvedValue(emptyPortfolio()), createConversation }));

      await screen.findByText("Ask needs at least one validated contract.");
      expect(createConversation).not.toHaveBeenCalled();
    });
  });

  describe("AC-1/task text (2): new chat", () => {
    it("shows the hello line, the scope line naming the validated count, and two suggestion chips", async () => {
      renderAsk(mockApiClient());

      expect(await screen.findByText("What do you want to know?")).toBeInTheDocument();
      expect(screen.getByText(/answers only from 1 validated contract/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "What can Raffa do?" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "When does this contract expire?" })).toBeInTheDocument();
    });

    it("typing a question creates a conversation, posts the message, and navigates to /ask/<id>", async () => {
      const createConversation = vi.fn().mockResolvedValue(createdConversation());
      const postMessage = vi.fn().mockResolvedValue(postedReply());
      renderAsk(mockApiClient({ createConversation, postMessage }));

      const input = await screen.findByRole("textbox", { name: /ask raffa a question/i });
      await userEvent.type(input, "What liability do we have with AWS?{Enter}");

      expect(createConversation).toHaveBeenCalledWith(WORKSPACE_ID, {});
      await waitFor(() => expect(postMessage).toHaveBeenCalledWith(WORKSPACE_ID, CONVERSATION_ID, { question: "What liability do we have with AWS?" }));
      await waitFor(() => expect(screen.getByTestId("path")).toHaveTextContent(`/ask/${CONVERSATION_ID}`));
      expect(input).toHaveValue("");
    });

    it("clicking a suggestion chip asks it directly", async () => {
      const createConversation = vi.fn().mockResolvedValue(createdConversation());
      const postMessage = vi.fn().mockResolvedValue(postedReply());
      renderAsk(mockApiClient({ createConversation, postMessage }));

      await userEvent.click(await screen.findByRole("button", { name: "What can Raffa do?" }));

      await waitFor(() => expect(postMessage).toHaveBeenCalledWith(WORKSPACE_ID, CONVERSATION_ID, { question: "What can Raffa do?" }));
    });

    it("seeds and asks the query carried in router state from the global Ask bar, exactly once", async () => {
      const createConversation = vi.fn().mockResolvedValue(createdConversation());
      const postMessage = vi.fn().mockResolvedValue(postedReply());
      renderAsk(mockApiClient({ createConversation, postMessage }), { pathname: "/ask", state: { query: "What liability do we have with AWS?", newChat: true } });

      await waitFor(() => expect(createConversation).toHaveBeenCalledTimes(1));
      expect(postMessage).toHaveBeenCalledWith(WORKSPACE_ID, CONVERSATION_ID, { question: "What liability do we have with AWS?" });
    });

    it("AC-5 /ask?scope=<contractId> passes scopeContractId to createConversation", async () => {
      const createConversation = vi.fn().mockResolvedValue(createdConversation());
      const postMessage = vi.fn().mockResolvedValue(postedReply());
      renderAsk(mockApiClient({ createConversation, postMessage }), "/ask?scope=contract-1");

      const input = await screen.findByRole("textbox", { name: /ask raffa a question/i });
      await userEvent.type(input, "When must we give notice?{Enter}");

      await waitFor(() => expect(createConversation).toHaveBeenCalledWith(WORKSPACE_ID, { scopeContractId: "contract-1" }));
    });

    it("shows the thinking indicator while the request is in flight, then replaces it with the reply", async () => {
      let resolvePost!: (value: PostMessageResult) => void;
      const pending = new Promise<PostMessageResult>((resolve) => {
        resolvePost = resolve;
      });
      renderAsk(mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockReturnValue(pending) }));

      const input = await screen.findByRole("textbox", { name: /ask raffa a question/i });
      await userEvent.type(input, "What liability do we have with AWS?{Enter}");

      expect(await screen.findByText(/authorising scope/i)).toBeInTheDocument();
      resolvePost(postedReply());

      expect(await screen.findByText("15 January 2027")).toBeInTheDocument();
      await waitFor(() => expect(screen.queryByText(/authorising scope/i)).not.toBeInTheDocument());
    });
  });

  describe("AC-3: a mocked answer reply renders markdown + citation cards + actions", () => {
    it("renders bold markdown, a numbered citation card (validated contract badge), and an action button", async () => {
      renderAsk(
        mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockResolvedValue(postedReply()) }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");

      expect(await screen.findByText("15 January 2027")).toBeInTheDocument();
      expect(screen.getByText("Validated contract")).toBeInTheDocument();
      expect(screen.getByText("Salesforce · MSA 2024")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Open Contract 360 →" })).toHaveAttribute("href", "/contracts/contract-1");
      expect(screen.getByRole("button", { name: /Where can I push on the renewal\?/ })).toBeInTheDocument();
      expect(screen.queryByText(/cannot determine reliably/i)).not.toBeInTheDocument();
    });

    it("never renders 'Structured query…'/'Clause retrieval…' or a raw guid anywhere", async () => {
      renderAsk(
        mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockResolvedValue(postedReply()) }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");
      await screen.findByText("Salesforce · MSA 2024");

      // Scoped to the chat log itself: `PathProbe` (this suite's own routing harness) legitimately
      // renders the conversation id as part of `/ask/<id>` once navigation lands, which is not the
      // product surface this assertion is about.
      const log = screen.getByRole("log");
      expect(within(log).queryByText(/structured query/i)).not.toBeInTheDocument();
      expect(within(log).queryByText(/clause retrieval/i)).not.toBeInTheDocument();
      expect(within(log).queryByText(/22222222-2222-2222-2222-222222222222/)).not.toBeInTheDocument();
      expect(within(log).queryByText(/doc-1/)).not.toBeInTheDocument();
    });

    it("a redirect reply renders warm prose + one CTA, never the abstain block", async () => {
      const redirectReply = answerReply({
        kind: "redirect",
        answerMarkdown: "Ask never accepts attachments here — drop the contract in Documents instead.",
        citations: [],
        actions: [{ label: "Upload in Documents", href: "/documents", kind: "upload" }],
        followUps: [],
      });
      renderAsk(
        mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockResolvedValue(postedReply(redirectReply)) }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "ciao{Enter}");

      expect(await screen.findByText(/ask never accepts attachments here/i)).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Upload in Documents" })).toHaveAttribute("href", "/documents");
      expect(screen.queryByText(/cannot determine reliably/i)).not.toBeInTheDocument();
      expect(document.querySelector(".abstain-block")).toBeNull();
    });

    it("a genuine abstain reply renders the accent-left block with the reply's own reason", async () => {
      const abstainReply = answerReply({
        kind: "abstain",
        answerMarkdown: "Nothing in the validated contracts supports a reliable answer.",
        citations: [],
        actions: [],
        followUps: [],
      });
      renderAsk(
        mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockResolvedValue(postedReply(abstainReply)) }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");

      expect(await screen.findByText(/cannot determine reliably/i)).toBeInTheDocument();
      expect(screen.getByText(/nothing in the validated contracts supports a reliable answer/i)).toBeInTheDocument();
      expect(document.querySelector(".abstain-block")).not.toBeNull();
    });

    it("clicking a follow-up posts it as a new message in the same conversation", async () => {
      const postMessage = vi
        .fn()
        .mockResolvedValueOnce(postedReply())
        .mockResolvedValueOnce(postedReply(answerReply({ answerMarkdown: "Second answer.", followUps: [] })));
      renderAsk(mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage }));

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");
      await screen.findByText("Salesforce · MSA 2024");

      await userEvent.click(screen.getByRole("button", { name: /Where can I push on the renewal\?/ }));

      await waitFor(() => expect(postMessage).toHaveBeenCalledTimes(2));
      expect(postMessage).toHaveBeenLastCalledWith(WORKSPACE_ID, CONVERSATION_ID, { question: "Where can I push on the renewal?" });
      expect(await screen.findByText("Second answer.")).toBeInTheDocument();
    });
  });

  describe("AC-2/AC-3: citation landing (task text point (3); R-EVD-02)", () => {
    it("a tenant citation navigates to /contracts/<id>?page=<n> with state.from = 'ask'", async () => {
      renderAsk(
        mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockResolvedValue(postedReply()) }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");
      const card = await screen.findByText("Salesforce · MSA 2024");
      await userEvent.click(card.closest("button")!);

      expect(await screen.findByText(/CONTRACT_360/)).toHaveTextContent("contractId=contract-1");
      expect(screen.getByText(/CONTRACT_360/)).toHaveTextContent("search=?page=12");
      expect(screen.getByText(/CONTRACT_360/)).toHaveTextContent("from=ask");
    });

    it("a market citation opens the side panel and loads the record, showing the provenance label", async () => {
      const marketReply = answerReply({
        citations: [
          {
            n: 1,
            corpus: "market",
            title: "Sales Cloud Enterprise · CH",
            subtitle: "representative market data · mock feed · updated 2026-09-01",
            snippet: "P25 118 · P50 132 · P75 149 CHF/user/month",
            documentId: null,
            contractId: null,
            page: null,
            section: null,
            previewUrl: null,
            href: null,
            recordId: "rec-1",
          },
        ],
      });
      const getMarketRecord = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        record: {
          recordId: "rec-1",
          title: "Salesforce · Sales Cloud Enterprise",
          category: "CRM",
          geography: "CH",
          band: { p25: 118, p50: 132, p75: 149, currency: "CHF" },
          provenance: "representative market data · mock feed · updated 2026-09-01",
          updatedAt: "2026-09-01T00:00:00Z",
        },
        error: null,
      });
      renderAsk(
        mockApiClient({
          createConversation: vi.fn().mockResolvedValue(createdConversation()),
          postMessage: vi.fn().mockResolvedValue(postedReply(marketReply)),
          getMarketRecord,
        }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");
      const card = await screen.findByText("Sales Cloud Enterprise · CH");
      await userEvent.click(card.closest("button")!);

      expect(getMarketRecord).toHaveBeenCalledWith("rec-1");
      const panel = await screen.findByLabelText("Market record");
      expect(within(panel).getByText(/representative market data · mock feed · updated 2026-09-01/)).toBeInTheDocument();
    });
  });

  describe("AC-5: resume", () => {
    it("/ask/:conversationId loads the conversation and renders past turns with cards and actions clickable", async () => {
      const getConversation = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        conversation: {
          id: CONVERSATION_ID,
          title: "When does Salesforce expire?",
          scopeContractId: null,
          createdAt: "2026-09-08T00:00:00Z",
          updatedAt: "2026-09-08T00:05:00Z",
          messages: [
            {
              id: "m1",
              role: "you",
              kind: "answer",
              markdown: "When does Salesforce expire?",
              citations: [],
              actions: [],
              modelId: null,
              promptVersion: null,
              inputHash: null,
              createdAt: "2026-09-08T00:00:00Z",
            },
            {
              id: "m2",
              role: "raffa",
              kind: "answer",
              markdown: "Salesforce ends on **15 January 2027** [1].",
              citations: answerReply().citations,
              actions: answerReply().actions,
              modelId: "fixture",
              promptVersion: "answer-v2.1",
              inputHash: "abc",
              createdAt: "2026-09-08T00:00:05Z",
            },
          ],
        },
        error: null,
      });
      renderAsk(mockApiClient({ getConversation }), `/ask/${CONVERSATION_ID}`);

      // Scoped to the chat log: the conversation's own title ("When does Salesforce expire?") also
      // renders verbatim in the `ask-screen-header` h2 above the log in this fixture (the "you"
      // message and the derived title happen to coincide), so an unscoped query matches both and
      // throws "found multiple elements" -- within(log) proves the turn itself rendered, which is what
      // this test is actually about (see the AC-3 "never renders..." test above for the same pattern).
      // findByText (not getByText): the `role="log"` element itself exists a render before its
      // content does (resumeState reaching "ready" and index.tsx's own `turns` state syncing from it
      // are two separate render passes, `index.tsx`'s own header comment on `resumeState` -- so the
      // log briefly shows the empty "new chat" face first), so this has to keep polling the same log
      // node rather than reading its children exactly once.
      const log = await screen.findByRole("log");
      expect(await within(log).findByText("When does Salesforce expire?")).toBeInTheDocument();
      expect(await screen.findByText("15 January 2027")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Open Contract 360 →" })).toHaveAttribute("href", "/contracts/contract-1");
      expect(getConversation).toHaveBeenCalledWith(WORKSPACE_ID, CONVERSATION_ID);
      expect(screen.getByRole("link", { name: "+ New chat" })).toHaveAttribute("href", "/ask");
    });

    it("shows a named 'not found' state for an unknown/foreign conversation id, not a generic error", async () => {
      const getConversation = vi.fn().mockResolvedValue({ ok: false, statusCode: 404, conversation: null, error: null });
      renderAsk(mockApiClient({ getConversation }), `/ask/${CONVERSATION_ID}`);

      expect(await screen.findByText(/conversation not found/i)).toBeInTheDocument();
    });
  });
});
