import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ShellRoutes } from "../../../src/components/shell/WorkspaceShellApp";
import type { WorkspaceRole } from "../../../src/components/shell/navItems";
import type { ApiClient } from "../../../src/api/client";

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
function mockApiClient(): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
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
          },
        ],
        page: 1,
        pageSize: 100,
        totalCount: 1,
      },
      error: null,
    }),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
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
    validateDocument: vi.fn(),
    // Task E08/F01/US01/T01 (renewal-pipeline): RenewalsRoute (like Contract360Route above) calls
    // getRenewals unconditionally on mount, already covered by the resolved default above; no test
    // in this suite triggers an insight-card action, so a bare vi.fn() is enough here -- see
    // tests/routes/renewals/*.test.tsx for that screen's own fetch/action coverage.
    postRenewalAction: vi.fn(),
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
    askContigo: vi.fn(),
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
    getCapabilities: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, catalog: { version: "test", capabilities: [] }, error: null }),
    getMarketRecord: vi.fn(),
  };
}

function renderShell(role: WorkspaceRole, initialPath = "/") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <ShellRoutes
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

  it("redirects / to /ask and renders the rail alongside the real Ask Contigo screen (R-WEB-01)", async () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin");

    expect(screen.getByRole("navigation", { name: /primary/i })).toBeInTheDocument();
    // This suite's own mockApiClient() above resolves getPortfolio to an empty page (0 validated
    // contracts), so the real V2 AskRoute this shell mounts renders its off state (AskOffState.tsx),
    // not the "What do you want to know?" on-state -- proves the real route (task E13/F09/US01/T04),
    // not a scaffold, is mounted, the same "assert on the real route's own honest content" convention
    // every other screen in this suite already follows (see e.g. the Contract 360/Renewals cases
    // below). The full off/on state matrix is covered in depth by tests/routes/ask/AskRoute.test.tsx.
    expect(await screen.findByRole("heading", { name: "Ask needs at least one validated contract." })).toBeInTheDocument();
  });

  it("renders the global Ask bar on a routed screen (AC-3, every app screen)", () => {
    // Task E07/F01/US01/T01 (portfolio-list-filters) replaced /contracts' ScaffoldScreen with the real
    // PortfolioRoute, which -- like DocumentsRoute below -- reads the current workspace directly and
    // guards on it being set, so this generic cross-screen assertion needs one current, same as the
    // dedicated /documents test further down.
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/contracts");

    expect(screen.getByRole("heading", { name: "Portfolio" })).toBeInTheDocument();
    expect(screen.getByRole("search")).toBeInTheDocument();
  });

  it("gates /workspace/members behind the request-access state for Procurement", () => {
    renderShell("procurement", "/workspace/members");

    expect(screen.getByRole("heading", { name: /you don.t manage this workspace/i })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Workspace & members" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /send invitation/i })).not.toBeInTheDocument();
  });

  it("renders the real members + invite screen for an Admin at /workspace/members", () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/workspace/members");

    expect(screen.getByRole("heading", { name: "Workspace & members" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send invitation/i })).toBeInTheDocument();
    expect(screen.queryByText(/members table \+ invite ships in epic-06\/feature-04-workspace-members-ui/i)).not.toBeInTheDocument();
  });

  it("redirects an unknown path to Ask (via / -- there is no Home to fall back to in V2)", async () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/this-route-does-not-exist");

    // See the identical assertion (and its own comment) in the "redirects / to /ask..." test above --
    // this suite's shared mock always resolves 0 validated contracts, so the real Ask screen the
    // catch-all redirect lands on is its off state.
    expect(await screen.findByRole("heading", { name: "Ask needs at least one validated contract." })).toBeInTheDocument();
  });

  it("/review redirects to /documents?filter=attention (Review is a state of Documents in V2, not a rail destination)", async () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/review");

    expect(await screen.findByRole("heading", { name: "Documents" })).toBeInTheDocument();
  });

  it("renders the real Documents screen instead of a scaffold placeholder (task E06/F05/US01/T01)", async () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
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

  it("renders the real Portfolio screen instead of a scaffold placeholder (task E07/F01/US01/T01)", () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/contracts");

    expect(screen.getByRole("heading", { name: "Portfolio" })).toBeInTheDocument();
    expect(screen.queryByText(/ships in epic-07\/feature-01-portfolio-ui/i)).not.toBeInTheDocument();
  });

  it("renders the real Contract 360 screen instead of a scaffold placeholder (task E07/F02/US01/T01)", async () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/contracts/22222222-2222-2222-2222-222222222222");

    // The shared mockApiClient() above resolves getContract360 to a 404 -- proves the real route
    // (which renders its own named "not found" state) is mounted, not the scaffold; the fetch-outcome
    // matrix itself (populated/loading/error/not-found) is covered in depth by
    // tests/routes/contracts/contract360/*.test.tsx.
    expect(await screen.findByText(/contract not found/i)).toBeInTheDocument();
    expect(screen.queryByText(/ships in epic-07\/feature-02-contract-360-ui/i)).not.toBeInTheDocument();
  });

  it("renders the real Renewals screen instead of a scaffold placeholder (task E08/F01/US01/T01)", async () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/renewals");

    // The shared mockApiClient() above resolves getRenewals to an empty pipeline -- proves the real
    // route (which renders its own named empty state) is mounted, not the scaffold; the fetch-outcome
    // matrix itself (populated/loading/error/empty/no-window) is covered in depth by
    // tests/routes/renewals/*.test.tsx.
    expect(await screen.findByText("No renewal dates yet")).toBeInTheDocument();
    expect(screen.queryByText(/ships in epic-08\/feature-01-renewal-pipeline-ui/i)).not.toBeInTheDocument();
  });

  it("renders the real Savings screen at /savings, moved from / (task E13/F09/US01/T01, gap G-IA-V2)", async () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/savings");

    // The shared mockApiClient() above resolves getSavingsKpis/getSavingsOpportunities to empty --
    // proves the real route (KPI row AND opportunities table) is mounted at its new V2 path; the
    // fetch-outcome matrix itself (populated/loading/error/empty/stale) is covered in depth by
    // tests/routes/savings/*.test.tsx.
    expect(await screen.findByText("Contracts analyzed")).toBeInTheDocument();
    expect(await screen.findByText(/no savings opportunities yet/i)).toBeInTheDocument();
  });
});
