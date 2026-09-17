import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import RailNav from "../../../src/components/shell/RailNav";
import type { DocumentCountsBody } from "../../../src/components/shell/useDocumentCounts";
import type { ApiClient, ConversationSummaryBody } from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

/** Minimal full `ApiClient` mock -- this suite only ever exercises `listConversations` (the rail's
 * own "last 5 conversations" slot, task E13/F09/US01/T04); every other method is a bare `vi.fn()`,
 * the same "only stub what this suite actually calls" convention every other route's own
 * `mockApiClient` helper in this repo already follows. */
function mockApiClient(listConversations: ApiClient["listConversations"] = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations: [], error: null })): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    listWorkspaces: vi.fn(),
    getWorkspaceMembers: vi.fn(),
    revokeInvitation: vi.fn(),
    removeMember: vi.fn(),
    getInvitation: vi.fn(),
    acceptInvitation: vi.fn(),
    acceptPendingInvitation: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    prioritiseDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
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
    listConversations,
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): this suite never reaches the Renewals screen -- bare
    // vi.fn() is enough, same convention as the other unexercised calls above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
  };
}

function conversation(overrides: Partial<ConversationSummaryBody> = {}): ConversationSummaryBody {
  return {
    id: "conv-1",
    title: "When does the Salesforce contract expire?",
    scopeContractId: null,
    updatedAt: "2026-09-08T00:00:00Z",
    ...overrides,
  };
}

function renderRail(
  overrides: Partial<{
    role: "admin" | "procurement";
    kbReady: boolean;
    validatedContractCount: number;
    documentCounts: DocumentCountsBody | null;
    onSignOut: () => void;
    apiClient: ApiClient;
    initialEntry: string;
  }> = {},
) {
  const {
    role = "admin",
    kbReady = false,
    validatedContractCount = 0,
    documentCounts = null,
    onSignOut = vi.fn(),
    apiClient = mockApiClient(),
    initialEntry = "/ask",
  } = overrides;
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <RailNav
        workspaceName="Acme Procurement"
        role={role}
        userLabel="user@example.test"
        kbReady={kbReady}
        validatedContractCount={validatedContractCount}
        documentCounts={documentCounts}
        onSignOut={onSignOut}
        apiClient={apiClient}
      />
    </MemoryRouter>,
  );
}

function counts(overrides: Partial<DocumentCountsBody> = {}): DocumentCountsBody {
  return { all: 0, needsAttention: 0, needsReview: 0, processing: 0, rejected: 0, ...overrides };
}

