import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ShellRoutes } from "../../../src/components/shell/WorkspaceShellApp";
import type { WorkspaceRole } from "../../../src/components/shell/navItems";
import type { ApiClient } from "../../../src/api/client";
import { contractsV4Mocks } from "../../helpers/contractsV4Mocks";

// Task E06/F05/US01/T01 (document-upload) / E06/F05/US02/T01
// (document-status-readback) added the `documents` route's real content,
// which needs an ApiClient -- this suite only proves routing/guards (see
// tests/routes/documents/*.test.tsx for that screen's own coverage), so a
// plain stub is enough here, the same convention tests/App.test.tsx uses.
//
// getPortfolio defaults to a *resolved* empty page, not a bare vi.fn(): every
// screen this shell mounts now reaches AppShell (task E13/F09/US01/T01's own
// `useValidatedContractCount` calls it unconditionally on every mount, not
// just PortfolioRoute), so an unconfigured vi.fn() (which returns undefined,
// not a Promise) would throw the moment that effect calls .then() on it --
// see tests/routes/contracts/*.test.tsx for that screen's own fetch-outcome
// coverage.
const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

function mockApiClient(): ApiClient {
  return {
    ...contractsV4Mocks(),
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    // Task E14/F03/US02/T01 (wave w14): useValidatedContractCount now reads this instead of
    // getPortfolio below (see that hook's own header comment) -- every test in this suite already
    // expects the 0-validated-contracts state (getPortfolio's own resolved default was an empty
    // page), so an empty workspace list -- "no row matches the session hint" -- reproduces the
    // identical kbReady=false/count=0 outcome. An unconfigured vi.fn() would throw the moment
    // AppShell's own effect calls .then() on it, same "resolved default required" reasoning as
    // getPortfolio's own comment below.
    listWorkspaces: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, workspaces: [], error: null }),
    // Task E15/F02/US01/T01 (wave w14): MembersRoute now reads this unconditionally on mount (it
    // replaced the sessionStorage-backed memberStore this suite used to rely on implicitly) -- same
    // "resolved default required" reasoning as listWorkspaces/listDocuments above, or the effect's
    // own .then() throws on the unconfigured vi.fn()'s undefined return.
    getWorkspaceMembers: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, members: [], error: null }),
    getWorkspaceSettings: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, settings: null, error: "No workspace settings." }),
    updateWorkspaceSettings: vi.fn(),
    revokeInvitation: vi.fn(),
    removeMember: vi.fn(),
    getInvitation: vi.fn(),
    acceptInvitation: vi.fn(),
    acceptPendingInvitation: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    // Task E13/F09/US01/T03 (web-documents-v2): DocumentsRoute (like PortfolioRoute below) calls
    // listDocuments unconditionally on mount (`useDocumentsList.ts`), so an unconfigured vi.fn()
    // would throw the moment its effect calls .then() on it -- same reasoning as getPortfolio's own
    // resolved default below. One resolved row (not an empty page) is enough for this suite's own
    // routing/guard assertions to see the real *list* screen (heading + dropzone), not the onboarding
    // empty state -- see tests/routes/documents/*.test.tsx for that screen's own fetch-outcome
    // coverage, including the onboarding-empty case this suite deliberately does not exercise.
    listDocuments: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      page: {
        items: [
          {
            id: "doc-1",
            contractId: "contract-1",
            supplierName: "Salesforce",
            fileName: "Salesforce_MSA.pdf",
            documentType: "Msa",
            processingStatus: "Completed",
            stage: null,
            pageCount: 12,
            createdAt: "2026-09-06T08:00:00Z",
            weakFactCount: 0,
            rejectionReason: null,
          },
        ],
        page: 1,
        pageSize: 100,
        totalCount: 1,
        counts: { all: 1, needsAttention: 0, needsReview: 0, processing: 0, rejected: 0 },
      },
      error: null,
    }),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    deleteAllDocuments: vi.fn(),
    prioritiseDocument: vi.fn(),
    getPortfolio: vi
      .fn()
      .mockResolvedValue({ ok: true, statusCode: 200, portfolio: { items: [], page: 1, pageSize: 100, totalCount: 0 }, error: null }),
    // Task E07/F02/US01/T01 (contract-360): Contract360Route (like PortfolioRoute above) calls all
    // three of these unconditionally on mount, so an unconfigured vi.fn() would throw the moment its
    // effect calls .then() on it -- same reasoning as getPortfolio's own resolved default above. A
    // resolved 404/empty-list default is enough for this suite's own routing/guard assertions; see
    // tests/routes/contracts/contract360/*.test.tsx for that screen's own fetch-outcome coverage.
    getContract360: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, contract: null, error: "No contract found." }),
    getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [], totalCount: 0 }, error: null }),
    getRenewalPriority: vi
      .fn()
      .mockResolvedValue({ ok: false, statusCode: 404, priority: null, error: "No contract found." }),
    // Task E07/F03/US01/T01 (field-review-correction): no test in this suite navigates to
    // /contracts/:contractId/review -- bare vi.fn() is enough, same convention as getPortfolio/
    // getContract360 above (this comment records *why* it's safe to leave unresolved, unlike those).
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn(),
    getContractStrategy: vi.fn(),
    validateDocument: vi.fn(),
    // Task E08/F01/US01/T01 (renewal-pipeline): RenewalsRoute (like Contract360Route above) calls
    // getRenewals unconditionally on mount, already covered by the resolved default above; no test
    // in this suite triggers an insight-card action, so a bare vi.fn() is enough here -- see
    // tests/routes/renewals/*.test.tsx for that screen's own fetch/action coverage.
    postRenewalAction: vi.fn(),
    getQuote: vi.fn(),
    getNegotiationSteps: vi.fn(),
    putNegotiationSteps: vi.fn(),
    // Task E08/F03/US01/T01 (quote-check-ui): QuoteCheckRoute (like PortfolioRoute/Contract360Route
    // above) will call these on mount once a quote id is present, but every test in this suite that
    // reaches /quotes/:quoteId only asserts routing/guards without a real id -- bare vi.fn() is
    // enough here; see tests/routes/quotes/*.test.tsx for that screen's own fetch-outcome coverage.
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    // No test in this suite navigates to /ask with a typed question -- bare vi.fn() is enough; see
    // tests/routes/ask/*.test.tsx for that screen's own fetch-outcome coverage.
    askRaffa: vi.fn(),
    // Task E08/F02/US01/T01 (savings-home; moved to /savings by E13/F09/US01/T01): SavingsRoute
    // (like PortfolioRoute/Contract360Route/RenewalsRoute above) calls both of these unconditionally
    // on mount, so an unconfigured vi.fn() would throw the moment its effect calls .then() on it --
    // same reasoning as getPortfolio's own resolved default above. Resolved, empty-but-successful
    // defaults are enough for this suite's own routing/guard assertions; see
    // tests/routes/savings/*.test.tsx for that screen's own fetch-outcome coverage.
    getSavingsKpis: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      kpis: {
        annualSpendAnalyzed: [],
        contractsAnalyzedCount: 0,
        savingsIdentified: [],
        savingsInProgress: [],
        savingsRealized: [],
        upcomingRenewalsCount: 0,
      },
      error: null,
    }),
    getSavingsOpportunities: vi
      .fn()
      .mockResolvedValue({ ok: true, statusCode: 200, opportunities: { items: [], totalCount: 0 }, error: null }),
    // Task E13/F09/US01/T04 (web-ask-v2): RailNav's own `useRecentConversations` and GlobalAskBar's
    // own capability fetch both call these unconditionally on every shell mount (they render outside
    // `<Outlet/>`, in AppShell.tsx, on every route this suite exercises) -- same "resolved default
    // required" reasoning as getPortfolio/listDocuments above. No test in this suite resumes a
    // conversation or asserts capability-sourced chip copy -- see
    // tests/components/shell/RailNav.test.tsx and tests/components/ask-bar/GlobalAskBar.test.tsx.
    listConversations: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations: [], error: null }),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    postConversationFeedback: vi.fn(),
    deleteConversation: vi.fn(),
    renameConversation: vi.fn(),
    restoreConversation: vi.fn(),
    getCapabilities: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, catalog: { version: "test", capabilities: [] }, error: null }),
    getMarketRecord: vi.fn(),
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): this suite never reaches the Renewals screen -- bare
    // vi.fn() is enough, same convention as the other unexercised calls above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
  };
}

