import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation, useParams } from "react-router-dom";
import AskRoute from "../../../src/routes/ask";
import { DocumentViewerProvider } from "../../../src/routes/documents/viewer/DocumentViewerOverlay";
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

// Task E14/F03/US02/T01 (wave w14): AskRoute never calls apiClient.getPortfolio at all (confirmed
// against src/routes/ask/index.tsx) -- useValidatedContractCount reads listWorkspaces() instead (see
// validatedWorkspace() below), and no other code path in this screen reaches Portfolio. This mock
// value is kept resolved (not deleted outright) only so mockApiClient's own getPortfolio stub stays
// harmless if something is ever wired to it; there is deliberately no off/empty variant to keep in
// step, since nothing here reads it.
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
      processingDocumentCount: 0,
    },
    error: null,
  };
}

function emptyDocuments(): ListDocumentsResult {
  return {
    ok: true,
    statusCode: 200,
    page: { items: [], page: 1, pageSize: 1, totalCount: 0, counts: { all: 0, needsAttention: 0, needsReview: 0, processing: 0, rejected: 0 } },
    error: null,
  };
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
    payload: null,
    ...overrides,
  };
}

/**
 * Task E14/F03/US02/T01 (wave w14): useValidatedContractCount now reads listWorkspaces() instead of
 * counting getPortfolio() client-side (see that hook's own header comment) -- this is the one row
 * this whole suite's default "on, 1 validated contract" expectation (the scope-line assertion below)
 * now comes from; the three "off state" tests further down override it to a 0-contractCount row
 * instead of overriding getPortfolio, which AskRoute itself never calls (getPortfolio's own base
 * default below is genuinely unused dead weight from before this task -- left resolved rather than
 * bare so it stays harmless if something ever does call it).
 */