describe("RailNav (V2 two-tier rail, ADR-024 amendment; task E13/F09/US01/T01, gap G-IA-V2)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    // Task E13/F09/US01/T04: useRecentConversations reads the current workspace itself
    // (loadCurrentWorkspace()) -- every test needs one set, even the pre-existing ones above that
    // never assert on conversations, the same "shell-level hook needs a real workspace" reasoning
    // useValidatedContractCount already established for AppShell.tsx.
    window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
  });

  it("renders the workspace name and the two tiers in order: Ask Raffa, Documents, then 'From your contracts' / Portfolio, Renewals, Quote check", () => {
    const { container } = renderRail();

    expect(screen.getByText("Acme Procurement")).toBeInTheDocument();

    const labels = Array.from(container.querySelectorAll(".shell-rail-item-label")).map((node) => node.textContent);
    expect(labels).toEqual(["Ask Raffa", "Documents", "Portfolio", "Renewals", "Quote check"]);
  });

  it("has no Home or Review queue item anywhere in the rail (V2 removes both)", () => {
    renderRail();

    expect(screen.queryByText("Home")).not.toBeInTheDocument();
    expect(screen.queryByText("Review queue")).not.toBeInTheDocument();
  });

  it("Ask Raffa carries the ⌘K badge and an empty conversation slot with '+ New chat'", () => {
    renderRail();

    const askLink = screen.getByText("Ask Raffa").closest("a")!;
    expect(askLink).toHaveTextContent("⌘K");

    const newChat = screen.getByRole("link", { name: "+ New chat" });
    expect(newChat).toHaveAttribute("href", "/ask");
  });

  // Task E16/F03/US01/T01 (ADR-012 w15 §6, NW-10): the badge reads the server's `counts`, fetched
  // once by AppShell (`useDocumentCounts`) and passed down -- the sessionStorage tracker is gone.
  describe("Documents badge (`app.jsx`: needReview -> 'N to review' (attention) else docs.length -> 'N docs')", () => {
    it("shows no badge while the shell has not confirmed a count (null), never a fabricated '0 docs'", () => {
      renderRail({ documentCounts: null });

      const documentsLink = screen.getByText("Documents").closest("a")!;
      expect(documentsLink).toHaveTextContent(/^Documents$/);
    });

    it("shows no badge when the tenant holds nothing -- refused files are not held (`all` excludes Rejected)", () => {
      renderRail({ documentCounts: counts({ rejected: 2 }) });

      const documentsLink = screen.getByText("Documents").closest("a")!;
      expect(documentsLink).toHaveTextContent(/^Documents$/);
    });

    it("shows a muted 'N docs' count off counts.all when nothing needs review", () => {
      renderRail({ documentCounts: counts({ all: 1 }) });

      const documentsLink = screen.getByText("Documents").closest("a")!;
      expect(documentsLink).toHaveTextContent("1 docs");
    });

    it("shows an attention-toned 'N to review' off counts.needsReview", () => {
      renderRail({ documentCounts: counts({ all: 4, needsAttention: 3, needsReview: 1, processing: 2 }) });

      const documentsLink = screen.getByText("Documents").closest("a")!;
      expect(documentsLink).toHaveTextContent("1 to review");
      expect(documentsLink.querySelector(".shell-rail-badge")).toHaveClass("is-attention");
    });
  });

  describe("greyed secondary tier (kbReady)", () => {
    it("greys Portfolio, Renewals and Quote check and shows no count badge with 0 validated contracts", () => {
      renderRail({ kbReady: false, validatedContractCount: 0 });

      for (const label of ["Portfolio", "Renewals", "Quote check"]) {
        const link = screen.getByText(label).closest("a")!;
        expect(link).toHaveClass("is-greyed");
      }
      const portfolioLink = screen.getByText("Portfolio").closest("a")!;
      expect(portfolioLink).not.toHaveTextContent(/\d/);
    });

    it("Quote check still shows 'optional' even while greyed", () => {
      renderRail({ kbReady: false });

      expect(screen.getByText("Quote check").closest("a")).toHaveTextContent("optional");
    });

    it("un-greys the tier and badges Portfolio/Renewals with the validated count once kbReady", () => {
      renderRail({ kbReady: true, validatedContractCount: 3 });

      for (const label of ["Portfolio", "Renewals", "Quote check"]) {
        const link = screen.getByText(label).closest("a")!;
        expect(link).not.toHaveClass("is-greyed");
      }
      expect(screen.getByText("Portfolio").closest("a")).toHaveTextContent("3");
      expect(screen.getByText("Renewals").closest("a")).toHaveTextContent("3");
      expect(screen.getByText("Quote check").closest("a")).toHaveTextContent("optional");
    });
  });

  describe("footer", () => {
    it("shows Workspace & members, the role label, and Sign out for a Workspace Admin", () => {
      renderRail({ role: "admin" });

      expect(screen.getByText("Workspace & members")).toBeInTheDocument();
      expect(screen.getByText("Workspace Admin")).toBeInTheDocument();
    });

    it("hides Workspace & members for Procurement (AC-2)", () => {
      renderRail({ role: "procurement" });

      expect(screen.queryByText("Workspace & members")).not.toBeInTheDocument();
      expect(screen.getByText("Portfolio")).toBeInTheDocument();
      expect(screen.getByText("Procurement")).toBeInTheDocument();
    });

    it("calls onSignOut when the footer's Sign out control is clicked", async () => {
      const onSignOut = vi.fn();
      renderRail({ onSignOut });

      await userEvent.click(screen.getByRole("button", { name: /sign out/i }));

      expect(onSignOut).toHaveBeenCalledTimes(1);
    });
  });

  describe("recent conversations slot (R-CONV-02; task E13/F09/US01/T04, gap G-CONVERSATIONS)", () => {
    it("calls listConversations once on mount, for the current workspace", () => {
      const listConversations = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations: [], error: null });
      renderRail({ apiClient: mockApiClient(listConversations) });

      expect(listConversations).toHaveBeenCalledWith(WORKSPACE_ID);
    });

    it("lists every returned conversation, in the order the API returned them, above '+ New chat'", async () => {
      const conversations = [
        conversation({ id: "conv-1", title: "When does Salesforce expire?" }),
        conversation({ id: "conv-2", title: "What is our AWS liability cap?" }),
      ];
      const listConversations = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations, error: null });
      const { container } = renderRail({ apiClient: mockApiClient(listConversations) });

      await screen.findByRole("link", { name: "When does Salesforce expire?" });

      const conversationSlot = container.querySelector(".shell-rail-conversations")!;
      const linkNames = Array.from(conversationSlot.querySelectorAll("a")).map((node) => node.textContent);
      expect(linkNames).toEqual(["When does Salesforce expire?", "What is our AWS liability cap?", "+ New chat"]);
    });

    it("resumes by click -- each conversation link points at /ask/<id>", async () => {
      const listConversations = vi
        .fn()
        .mockResolvedValue({ ok: true, statusCode: 200, conversations: [conversation({ id: "conv-42" })], error: null });
      renderRail({ apiClient: mockApiClient(listConversations) });

      const link = await screen.findByRole("link", { name: conversation().title });
      expect(link).toHaveAttribute("href", "/ask/conv-42");
    });

    it("marks the conversation matching the current /ask/:id route active, in accent", async () => {
      const listConversations = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        conversations: [conversation({ id: "conv-1" }), conversation({ id: "conv-2", title: "Second chat" })],
        error: null,
      });
      renderRail({ apiClient: mockApiClient(listConversations), initialEntry: "/ask/conv-2" });

      const active = await screen.findByRole("link", { name: "Second chat" });
      expect(active).toHaveClass("is-active");
      const inactive = screen.getByRole("link", { name: conversation().title });
      expect(inactive).not.toHaveClass("is-active");
    });

    it("marks no conversation active on the plain /ask (new chat) route", async () => {
      const listConversations = vi
        .fn()
        .mockResolvedValue({ ok: true, statusCode: 200, conversations: [conversation({ id: "conv-1" })], error: null });
      renderRail({ apiClient: mockApiClient(listConversations), initialEntry: "/ask" });

      const link = await screen.findByRole("link", { name: conversation().title });
      expect(link).not.toHaveClass("is-active");
    });

    it("renders no conversation links, only '+ New chat', when the tenant has none yet", () => {
      renderRail({ apiClient: mockApiClient(vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations: [], error: null })) });

      expect(screen.queryByText(/expire\?/)).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "+ New chat" })).toBeInTheDocument();
    });
  });
});