function renderShell(role: WorkspaceRole, initialPath = "/") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <ShellRoutes
        workspaceId={WORKSPACE_ID}
        workspaceName="Acme Procurement"
        role={role}
        userLabel="user@example.test"
        onSignOut={vi.fn()}
        apiClient={mockApiClient()}
      />
    </MemoryRouter>,
  );
}

describe("ShellRoutes (V2 route table, ADR-024 amendment; task E13/F09/US01/T01, gap G-IA-V2)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("redirects / to /today and renders the AskRaffa 4.0 top nav and Guida Raffa around it", async () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    Object.defineProperty(window, "innerWidth", { value: 1440, configurable: true });
    renderShell("admin");

    expect(screen.getByRole("navigation", { name: /primary/i })).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: /^Today/ })).toHaveAttribute("aria-current", "page");
    for (const name of ["Renewals", "Contracts", "Results", "Ask"]) {
      expect(screen.getByRole("link", { name })).toHaveAttribute("href", expect.stringMatching(/^\//));
    }
    expect(screen.getByRole("complementary", { name: "Raffa guide" })).toBeInTheDocument();
    expect(screen.queryByRole("search")).not.toBeInTheDocument();
  });

  it("renders no global Ask bar on a routed screen (screens carry their own Ask action)", () => {
    // Task E07/F01/US01/T01 (portfolio-list-filters) replaced /contracts' ScaffoldScreen with the real
    // PortfolioRoute, which -- like DocumentsRoute below -- reads the current workspace directly and
    // guards on it being set, so this generic cross-screen assertion needs one current, same as the
    // dedicated /documents test further down.
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/contracts");

    expect(screen.getByRole("heading", { name: "Contracts" })).toBeInTheDocument();
    expect(screen.queryByRole("search")).not.toBeInTheDocument();
  });

  it("no longer gates /workspace/members behind RequireRole for Procurement (task E14/F03/US02/T01 removes that wrap so a Procurement member reaches the screen; E15/F02/US01/T01, same phase, renders it read-only for that role -- this suite only proves the route is reachable, not the read-only content itself, which tests/routes/workspace/members/* covers)", () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("procurement", "/workspace/members");

    // The real route heading rendering at all is the proof: the old RequireRole gate replaced this
    // entire subtree with its own "empty-state" div (no "Workspace & members" heading in sight). The
    // read-only Procurement pane the real route renders instead (RequestAccessPane, index.tsx) reuses
    // the same "You don't manage this workspace" prototype copy as that old gate for a different,
    // legitimate reason -- content this suite deliberately does not assert on (see the test title).
    expect(screen.getByRole("heading", { name: "Workspace & members" })).toBeInTheDocument();
  });

  it("renders the real members + invite screen for an Admin at /workspace/members", () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/workspace/members");

    expect(screen.getByRole("heading", { name: "Workspace & members" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send invitation/i })).toBeInTheDocument();
    expect(screen.queryByText(/members table \+ invite ships in epic-06\/feature-04-workspace-members-ui/i)).not.toBeInTheDocument();
  });

  it("redirects an unknown path to Today (via /)", async () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/this-route-does-not-exist");

    expect(await screen.findByRole("link", { name: /^Today/ })).toHaveAttribute("aria-current", "page");
  });

  it("/review redirects to /documents?filter=attention (Review is a state of Documents in V2, not a rail destination)", async () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/review");

    expect(await screen.findByRole("heading", { name: "Documents" })).toBeInTheDocument();
  });

  it("renders the real Documents screen instead of a scaffold placeholder (task E06/F05/US01/T01)", async () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/documents");

    // Task E13/F09/US01/T03: DocumentsRoute's loading state (useDocumentsList.ts fetching
    // GET /api/documents) renders only "Loading documents…" + skeleton rows, no "Documents" heading
    // at all -- unlike Portfolio/Renewals/Savings below, whose own loading states keep their heading
    // on screen throughout. findByRole (not getByRole) waits for that fetch to resolve and the real
    // list screen to render, the same convention the "/review redirects…" and "redirects an unknown
    // path…" tests above already use for this identical reason.
    expect(await screen.findByRole("heading", { name: "Documents" })).toBeInTheDocument();
    // V2's UploadDropzone.tsx (task E13/F09/US01/T03) renders a visible "Upload contracts" button
    // (V1's own "Choose from computer" button no longer exists) plus a visually-hidden file input
    // aria-labelled "Choose contract files from your computer" -- see
    // tests/routes/documents/DocumentsRoute.test.tsx's own selectFiles() helper for where this suite
    // drives that hidden input directly.
    expect(screen.getByRole("button", { name: /upload contracts/i })).toBeInTheDocument();
    expect(screen.queryByText(/ships in epic-06\/feature-05-document-upload-ui/i)).not.toBeInTheDocument();
  });

  it("renders the AskRaffa 4.0 Contratti screen at /contracts", () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/contracts");

    expect(screen.getByRole("heading", { name: "Contracts" })).toBeInTheDocument();
    expect(document.querySelector('[data-screen-label="Contratti"]')).not.toBeNull();
  });

  it("renders the AskRaffa 4.0 Contratto screen at /contracts/:contractId", async () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/contracts/22222222-2222-2222-2222-222222222222");

    // The shared mock answers getContractV4 with a 404: the route shows its inline error (A3).
    expect(await screen.findByText(/the contract could not be loaded/i)).toBeInTheDocument();
  });

  it("renders the real Renewals screen instead of a scaffold placeholder (task E08/F01/US01/T01)", async () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/renewals");

    // The Renewals screen (cards, KPI strip, case panel) is mounted -- not the scaffold. Its own
    // behaviour is covered in depth by tests/routes/renewals/*.test.tsx.
    expect(await screen.findByRole("heading", { level: 2, name: "Renewals" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Renewal KPIs" })).toBeInTheDocument();
    expect(screen.queryByText(/ships in epic-08\/feature-01-renewal-pipeline-ui/i)).not.toBeInTheDocument();
  });

  it("mounts the renewal wizard chat at /renewals/:contractId/chat", async () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/renewals/4/chat");

    // The wizard's own header (not the Renewals list): case title and the seven-step stepper.
    expect(await screen.findByRole("heading", { name: /Renewal · DocuSign/ })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Renewal steps" })).toBeInTheDocument();
  });

  it("renders Risultati at /savings (AskRaffa 4.0 slice 5)", async () => {
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/savings");

    // The shared mockApiClient() has no Risultati calls: the page still renders its frame, the
    // collapsed goal bar and the honest inline error (DESIGN-GAPS A3).
    expect(await screen.findByRole("heading", { name: "Results" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Set a goal" })).toBeInTheDocument();
    expect(await screen.findByText("Results are not available right now.")).toBeInTheDocument();
  });
});