function validatedWorkspace(contractCount = 1) {
  return {
    ok: true,
    statusCode: 200,
    workspaces: [{ id: WORKSPACE_ID, name: "Acme Procurement", createdAt: "2026-01-01T00:00:00Z", role: "Admin", contractCount }],
    error: null,
  };
}

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    listWorkspaces: vi.fn().mockResolvedValue(validatedWorkspace()),
    getWorkspaceMembers: vi.fn(),
    getWorkspaceSettings: vi.fn(),
    updateWorkspaceSettings: vi.fn(),
    revokeInvitation: vi.fn(),
    removeMember: vi.fn(),
    getInvitation: vi.fn(),
    acceptInvitation: vi.fn(),
    acceptPendingInvitation: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listDocuments: vi.fn().mockResolvedValue(emptyDocuments()),
    getDocumentPreviewUrl: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, objectUrl: null, error: "No preview." }),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    deleteAllDocuments: vi.fn(),
    prioritiseDocument: vi.fn(),
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
    getContractStrategy: vi.fn(),
    validateDocument: vi.fn(),
    postRenewalAction: vi.fn(),
    getQuote: vi.fn(),
    getNegotiationSteps: vi.fn(),
    putNegotiationSteps: vi.fn(),
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
    postConversationFeedback: vi.fn(),
    deleteConversation: vi.fn(),
    getCapabilities: vi.fn().mockResolvedValue(emptyCatalog()),
    getMarketRecord: vi.fn(),
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): this suite never reaches the Renewals screen -- bare
    // vi.fn() is enough, same convention as the other unexercised calls above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
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
      <DocumentViewerProvider apiClient={apiClient}>
        <PathProbe />
        <Routes>
          <Route path="/ask" element={<AskRoute apiClient={apiClient} />} />
          <Route path="/ask/:conversationId" element={<AskRoute apiClient={apiClient} />} />
          <Route path="/contracts/:contractId" element={<Contract360Stub />} />
          <Route path="/renewals" element={<div>RENEWALS SCREEN</div>} />
        </Routes>
      </DocumentViewerProvider>
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
      renderAsk(mockApiClient({ listWorkspaces: vi.fn().mockResolvedValue(validatedWorkspace(0)), listDocuments: vi.fn().mockResolvedValue(emptyDocuments()) }));

      expect(await screen.findByText("Ask needs at least one validated contract.")).toBeInTheDocument();
      expect(screen.getByText(/upload a contract first/i)).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Upload a contract" })).toHaveAttribute("href", "/documents");
    });

    it("shows the 'still processing' reason and 'Go to Documents' when the server counts a document in flight", async () => {
      // Task E16/F03/US01/T01 (ADR-012 w15 §4): the variant is read off the server's `counts`,
      // never off `totalCount` -- a tenant whose only document *failed* must not be told it is
      // "still processing".
      const listDocuments = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        page: { items: [], page: 1, pageSize: 1, totalCount: 1, counts: { all: 1, needsAttention: 1, needsReview: 0, processing: 1, rejected: 0 } },
        error: null,
      });
      renderAsk(mockApiClient({ listWorkspaces: vi.fn().mockResolvedValue(validatedWorkspace(0)), listDocuments }));

      expect(await screen.findByText(/still processing or waiting for review/i)).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Go to Documents" })).toHaveAttribute("href", "/documents");
    });

    it("shows the third variant -- 'could not finish processing' -- when documents are held but none is in flight or validated", async () => {
      const listDocuments = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        page: { items: [], page: 1, pageSize: 1, totalCount: 1, counts: { all: 1, needsAttention: 1, needsReview: 0, processing: 0, rejected: 0 } },
        error: null,
      });
      renderAsk(mockApiClient({ listWorkspaces: vi.fn().mockResolvedValue(validatedWorkspace(0)), listDocuments }));

      expect(await screen.findByText(/could not finish processing your documents/i)).toBeInTheDocument();
      expect(screen.queryByText(/still processing/i)).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Go to Documents" })).toHaveAttribute("href", "/documents");
    });

    it("never calls createConversation/postMessage while off", async () => {
      const createConversation = vi.fn();
      renderAsk(mockApiClient({ listWorkspaces: vi.fn().mockResolvedValue(validatedWorkspace(0)), createConversation }));

      await screen.findByText("Ask needs at least one validated contract.");
      expect(createConversation).not.toHaveBeenCalled();
    });
  });

  describe("AC-1/task text (2): new chat", () => {
    it("shows the hello line, the intro, the four starter groups, the header's scope line and the two composer chips", async () => {
      renderAsk(mockApiClient());

      expect(await screen.findByText("What do you want to know?")).toBeInTheDocument();
      expect(screen.getByText(/I work on procurement only/)).toBeInTheDocument();
      // `askScopeShort` in the conversation header: the validated count (from the shell hook); no
      // supplier clause, since this fixture's one validated contract names no supplier.
      expect(screen.getByText("1 validated contract")).toBeInTheDocument();
      expect(screen.getByRole("heading", { name: "Ask Raffa.ai · new chat" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "+ New chat" })).toBeInTheDocument();
      // Starter groups (Save · Dates · Negotiate · Risk): the supplier-templated questions are left
      // out while no validated supplier name is known -- never a placeholder question.
      expect(screen.getByText("Save")).toBeInTheDocument();
      expect(screen.getByText("Dates")).toBeInTheDocument();
      expect(screen.getByText("Risk")).toBeInTheDocument();
      expect(screen.queryByText("Negotiate")).toBeNull();
      expect(screen.getByRole("button", { name: "Where can I save the most this quarter?" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Which contracts renew in the next 120 days?" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Which contracts have uncapped liability?" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /give notice to/ })).toBeNull();
      // Composer chips + note.
      expect(screen.getByRole("button", { name: "What can Raffa do?" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "When does this contract expire?" })).toBeInTheDocument();
      expect(screen.getByText("Procurement only · cites its sources")).toBeInTheDocument();
    });

    it("names the first validated supplier in the header scope line and the starter questions", async () => {
      const portfolio = validatedPortfolio();
      portfolio.portfolio!.items[0].supplierName = "Salesforce";
      renderAsk(mockApiClient({ getPortfolio: vi.fn().mockResolvedValue(portfolio) }));

      expect(await screen.findByText("1 validated contract · Salesforce")).toBeInTheDocument();
      expect(screen.getByText("Negotiate")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "When must we give notice to Salesforce?" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Prepare the renegotiation email for Salesforce" })).toBeInTheDocument();
    });

    it("clicking a starter asks it directly", async () => {
      const createConversation = vi.fn().mockResolvedValue(createdConversation());
      const postMessage = vi.fn().mockResolvedValue(postedReply());
      renderAsk(mockApiClient({ createConversation, postMessage }));

      await userEvent.click(await screen.findByRole("button", { name: "Where can I save the most this quarter?" }));

      await waitFor(() => expect(postMessage).toHaveBeenCalledWith(WORKSPACE_ID, CONVERSATION_ID, { question: "Where can I save the most this quarter?" }));
      // The header now carries the conversation's own title (the first question, the server's rule).
      expect(screen.getByRole("heading", { name: "Where can I save the most this quarter?" })).toBeInTheDocument();
      expect(screen.queryByText("What do you want to know?")).toBeNull();
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
    it("renders bold markdown, one evidence card (the supplier named, the row labelled), and an action button", async () => {
      renderAsk(
        mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockResolvedValue(postedReply()) }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");

      expect(await screen.findByText("15 January 2027")).toBeInTheDocument();
      expect(screen.getByText("Your contracts")).toBeInTheDocument();
      // The card names the supplier as the group and keeps only what the row adds ("MSA 2024").
      expect(document.querySelector(".evidence-group-title")).toHaveTextContent("Salesforce");
      expect(screen.getByText("MSA 2024")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Salesforce · Contract 360 →" })).toHaveAttribute("href", "/contracts/contract-1");
      expect(screen.getByRole("button", { name: /Where can I push on the renewal\?/ })).toBeInTheDocument();
      expect(screen.queryByText(/cannot determine reliably/i)).not.toBeInTheDocument();
    });

    it("never renders 'Structured query…'/'Clause retrieval…' or a raw guid anywhere", async () => {
      renderAsk(
        mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockResolvedValue(postedReply()) }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");
      await screen.findByText("MSA 2024");

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

    it("an abstain reply renders the server's proposal as plain prose -- never the 'I don't have data I trust' banner", async () => {
      const abstainReply = answerReply({
        kind: "abstain",
        answerMarkdown: "Here's how to prepare the renewal: name the supplier and I'll draft a ready-to-send email.",
        citations: [],
        actions: [],
        followUps: [],
      });
      renderAsk(
        mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockResolvedValue(postedReply(abstainReply)) }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");

      expect(await screen.findByText(/here's how to prepare the renewal/i)).toBeInTheDocument();
      expect(screen.queryByText(/I don't have data I trust enough to answer/)).toBeNull();
      expect(document.querySelector(".abstain-block")).toBeNull();
    });

    it("clicking a follow-up posts it as a new message in the same conversation", async () => {
      const postMessage = vi
        .fn()
        .mockResolvedValueOnce(postedReply())
        .mockResolvedValueOnce(postedReply(answerReply({ answerMarkdown: "Second answer.", followUps: [] })));
      renderAsk(mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage }));

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");
      await screen.findByText("MSA 2024");

      await userEvent.click(screen.getByRole("button", { name: /Where can I push on the renewal\?/ }));

      await waitFor(() => expect(postMessage).toHaveBeenCalledTimes(2));
      expect(postMessage).toHaveBeenLastCalledWith(WORKSPACE_ID, CONVERSATION_ID, { question: "Where can I push on the renewal?" });
      expect(await screen.findByText("Second answer.")).toBeInTheDocument();
    });
  });

  // ADR-030: an ambiguous question is interviewed; a chip answers it by key with the label as the
  // transcript line; a typed reply while the interview is pending answers it as free text.
  describe("interview (ADR-030)", () => {
    const interviewReply = () =>
      answerReply({
        kind: "interview",
        messageId: "msg-interview",
        answerMarkdown: "Before I answer, one quick check.",
        citations: [],
        actions: [],
        followUps: [],
        interview: {
          prompt: "Before I answer, one quick check.",
          answered: false,
          questions: [
            {
              key: "interpretation",
              prompt: "Which of these do you mean?",
              presentation: "choice",
              allowFreeText: true,
              options: [
                { key: "portfolio-overview", label: "The most critical contracts and where we can save", hint: null },
                { key: "renewals-window", label: "The contracts renewing in the next 120 days", hint: null },
              ],
            },
          ],
        },
      });

    it("renders the chips and a click posts the option by key with its label as the question", async () => {
      const postMessage = vi.fn().mockResolvedValueOnce(postedReply(interviewReply())).mockResolvedValueOnce(postedReply());
      renderAsk(mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage }));

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "Did you over all my contract?{Enter}");
      const chip = await screen.findByRole("button", { name: /renewing in the next 120 days/ });
      expect(screen.queryByText("I don't have data I trust enough to answer.")).toBeNull();

      await userEvent.click(chip);

      await waitFor(() =>
        expect(postMessage).toHaveBeenLastCalledWith(WORKSPACE_ID, CONVERSATION_ID, {
          question: "The contracts renewing in the next 120 days",
          interviewAnswer: { messageId: "msg-interview", questionKey: "interpretation", optionKey: "renewals-window" },
        }),
      );
      // The chips stay on screen but can no longer be clicked; the transcript shows the label.
      expect((chip as HTMLButtonElement).disabled).toBe(true);
      expect(screen.getByText("The contracts renewing in the next 120 days")).toBeInTheDocument();
      expect(await screen.findByText("15 January 2027")).toBeInTheDocument();
    });

    it("typing while an interview is pending answers it as free text", async () => {
      const postMessage = vi.fn().mockResolvedValueOnce(postedReply(interviewReply())).mockResolvedValueOnce(postedReply());
      renderAsk(mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage }));

      const input = await screen.findByRole("textbox", { name: /ask raffa a question/i });
      await userEvent.type(input, "Did you over all my contract?{Enter}");
      await screen.findByRole("button", { name: /renewing in the next 120 days/ });

      await userEvent.type(input, "the renewals{Enter}");

      await waitFor(() =>
        expect(postMessage).toHaveBeenLastCalledWith(WORKSPACE_ID, CONVERSATION_ID, {
          question: "the renewals",
          interviewAnswer: { messageId: "msg-interview", questionKey: "interpretation", freeText: true },
        }),
      );
    });
  });

  describe("AC-2/AC-3: citation landing (task text point (3); R-EVD-02)", () => {
    it("a tenant citation navigates to /contracts/<id>?page=<n> with state.from = 'ask'", async () => {
      renderAsk(
        mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage: vi.fn().mockResolvedValue(postedReply()) }),
      );

      await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");
      await screen.findByText("MSA 2024");
      await userEvent.click(screen.getByRole("button", { name: "Open source 1" }));

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
      await screen.findByText("Sales Cloud Enterprise · CH");
      await userEvent.click(screen.getByRole("button", { name: "Open source 1" }));

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
      // renders verbatim in the conversation header's h2 above the log in this fixture (the "you"
      // message and the server's title happen to coincide), so an unscoped query matches both and
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
      expect(screen.getByRole("link", { name: "Salesforce · Contract 360 →" })).toHaveAttribute("href", "/contracts/contract-1");
      expect(getConversation).toHaveBeenCalledWith(WORKSPACE_ID, CONVERSATION_ID);
      expect(screen.queryByRole("link", { name: "+ New chat" })).not.toBeInTheDocument();
      // `convTitle`: the resumed conversation's own server-side title in the header.
      expect(screen.getByRole("heading", { name: "When does Salesforce expire?" })).toBeInTheDocument();
    });

    it("shows a named 'not found' state for an unknown/foreign conversation id, not a generic error", async () => {
      const getConversation = vi.fn().mockResolvedValue({ ok: false, statusCode: 404, conversation: null, error: null });
      renderAsk(mockApiClient({ getConversation }), `/ask/${CONVERSATION_ID}`);

      expect(await screen.findByText(/conversation not found/i)).toBeInTheDocument();
    });
  });

  // ADR-030 D5: the feedback card's one call, and the confirmation turn it appends.
  describe("ADR-030: feedback card", () => {
    const offer = {
      prompt: "Want to report this to the Raffa.ai team so they can build it?",
      yesLabel: "Yes", noLabel: "No", nextLabel: "Next", backLabel: "Back", submitLabel: "Send",
      sendingLabel: "Sending…", thanksLabel: "Thanks!", errorLabel: "Try again.", publicNotice: "Public on GitHub.",
      questions: [
        { key: "what" as const, kind: "text" as const, label: "What exactly should Raffa do?", prefill: "Export to Excel", choices: null },
        { key: "frequency" as const, kind: "choice" as const, label: "How often?", prefill: null, choices: [{ key: "weekly", label: "every week" }] },
        { key: "importance" as const, kind: "choice" as const, label: "How important?", prefill: null, choices: [{ key: "blocking", label: "blocking" }] },
      ],
    };

    it("submitting feedback posts to the feedback endpoint and appends the confirmation turn", async () => {
      const gapReply = answerReply({
        messageId: "msg-gap",
        kind: "redirect",
        answerMarkdown: "I can't export files from Raffa.ai yet, but Portfolio shows the same data.",
        citations: [],
        actions: [{ label: "Open Portfolio →", href: "/contracts", kind: "navigate" }],
        followUps: [],
        payload: { gap: { key: "export-file", title: "Export to Excel or Word", language: "en" }, draft: null, feedbackOffer: offer, feedbackResult: null },
      });
      const postConversationFeedback = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 201,
        result: {
          feedbackId: "fb-1",
          status: "issue_opened",
          issueNumber: 42,
          issueUrl: "https://github.com/lucalamalfa91/Raffa.ai/issues/42",
          message: {
            id: "msg-confirm", role: "raffa", kind: "answer", markdown: "Thanks, I opened issue #42 for the Raffa.ai team.",
            citations: [], actions: [{ label: "Open issue #42 →", href: "https://github.com/lucalamalfa91/Raffa.ai/issues/42", kind: "external" }],
            modelId: null, promptVersion: null, inputHash: null, createdAt: "2026-09-22T00:00:10Z",
            payload: { gap: null, draft: null, feedbackOffer: null, feedbackResult: { forMessageId: "msg-gap", status: "issue_opened", issueNumber: 42, issueUrl: "https://github.com/lucalamalfa91/Raffa.ai/issues/42" } },
          },
        },
        error: null,
      });
      const user = userEvent.setup();
      renderAsk(
        mockApiClient({
          createConversation: vi.fn().mockResolvedValue(createdConversation()),
          postMessage: vi.fn().mockResolvedValue(postedReply(gapReply)),
          postConversationFeedback,
        }),
      );

      await user.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "Export my contracts to Excel{Enter}");

      await user.click(await screen.findByRole("button", { name: "Yes" }));
      await user.click(screen.getByRole("button", { name: "Next" }));
      await user.click(screen.getByRole("button", { name: "every week" }));
      await user.click(screen.getByRole("button", { name: "Next" }));
      await user.click(screen.getByRole("button", { name: "blocking" }));
      await user.click(screen.getByRole("button", { name: "Send" }));

      await waitFor(() =>
        expect(postConversationFeedback).toHaveBeenCalledWith(WORKSPACE_ID, CONVERSATION_ID, {
          messageId: "msg-gap",
          answers: { what: "Export to Excel", frequency: "weekly", importance: "blocking" },
        }),
      );
      expect(await screen.findByText("Thanks, I opened issue #42 for the Raffa.ai team.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Open issue #42 →" })).toHaveAttribute("target", "_blank");
      // The answered offer never re-opens.
      await waitFor(() => expect(screen.queryByText(offer.prompt)).not.toBeInTheDocument());
    });
  });

  // ADR-031: the capability check runs beside the answer; its proposal is a separate turn after it.
  describe("ADR-031: capability follow-up", () => {
    const proposalOffer = {
      prompt: "Want to propose “Management reports” as a new Raffa.ai feature?",
      yesLabel: "Yes", noLabel: "No", nextLabel: "Next", backLabel: "Back", submitLabel: "Send",
      sendingLabel: "Sending…", thanksLabel: "Thanks!", errorLabel: "Try again.", publicNotice: "Public on GitHub.",
      questions: [
        { key: "what" as const, kind: "text" as const, label: "What exactly should Raffa do?", prefill: "Generate a report.", choices: null },
      ],
    };

    function followUpMessage(forMessageId: string) {
      return {
        id: "msg-follow-up", role: "raffa" as const, kind: "redirect" as const,
        markdown: "I checked what Raffa.ai can do for your request. I can't generate a report for management from Raffa.ai yet.",
        citations: [], actions: [{ label: "Portfolio →", href: "/contracts", kind: "navigate" as const }],
        modelId: null, promptVersion: null, inputHash: null, createdAt: "2026-09-23T00:00:05Z",
        payload: {
          gap: { key: "discovered:management-report", title: "Management reports", language: "en" as const },
          draft: null, feedbackOffer: proposalOffer, feedbackResult: null,
          followUps: ["What is our total annual spend across contracts?"],
          capabilityCheckFor: forMessageId,
        },
      };
    }

    it("shows the answer, then the proposal the reply carried as a separate turn with its chips and card", async () => {
      const user = userEvent.setup();
      renderAsk(
        mockApiClient({
          createConversation: vi.fn().mockResolvedValue(createdConversation()),
          postMessage: vi.fn().mockResolvedValue(postedReply(answerReply({ followUpMessage: followUpMessage("msg-1") }))),
        }),
      );

      await user.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "Can you write a report for my boss?{Enter}");

      expect(await screen.findByText(/I checked what Raffa.ai can do for your request/)).toBeInTheDocument();
      expect(screen.getByText(proposalOffer.prompt)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "What is our total annual spend across contracts? →" })).toBeInTheDocument();
      // The standard answer is still there, first.
      expect(screen.getByText(/Salesforce ends on/)).toBeInTheDocument();
    });

    it("looks for a pending follow-up in the conversation and appends it once it is stored", async () => {
      const user = userEvent.setup();
      const getConversation = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        conversation: {
          id: CONVERSATION_ID, title: "Report", scopeContractId: null, createdAt: "2026-09-23T00:00:00Z", updatedAt: "2026-09-23T00:00:05Z",
          messages: [followUpMessage("msg-1")],
        },
        error: null,
      });
      renderAsk(
        mockApiClient({
          createConversation: vi.fn().mockResolvedValue(createdConversation()),
          postMessage: vi.fn().mockResolvedValue(postedReply(answerReply({ capabilityCheck: "pending", followUpMessage: null }))),
          getConversation,
        }),
      );

      await user.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "Can you write a report for my boss?{Enter}");

      expect(await screen.findByText(/Salesforce ends on/)).toBeInTheDocument();
      expect(await screen.findByText(/I checked what Raffa.ai can do for your request/, undefined, { timeout: 5000 })).toBeInTheDocument();
      expect(getConversation).toHaveBeenCalledWith(WORKSPACE_ID, CONVERSATION_ID);
    });
  });

  /**
   * Task E27/F04/US01/T01 (binding-chip; parent story us-01-binding-chip AC-1/AC-2/AC-3; closes
   * NW-78, ADR-012 cl. 49 / ADR-020 37.2 per `reports/architecture/waves/w19.md`; screens-v2.md #2
   * "scope line" as anchor). `askViewModel.test.ts` proves the pure chip-building/fetch logic in
   * isolation; this describe block proves the one thing that cannot -- that the real, mounted
   * `AskRoute` actually wires `boundContractId`/`boundContractChip` into the screen: the chip
   * appears above the thread and links to Contract 360, both freshly created (AC-1) and resumed
   * with no `?scope=` in the URL at all (AC-2), and stays off the screen while the bound contract
   * has not resolved (AC-3).
   */
  describe("NW-78: persistent binding chip", () => {
    /** Minimal `GetContract360Result` shape -- only the three header fields
     * `askViewModel.ts#buildBoundContractChip` reads, the same "cast rather than fill every
     * generated field" convention `GlobalAskBar.test.tsx#contract360Result` already establishes for
     * this identical wire shape. */
    function boundContract360Result(): unknown {
      return {
        ok: true,
        statusCode: 200,
        contract: { header: { supplierName: "Acme Corp", supplierId: "s-1", type: "Msa" } },
        error: null,
      };
    }

    it("AC-1: a freshly scoped conversation shows the chip once created, linking to Contract 360", async () => {
      const createConversation = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 201,
        conversation: { id: CONVERSATION_ID, title: "New chat", scopeContractId: "contract-1", updatedAt: "2026-09-08T00:00:00Z" },
        error: null,
      });
      const postMessage = vi.fn().mockResolvedValue(postedReply());
      const getContract360 = vi.fn().mockResolvedValue(boundContract360Result());
      renderAsk(mockApiClient({ createConversation, postMessage, getContract360 }), "/ask?scope=contract-1");

      const input = await screen.findByRole("textbox", { name: /ask raffa a question/i });
      await userEvent.type(input, "When must we give notice?{Enter}");

      const chip = await screen.findByRole("link", { name: "Acme Corp · MSA" });
      expect(chip).toHaveAttribute("href", "/contracts/contract-1");
      expect(getContract360).toHaveBeenCalledWith(WORKSPACE_ID, "contract-1");

      await userEvent.click(chip);
      expect(await screen.findByText(/CONTRACT_360/)).toHaveTextContent("contractId=contract-1");
    });

    it("AC-2 survives resume: a resumed conversation shows the chip from its own persisted scopeContractId -- the resumed URL carries no `?scope=` at all", async () => {
      const getConversation = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        conversation: {
          id: CONVERSATION_ID,
          title: "When does Salesforce expire?",
          scopeContractId: "contract-1",
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
          ],
        },
        error: null,
      });
      const getContract360 = vi.fn().mockResolvedValue(boundContract360Result());
      // `/ask/${CONVERSATION_ID}` -- deliberately no `?scope=` query string, proving the chip cannot
      // be reading one.
      renderAsk(mockApiClient({ getConversation, getContract360 }), `/ask/${CONVERSATION_ID}`);

      const chip = await screen.findByRole("link", { name: "Acme Corp · MSA" });
      expect(chip).toHaveAttribute("href", "/contracts/contract-1");
      expect(getContract360).toHaveBeenCalledWith(WORKSPACE_ID, "contract-1");
    });

    it("AC-3: stays unrendered while the bound contract cannot be resolved", async () => {
      const getConversation = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        conversation: {
          id: CONVERSATION_ID,
          title: "New chat",
          scopeContractId: "contract-1",
          createdAt: "2026-09-08T00:00:00Z",
          updatedAt: "2026-09-08T00:05:00Z",
          messages: [],
        },
        error: null,
      });
      const getContract360 = vi.fn().mockResolvedValue({ ok: false, statusCode: 404, contract: null, error: "No contract found." });
      renderAsk(mockApiClient({ getConversation, getContract360 }), `/ask/${CONVERSATION_ID}`);

      await screen.findByRole("log");
      await waitFor(() => expect(getContract360).toHaveBeenCalledWith(WORKSPACE_ID, "contract-1"));
      expect(screen.queryByRole("link", { name: "Acme Corp · MSA" })).not.toBeInTheDocument();
    });
  });

  it("a viewer citation opens the document overlay on Ask without leaving the conversation", async () => {
    const viewerReply = answerReply({
      citations: [
        {
          n: 1,
          corpus: "tenant",
          title: "Northwind · MSA",
          subtitle: "p.2",
          snippet: "The initial term is thirty-six (36) months",
          documentId: "doc-1",
          contractId: "contract-1",
          page: 2,
          section: null,
          previewUrl: null,
          href: "/documents/doc-1/viewer?page=2",
          recordId: null,
        },
      ],
    });
    const getDocument = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      document: {
        id: "doc-1",
        contractId: "contract-1",
        fileName: "raffa-sample-northwind-msa.pdf",
        mimeType: "application/pdf",
        documentType: "Msa",
        processingStatus: "Completed",
        createdAt: "2026-09-01T00:00:00Z",
        pageCount: 2,
        isPageCountLimited: false,
      },
      error: null,
    });
    const getDocumentPreviewUrl = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, objectUrl: "blob:page", error: null });
    renderAsk(
      mockApiClient({
        createConversation: vi.fn().mockResolvedValue(createdConversation()),
        postMessage: vi.fn().mockResolvedValue(postedReply(viewerReply)),
        getDocument,
        getDocumentPreviewUrl,
      }),
    );

    await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "…{Enter}");
    await screen.findAllByText("Northwind");
    await userEvent.click(screen.getByRole("link", { name: "Open at this span" }));

    expect(await screen.findByRole("dialog", { name: "Document viewer" })).toBeInTheDocument();
    expect(screen.getByTestId("path").textContent).toMatch(/^\/ask/);
    expect(screen.queryByText(/CONTRACT_360/)).not.toBeInTheDocument();
  });
});

