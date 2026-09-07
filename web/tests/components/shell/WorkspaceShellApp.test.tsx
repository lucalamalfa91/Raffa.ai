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
// getPortfolio defaults to a *resolved* empty page, not a bare vi.fn(): unlike
// uploadDocument/getDocument (only ever called from inside a user action this
// suite never triggers), task E07/F01/US01/T01's PortfolioRoute calls
// getPortfolio unconditionally on mount, so an unconfigured vi.fn() (which
// returns undefined, not a Promise) would throw the moment that route's own
// effect calls .then() on it -- see tests/routes/contracts/*.test.tsx for
// that screen's own fetch-outcome coverage.
function mockApiClient(): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
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

describe("ShellRoutes", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("renders the rail alongside the Home placeholder at /", () => {
    renderShell("admin");

    expect(screen.getByRole("navigation", { name: /primary/i })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Home" })).toBeInTheDocument();
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
  });

  it("renders the real placeholder screen for an Admin at /workspace/members", () => {
    renderShell("admin", "/workspace/members");

    expect(screen.getByRole("heading", { name: "Workspace & members" })).toBeInTheDocument();
  });

  it("redirects an unknown path back to Home", () => {
    renderShell("admin", "/this-route-does-not-exist");

    expect(screen.getByRole("heading", { name: "Home" })).toBeInTheDocument();
  });

  it("renders the real Documents screen instead of a scaffold placeholder (task E06/F05/US01/T01)", () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/documents");

    expect(screen.getByRole("heading", { name: "Documents" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /choose from computer/i })).toBeInTheDocument();
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
    expect(await screen.findByText(/no renewals in your pipeline yet/i)).toBeInTheDocument();
    expect(screen.queryByText(/ships in epic-08\/feature-01-renewal-pipeline-ui/i)).not.toBeInTheDocument();
  });
});
