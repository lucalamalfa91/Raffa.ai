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
    getWorkspaceSettings: vi.fn(),
    updateWorkspaceSettings: vi.fn(),
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
    deleteAllDocuments: vi.fn(),
    prioritiseDocument: vi.fn(),
    getPortfolio: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, portfolio: { items: [], page: 1, pageSize: 100, totalCount: 0 }, error: null }),
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
    postConversationFeedback: vi.fn(),
    deleteConversation: vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null }),
    renameConversation: vi.fn(),
    restoreConversation: vi.fn(),
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

  it("renders the workspace name and the two tiers in order: Ask Raffa, Documents, then 'From your contracts' / Portfolio, Renewals, Savings, Quote check", () => {
    const { container } = renderRail();

    expect(screen.getByText("Acme Procurement")).toBeInTheDocument();

    const labels = Array.from(container.querySelectorAll(".shell-rail-item-label")).map((node) => node.textContent);
    expect(labels).toEqual(["Ask Raffa", "Documents", "Portfolio", "Renewals", "Savings", "Quote check"]);
  });

  it("Savings is a visible first-class rail destination at /savings, with the same secondary-item chrome as Portfolio", () => {
    renderRail({ initialEntry: "/savings" });

    const savingsLink = screen.getByRole("link", { name: "Savings" });
    expect(savingsLink).toHaveAttribute("href", "/savings");
    expect(savingsLink).toHaveClass("shell-rail-secondary-item");
    expect(savingsLink).toHaveClass("is-active");
    expect(savingsLink).not.toHaveClass("is-greyed");
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
    it("greys Portfolio, Renewals and Quote check and shows no count badge with 0 validated contracts; Savings stays clickable", () => {
      renderRail({ kbReady: false, validatedContractCount: 0 });

      for (const label of ["Portfolio", "Renewals", "Quote check"]) {
        const link = screen.getByText(label).closest("a")!;
        expect(link).toHaveClass("is-greyed");
      }
      const savingsLink = screen.getByRole("link", { name: "Savings" });
      expect(savingsLink).not.toHaveClass("is-greyed");
      expect(savingsLink).toHaveAttribute("href", "/savings");
      const portfolioLink = screen.getByText("Portfolio").closest("a")!;
      expect(portfolioLink).not.toHaveTextContent(/\d/);
    });

    it("Quote check still shows 'optional' even while greyed", () => {
      renderRail({ kbReady: false });

      expect(screen.getByText("Quote check").closest("a")).toHaveTextContent("optional");
    });

    it("un-greys the tier and badges Portfolio/Renewals with the validated count once kbReady", () => {
      renderRail({ kbReady: true, validatedContractCount: 3 });

      for (const label of ["Portfolio", "Renewals", "Savings", "Quote check"]) {
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

    it("keeps Workspace & members in the pinned rail footer", () => {
      const { container } = renderRail({ role: "admin" });
      const footer = container.querySelector(".shell-rail-footer");
      const rail = container.querySelector(".shell-rail");

      expect(footer).not.toBeNull();
      expect(rail?.lastElementChild).toBe(footer);
      expect(footer).toContainElement(screen.getByRole("link", { name: "Workspace & members" }));
    });

    it("hides Workspace & members for Procurement (AC-2)", () => {
      renderRail({ role: "procurement" });

      expect(screen.queryByText("Workspace & members")).not.toBeInTheDocument();
      expect(screen.getByText("Portfolio")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Savings" })).toBeInTheDocument();
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
    it("lists the current workspace's chats on mount: the ones in use and, separately, the archive", () => {
      const listConversations = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations: [], error: null });
      renderRail({ apiClient: mockApiClient(listConversations) });

      expect(listConversations).toHaveBeenCalledTimes(2);
      expect(listConversations).toHaveBeenCalledWith(WORKSPACE_ID, 50, { archived: false });
      expect(listConversations).toHaveBeenCalledWith(WORKSPACE_ID, 50, { archived: true });
    });

    it("lists every returned conversation, in the order the API returned them, above '+ New chat'", async () => {
      const conversations = [
        conversation({ id: "conv-1", title: "When does Salesforce expire?" }),
        conversation({ id: "conv-2", title: "What is our AWS liability cap?" }),
      ];
      const listConversations = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations, error: null });
      const { container } = renderRail({ apiClient: mockApiClient(listConversations) });

      // Search chrome mounts only after listConversations resolves — "+ New chat" is always
      // present, so waiting on it races the async slot (CI on main after #167).
      await screen.findByRole("searchbox", { name: "Search chats" });

      const conversationSlot = container.querySelector(".shell-rail-conversations")!;
      const linkNames = Array.from(conversationSlot.querySelectorAll("a")).map((node) => node.textContent);
      expect(linkNames).toEqual(["Ask Raffa", "Ask Raffa", "+ New chat"]);
    });

    it("resumes by click -- each conversation link points at /ask/<id>", async () => {
      const listConversations = vi
        .fn()
        .mockResolvedValue({ ok: true, statusCode: 200, conversations: [conversation({ id: "conv-42" })], error: null });
      const { container } = renderRail({ apiClient: mockApiClient(listConversations) });

      await screen.findByRole("searchbox", { name: "Search chats" });
      const link = container.querySelector('a.shell-rail-conv-item[href="/ask/conv-42"]');
      expect(link).not.toBeNull();
    });

    it("marks the conversation matching the current /ask/:id route active, in accent", async () => {
      const listConversations = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        conversations: [conversation({ id: "conv-1" }), conversation({ id: "conv-2", title: "Second chat" })],
        error: null,
      });
      const { container } = renderRail({ apiClient: mockApiClient(listConversations), initialEntry: "/ask/conv-2" });

      await screen.findByRole("searchbox", { name: "Search chats" });
      const active = container.querySelector('a.shell-rail-conv-item[href="/ask/conv-2"]');
      const inactive = container.querySelector('a.shell-rail-conv-item[href="/ask/conv-1"]');
      expect(active).toHaveClass("is-active");
      expect(inactive).not.toHaveClass("is-active");
    });

    it("marks no conversation active on the plain /ask (new chat) route", async () => {
      const listConversations = vi
        .fn()
        .mockResolvedValue({ ok: true, statusCode: 200, conversations: [conversation({ id: "conv-1" })], error: null });
      const { container } = renderRail({ apiClient: mockApiClient(listConversations), initialEntry: "/ask" });

      await screen.findByRole("searchbox", { name: "Search chats" });
      const link = container.querySelector('a.shell-rail-conv-item[href="/ask/conv-1"]');
      expect(link).not.toHaveClass("is-active");
    });

    it("renders no conversation links, only '+ New chat', when the tenant has none yet", () => {
      renderRail({ apiClient: mockApiClient(vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations: [], error: null })) });

      expect(screen.queryByText(/expire\?/)).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "+ New chat" })).toBeInTheDocument();
    });

    it("filters the chat list by the bound title and deletes a chat from the rail", async () => {
      const conversations = [
        conversation({ id: "conv-1", scopeContractId: "contract-salesforce" }),
        conversation({ id: "conv-2", scopeContractId: "contract-aws" }),
      ];
      const listConversations = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations, error: null });
      const deleteConversation = vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null });
      const getPortfolio = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        portfolio: {
          items: [
            {
              contractId: "contract-salesforce",
              supplierId: null,
              supplierName: "Salesforce",
              type: "Msa",
              annualSpend: null,
              currency: "CHF",
              startDate: null,
              endDate: null,
              renewalDate: null,
              cancellationDeadline: null,
              autoRenewal: false,
              status: "active",
              risk: null,
            },
            {
              contractId: "contract-aws",
              supplierId: null,
              supplierName: "AWS",
              type: "OrderForm",
              annualSpend: null,
              currency: "CHF",
              startDate: null,
              endDate: null,
              renewalDate: null,
              cancellationDeadline: null,
              autoRenewal: false,
              status: "active",
              risk: null,
            },
          ],
          page: 1,
          pageSize: 100,
          totalCount: 2,
          processingDocumentCount: 0,
        },
        error: null,
      });
      const apiClient = mockApiClient(listConversations);
      apiClient.deleteConversation = deleteConversation;
      apiClient.getPortfolio = getPortfolio;
      renderRail({ apiClient });

      expect(await screen.findByRole("link", { name: "Salesforce — MSA" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "AWS — Order Form" })).toBeInTheDocument();

      await userEvent.type(screen.getByRole("searchbox", { name: "Search chats" }), "sales");
      expect(screen.getByRole("link", { name: "Salesforce — MSA" })).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "AWS — Order Form" })).not.toBeInTheDocument();

      await userEvent.click(screen.getByRole("button", { name: "Delete Salesforce — MSA" }));
      expect(deleteConversation).toHaveBeenCalledWith(WORKSPACE_ID, "conv-1");
    });
  });

  describe("rename a chat", () => {
    function renamedResult(id: string, customTitle: string | null) {
      return { ok: true, statusCode: 200, conversation: { id, title: "When does it expire?", customTitle, scopeContractId: null, updatedAt: "2026-09-08T00:00:00Z" }, error: null };
    }

    async function railWith(conversations: ConversationSummaryBody[], renameConversation = vi.fn()) {
      const apiClient = mockApiClient(vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations, error: null }));
      apiClient.renameConversation = renameConversation;
      const view = renderRail({ apiClient });
      await screen.findByRole("searchbox", { name: "Search chats" });
      return { ...view, renameConversation };
    }

    it("shows the name the server stored for a renamed chat", async () => {
      await railWith([conversation({ id: "conv-1", customTitle: "Atlassian renewal" }), conversation({ id: "conv-2" })]);

      expect(screen.getByRole("link", { name: "Atlassian renewal" })).toHaveAttribute("href", "/ask/conv-1");
      expect(screen.getByRole("link", { name: "Ask Raffa" })).toHaveAttribute("href", "/ask/conv-2");
    });

    it("Rename opens a field on the current title; Enter saves and the row shows the name at once", async () => {
      const user = userEvent.setup();
      const renameConversation = vi.fn().mockResolvedValue(renamedResult("conv-1", "Atlassian renewal"));
      await railWith([conversation({ id: "conv-1" })], renameConversation);

      await user.click(screen.getByRole("button", { name: "Rename Ask Raffa" }));
      const field = screen.getByRole("textbox", { name: "Rename Ask Raffa" });
      expect(field).toHaveValue("Ask Raffa");
      expect(field).toHaveFocus();
      expect(field).toHaveAttribute("maxLength", "48");

      await user.clear(field);
      await user.type(field, "Atlassian renewal{Enter}");

      expect(renameConversation).toHaveBeenCalledWith(WORKSPACE_ID, "conv-1", "Atlassian renewal");
      expect(screen.getByRole("link", { name: "Atlassian renewal" })).toBeInTheDocument();
      expect(screen.queryByRole("textbox", { name: /^Rename/ })).not.toBeInTheDocument();
    });

    it("Escape and an unchanged name leave the chat as it was, without a request", async () => {
      const user = userEvent.setup();
      const { renameConversation } = await railWith([conversation({ id: "conv-1" })]);

      await user.click(screen.getByRole("button", { name: "Rename Ask Raffa" }));
      await user.type(screen.getByRole("textbox", { name: "Rename Ask Raffa" }), " edited{Escape}");
      expect(screen.getByRole("link", { name: "Ask Raffa" })).toBeInTheDocument();

      await user.click(screen.getByRole("button", { name: "Rename Ask Raffa" }));
      await user.type(screen.getByRole("textbox", { name: "Rename Ask Raffa" }), "{Enter}");

      expect(renameConversation).not.toHaveBeenCalled();
    });

    it("a double-click on the title opens the same field, and clearing it restores the automatic title", async () => {
      const user = userEvent.setup();
      const renameConversation = vi.fn().mockResolvedValue(renamedResult("conv-1", null));
      await railWith([conversation({ id: "conv-1", customTitle: "Old name" })], renameConversation);

      await user.dblClick(screen.getByRole("link", { name: "Old name" }));
      const field = screen.getByRole("textbox", { name: "Rename Old name" });
      await user.clear(field);
      await user.type(field, "{Enter}");

      expect(renameConversation).toHaveBeenCalledWith(WORKSPACE_ID, "conv-1", null);
      expect(await screen.findByRole("link", { name: "Ask Raffa" })).toBeInTheDocument();
    });

    it("puts the old name back and says so when the server refuses the rename", async () => {
      const user = userEvent.setup();
      const renameConversation = vi.fn().mockResolvedValue({ ok: false, statusCode: 404, conversation: null, error: "No conversation found for id conv-1." });
      await railWith([conversation({ id: "conv-1", customTitle: "Old name" })], renameConversation);

      await user.click(screen.getByRole("button", { name: "Rename Old name" }));
      const field = screen.getByRole("textbox", { name: "Rename Old name" });
      await user.clear(field);
      await user.type(field, "New name{Enter}");

      expect(await screen.findByRole("alert")).toHaveTextContent("Could not rename this chat. Try again.");
      expect(screen.getByRole("link", { name: "Old name" })).toBeInTheDocument();
    });
  });

  describe("archive and search", () => {
    /** The server's two lists: `?archived=false` answers the chats in use, `?archived=true` the archive. */
    function railWithLists(inUse: ConversationSummaryBody[], archived: ConversationSummaryBody[]) {
      const listConversations = vi.fn(async (_tenant: string, _take?: number, options?: { archived?: boolean }) => ({
        ok: true,
        statusCode: 200,
        conversations: options?.archived ? archived : inUse,
        error: null,
      }));
      const apiClient = mockApiClient(listConversations as unknown as ApiClient["listConversations"]);
      apiClient.restoreConversation = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversation: null, error: null });
      return { ...renderRail({ apiClient }), apiClient, listConversations };
    }

    const recent = conversation({ id: "conv-new", customTitle: "Atlassian cap", archived: false, updatedAt: "2026-09-22T09:00:00Z" });
    const old = conversation({ id: "conv-old", customTitle: "Q2 renewals", archived: true, updatedAt: "2026-09-02T09:00:00Z" });
    const older = conversation({ id: "conv-older", customTitle: "AWS order form", archived: true, updatedAt: "2026-08-20T09:00:00Z" });

    it("files chats unused for a week under a collapsed Archive, with a count and a tooltip that says where they went", async () => {
      railWithLists([recent], [old, older]);

      const toggle = await screen.findByRole("button", { name: /Archive/ });
      expect(toggle).toHaveAttribute("aria-expanded", "false");
      expect(toggle).toHaveTextContent("2");
      expect(screen.getByRole("link", { name: "Atlassian cap" })).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: /Q2 renewals/ })).not.toBeInTheDocument();

      const tip = screen.getByRole("button", { name: "About the archive" });
      expect(tip).toHaveAccessibleDescription(/not used for a week move here/);
      expect(tip).toHaveAccessibleDescription(/Can't find a chat\? It is in the archive\. Click it and it is back in your chats/);
    });

    it("opens the archive to list its chats with the day each was last used", async () => {
      const user = userEvent.setup();
      railWithLists([recent], [old, older]);

      await user.click(await screen.findByRole("button", { name: /Archive/ }));

      expect(screen.getByRole("button", { name: /Archive/ })).toHaveAttribute("aria-expanded", "true");
      const link = screen.getByRole("link", { name: /Q2 renewals/ });
      expect(link).toHaveAttribute("href", "/ask/conv-old");
      expect(link).toHaveTextContent("2 Sep");
      expect(screen.getByRole("link", { name: /AWS order form/ })).toHaveTextContent("20 Aug");
    });

    it("clicking an archived chat opens it and brings it back to the chats in use at once", async () => {
      const user = userEvent.setup();
      const { apiClient, container } = railWithLists([recent], [old, older]);

      await user.click(await screen.findByRole("button", { name: /Archive/ }));
      await user.click(screen.getByRole("link", { name: /Q2 renewals/ }));

      expect(apiClient.restoreConversation).toHaveBeenCalledWith(WORKSPACE_ID, "conv-old");
      // Back on top of the recent list, no longer dated like an archived row; the archive keeps the rest.
      const recentLinks = Array.from(container.querySelectorAll(".shell-rail-conversations > div > .shell-rail-conv-row a"));
      expect(recentLinks.map((node) => node.textContent)).toEqual(["Q2 renewals", "Atlassian cap"]);
      expect(screen.getByRole("button", { name: /Archive/ })).toHaveTextContent("1");
    });

    it("puts the chat back in the archive when the server cannot restore it", async () => {
      const user = userEvent.setup();
      const { apiClient } = railWithLists([recent], [old]);
      apiClient.restoreConversation = vi.fn().mockResolvedValue({ ok: false, statusCode: 404, conversation: null, error: "gone" });

      await user.click(await screen.findByRole("button", { name: /Archive/ }));
      await user.click(screen.getByRole("link", { name: /Q2 renewals/ }));

      expect(await screen.findByRole("button", { name: /Archive/ })).toHaveTextContent("1");
    });

    it("shows no Archive while nothing is archived", async () => {
      railWithLists([recent], []);

      await screen.findByRole("link", { name: "Atlassian cap" });
      expect(screen.queryByRole("button", { name: /Archive/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "About the archive" })).not.toBeInTheDocument();
    });

    it("search looks in the archive too and opens it on a match; nothing found says so", async () => {
      const user = userEvent.setup();
      railWithLists([recent], [old, older]);

      const search = await screen.findByRole("searchbox", { name: "Search chats" });
      await user.type(search, "aws");
      expect(screen.getByRole("button", { name: /Archive/ })).toHaveAttribute("aria-expanded", "true");
      expect(screen.getByRole("link", { name: /AWS order form/ })).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Atlassian cap" })).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name: /Q2 renewals/ })).not.toBeInTheDocument();

      await user.clear(search);
      await user.type(search, "salesforce");
      expect(screen.getByRole("status")).toHaveTextContent("No chats match “salesforce”.");
    });

    it("clears the search with its own button or Escape", async () => {
      const user = userEvent.setup();
      railWithLists([recent], [old]);

      const search = await screen.findByRole("searchbox", { name: "Search chats" });
      expect(screen.queryByRole("button", { name: "Clear search" })).not.toBeInTheDocument();
      await user.type(search, "zzz");
      await user.click(screen.getByRole("button", { name: "Clear search" }));
      expect(search).toHaveValue("");
      expect(screen.getByRole("link", { name: "Atlassian cap" })).toBeInTheDocument();

      await user.type(search, "zzz{Escape}");
      expect(search).toHaveValue("");
    });

    it("offers search when every chat is archived", async () => {
      railWithLists([], [old]);

      expect(await screen.findByRole("searchbox", { name: "Search chats" })).toBeInTheDocument();
    });
  });
});