describe("AskRoute web research consent (ADR-030)", () => {
  const consentReply = () =>
    answerReply({
      kind: "interview",
      messageId: "msg-consent",
      answerMarkdown: "Raffa will search the public web for: “typical uplift caps on saas renewals”. Allow?",
      citations: [],
      actions: [],
      followUps: [],
      interview: {
        prompt: "Raffa will search the public web for: “typical uplift caps on saas renewals”. Allow?",
        answered: false,
        questions: [
          {
            key: "web-consent",
            prompt: "Raffa will search the public web for: “typical uplift caps on saas renewals”. Nothing from your contracts leaves Raffa. The results are not verified. Allow?",
            presentation: "consent",
            allowFreeText: false,
            options: [
              { key: "allow", label: "Yes, search the web", hint: "One search, for this question only." },
              { key: "decline", label: "No, stay in Raffa", hint: "I answer from your contracts only." },
            ],
          },
        ],
      },
    });

  const webAnswer = () =>
    answerReply({
      messageId: "msg-web-answer",
      answerMarkdown: "Public, unverified: a 5-10% uplift cap is common [1].",
      citations: [
        { n: 1, corpus: "web", title: "example.com · SaaS renewals", subtitle: null, snippet: "5-10% uplift cap", documentId: null, contractId: null, page: null, section: null, previewUrl: null, href: "https://example.com/a", recordId: null },
      ],
      actions: [{ label: "Quote check →", href: "/quotes", kind: "navigate" }],
      provenance: { sources: ["web"], modelId: "gpt-research", promptVersion: "research-v1", inputHash: "h", unverified: true },
      followUps: [],
    });

  it("shows the alert dialog for a consent question and Allow posts the allow option by key", async () => {
    const postMessage = vi.fn().mockResolvedValueOnce(postedReply(consentReply())).mockResolvedValueOnce(postedReply(webAnswer()));
    renderAsk(mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage }));

    await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "search the web for typical uplift caps on saas renewals{Enter}");

    const dialog = await screen.findByRole("alertdialog", { name: "Search the public web?" });
    expect(dialog).toHaveAccessibleDescription(/typical uplift caps on saas renewals/);
    expect(screen.getByRole("button", { name: "No, stay in Raffa" })).toHaveFocus();

    await userEvent.click(within(dialog).getByRole("button", { name: "Yes, search the web" }));

    await waitFor(() =>
      expect(postMessage).toHaveBeenLastCalledWith(WORKSPACE_ID, CONVERSATION_ID, {
        question: "Yes, search the web",
        interviewAnswer: { messageId: "msg-consent", questionKey: "web-consent", optionKey: "allow" },
      }),
    );
    // The dialog is gone, the answer is labelled unverified and its source opens in a new tab.
    await screen.findByText("Public web · not verified.");
    expect(screen.queryByRole("alertdialog")).toBeNull();
    const link = screen.getByRole("link", { name: "example.com ↗" });
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("Decline posts the decline option and never the allow one", async () => {
    const postMessage = vi.fn().mockResolvedValueOnce(postedReply(consentReply())).mockResolvedValueOnce(postedReply());
    renderAsk(mockApiClient({ createConversation: vi.fn().mockResolvedValue(createdConversation()), postMessage }));

    await userEvent.type(await screen.findByRole("textbox", { name: /ask raffa a question/i }), "search the web for typical uplift caps on saas renewals{Enter}");
    const dialog = await screen.findByRole("alertdialog", { name: "Search the public web?" });

    await userEvent.click(within(dialog).getByRole("button", { name: "No, stay in Raffa" }));

    await waitFor(() =>
      expect(postMessage).toHaveBeenLastCalledWith(WORKSPACE_ID, CONVERSATION_ID, {
        question: "No, stay in Raffa",
        interviewAnswer: { messageId: "msg-consent", questionKey: "web-consent", optionKey: "decline" },
      }),
    );
    expect(postMessage).toHaveBeenCalledTimes(2);
    expect(await screen.findByText("15 January 2027")).toBeInTheDocument();
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });
});
