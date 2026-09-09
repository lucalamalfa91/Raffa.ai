import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { InteractionStatus } from "@azure/msal-browser";
import App from "../src/App";
import type { AppConfig } from "../src/config/appConfig";
import type { ApiClient, HealthCheckResult } from "../src/api/client";

// App.tsx (task E06/F03/US01/T01 onward) is only the composition root: run
// the /health proof-of-connectivity effect, then mount ./routes/signin
// (SignInRoute). SignInRoute's own branching (unauthenticated vs signed-in,
// redirect state, workspace list/create) is exercised in depth by
// tests/routes/signin/*.test.tsx; this file only needs to prove App wires
// health correctly and actually mounts SignInRoute -- not re-derive its
// whole state machine.
const useMsalMock = vi.fn();

vi.mock("@azure/msal-react", () => ({
  useMsal: () => useMsalMock(),
}));

const appConfig: AppConfig = {
  apiBaseUrl: "https://api.dev.contigo.example",
  oidcAuthority: "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000",
  oidcClientId: "11111111-1111-1111-1111-111111111111",
  oidcRedirectUri: "https://web.dev.contigo.example",
  oidcApiScopes: ["api://11111111-1111-1111-1111-111111111111/Contigo.Read"],
};

/** src/api/client.ts (task E01/F07/US01/T02, extended by E06/F03/US01/T01) is exercised by its own tests/api/client.test.ts; here it is a plain mock so App's rendering is isolated. */
function mockApiClient(result: Promise<HealthCheckResult> | HealthCheckResult): ApiClient {
  return {
    getHealth: vi.fn().mockReturnValue(Promise.resolve(result)),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    // Task E06/F05/US01/T01 (document-upload): exercised by
    // tests/routes/documents/*.test.tsx; a plain stub here so App's own
    // rendering stays isolated (same convention getHealth/createWorkspace
    // already use above).
    uploadDocument: vi.fn(),
    // Task E06/F05/US02/T01 (document-status-readback): exercised by
    // tests/routes/documents/*.test.tsx; same plain-stub convention.
    getDocument: vi.fn(),
    // Task E13/F09/US01/T04 (web-ask-v2): `/` now redirects to `/ask` (R-WEB-01), so every "signed
    // in + workspace selected" test below actually mounts AskRoute, not the old Home/Savings
    // default -- AskRoute's own off-state (kbReady is false here, getPortfolio below resolves
    // empty) calls listDocuments once to pick between its two off-copy variants (screens-v2.md #2),
    // so an unconfigured vi.fn() would throw the moment that call's .then() runs, the same
    // "unconditional shell-level call needs a resolved default" reasoning getPortfolio's own comment
    // below already gives.
    listDocuments: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, page: { items: [], page: 1, pageSize: 100, totalCount: 0 }, error: null }),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    // Task E07/F01/US01/T01 (portfolio-list-filters): exercised in depth by
    // tests/routes/contracts/*.test.tsx. Task E13/F09/US01/T01 (web-shell-v2) made this call
    // unconditional here too -- AppShell's own `useValidatedContractCount` calls it on every mount
    // once a workspace is current (every "signed in + workspace selected" test below reaches
    // AppShell), so a resolved, empty default is required, the same reasoning this file's own
    // getSavingsKpis/getSavingsOpportunities comment already gives for HomeRoute (now SavingsRoute).
    getPortfolio: vi
      .fn()
      .mockResolvedValue({ ok: true, statusCode: 200, portfolio: { items: [], page: 1, pageSize: 100, totalCount: 0 }, error: null }),
    // Task E07/F02/US01/T01 (contract-360): exercised by
    // tests/routes/contracts/contract360/*.test.tsx; same plain-stub convention.
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    // Task E07/F03/US01/T01 (field-review-correction): App itself never reaches the Review screen --
    // bare vi.fn() is enough, same convention as getContract360 above.
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn(),
    validateDocument: vi.fn(),
    // Task E08/F01/US01/T01 (renewal-pipeline): App itself never reaches the Renewals screen --
    // bare vi.fn() is enough, same convention as getContract360 above.
    postRenewalAction: vi.fn(),
    // Task E08/F03/US01/T01 (quote-check-ui): App itself never reaches the Quote Check screen --
    // bare vi.fn() is enough, same convention as getCorrectionHistory above.
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
    // Task E08/F02/US01/T01 (savings-home): unlike getPortfolio/getContract360/etc. above (never
    // invoked here -- this file never navigates away from the shell's default route), HomeRoute IS
    // that default route ("/", WorkspaceShellApp's own `index` route) -- every "signed in + workspace
    // selected" test below actually mounts it and calls both of these unconditionally on mount, so an
    // unconfigured vi.fn() (which returns undefined, not a Promise) would throw the moment its effect
    // calls .then() on it, the same reasoning tests/components/shell/WorkspaceShellApp.test.tsx's own
    // getPortfolio comment already gives. Resolved, empty-but-successful defaults are enough for this
    // file's own routing assertions; see tests/routes/home/*.test.tsx for that screen's own
    // fetch-outcome coverage.
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
    // `<Outlet/>`, in AppShell.tsx, on every route) -- same "resolved default required" reasoning as
    // getPortfolio/listDocuments above. No test in this suite resumes a conversation or asserts
    // capability-sourced chip copy -- see tests/components/shell/RailNav.test.tsx and
    // tests/components/ask-bar/GlobalAskBar.test.tsx for that coverage.
    listConversations: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, conversations: [], error: null }),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, catalog: { version: "test", capabilities: [] }, error: null }),
    getMarketRecord: vi.fn(),
  };
}

const healthyClient = () => mockApiClient({ ok: true, statusCode: 200, body: "Healthy" });

describe("App", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  describe("mounts the /signin route (SignInRoute; see tests/routes/signin for its own coverage)", () => {
    it("shows the sign-in screen when unauthenticated", () => {
      useMsalMock.mockReturnValue({
        instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
        accounts: [],
        inProgress: InteractionStatus.None,
      });

      render(<App appConfig={appConfig} apiClient={healthyClient()} />);

      expect(screen.getByRole("button", { name: /continue with microsoft entra id/i })).toBeInTheDocument();
    });

    it("does not wrap SignInRoute in a second, constraining <main> -- sign-in owns its own page (E06/F06/US01/T01)", () => {
      useMsalMock.mockReturnValue({
        instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
        accounts: [],
        inProgress: InteractionStatus.None,
      });

      const { container } = render(<App appConfig={appConfig} apiClient={healthyClient()} />);

      // Exactly one <main> in the whole tree: SignInScreen's own
      // full-bleed `.signin-screen`, not a second wrapper around it (the
      // regression this used to be: `<main><SignInRoute/></main>`, capped
      // at `max-width: 40rem` by index.css's now-removed E01 rule).
      const mains = container.querySelectorAll("main");
      expect(mains).toHaveLength(1);
      expect(mains[0]).toHaveClass("signin-screen");
    });

    it("shows the workspace picker when authenticated", () => {
      useMsalMock.mockReturnValue({
        instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
        accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
        inProgress: InteractionStatus.None,
      });

      render(<App appConfig={appConfig} apiClient={healthyClient()} />);

      expect(screen.getByRole("heading", { name: /choose a workspace/i })).toBeInTheDocument();
      expect(screen.getByText(/user@example\.test/)).toBeInTheDocument();
    });
  });

  describe("mounts the workspace shell once signed in and a workspace is selected (task E06/F03/US02/T01)", () => {
    it("renders the rail nav instead of the workspace picker once a workspace is already current", () => {
      useMsalMock.mockReturnValue({
        instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
        accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
        inProgress: InteractionStatus.None,
      });
      window.sessionStorage.setItem(
        "contigo.signin.currentWorkspace",
        JSON.stringify({ id: "w-1", name: "Acme Procurement" }),
      );

      render(<App appConfig={appConfig} apiClient={healthyClient()} />);

      expect(screen.getByText("Acme Procurement")).toBeInTheDocument();
      expect(screen.getByText("Portfolio")).toBeInTheDocument();
      expect(screen.queryByRole("heading", { name: /choose a workspace/i })).not.toBeInTheDocument();
    });

    it("does not double-wrap the app shell's own <main> either (AppShell.tsx already owns `.shell-main`)", () => {
      useMsalMock.mockReturnValue({
        instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
        accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
        inProgress: InteractionStatus.None,
      });
      window.sessionStorage.setItem(
        "contigo.signin.currentWorkspace",
        JSON.stringify({ id: "w-1", name: "Acme Procurement" }),
      );

      const { container } = render(<App appConfig={appConfig} apiClient={healthyClient()} />);

      const mains = container.querySelectorAll("main");
      expect(mains).toHaveLength(1);
      expect(mains[0]).toHaveClass("shell-main");
    });

    it("still renders the always-on health status above the shell", async () => {
      useMsalMock.mockReturnValue({
        instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
        accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
        inProgress: InteractionStatus.None,
      });
      window.sessionStorage.setItem(
        "contigo.signin.currentWorkspace",
        JSON.stringify({ id: "w-1", name: "Acme Procurement" }),
      );

      render(<App appConfig={appConfig} apiClient={healthyClient()} />);

      expect(await screen.findByText(/API: reachable \(Healthy\)/)).toBeInTheDocument();
    });
  });

  describe("API health status (task E01/F07/US01/T02, 'wire /health')", () => {
    beforeEach(() => {
      useMsalMock.mockReturnValue({
        instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
        accounts: [],
        inProgress: InteractionStatus.None,
      });
    });

    it("calls apiClient.getHealth() once on mount", () => {
      const apiClient = healthyClient();
      render(<App appConfig={appConfig} apiClient={apiClient} />);
      expect(apiClient.getHealth).toHaveBeenCalledTimes(1);
    });

    it("keeps the status out of the visible canvas via CSS clip, not display:none, so its data-testid stays in the a11y tree (AC-1/AC-3, task E11/F01/US01/T01)", async () => {
      render(<App appConfig={appConfig} apiClient={healthyClient()} />);

      // vite.config.ts sets `test.css: false` (tests/styles/layout.test.ts's
      // own header comment) -- there is no real cascade under jsdom here, so
      // `toBeVisible()` cannot observe a clip-based hide (it only checks
      // display/visibility/opacity/the hidden attribute, none of which this
      // technique touches). Asserting the utility class -- the same
      // convention this file already uses for "signin-screen"/"shell-main"
      // above -- is this suite's proof of the DoD's "CSS clip" branch;
      // base.css's own `.visually-hidden` rule is what actually implements
      // it, and the node still resolving by testid/text below is the proof
      // it never went through `display:none`.
      const status = await screen.findByTestId("api-health-status");
      expect(status).toHaveClass("visually-hidden");
      expect(status).toHaveTextContent("API: reachable (Healthy)");
    });

    it("renders the reachable status once the health check resolves ok", async () => {
      render(<App appConfig={appConfig} apiClient={healthyClient()} />);

      expect(await screen.findByText(/API: reachable \(Healthy\)/)).toBeInTheDocument();
    });

    it("renders the unreachable status (with status code) when the health check resolves not-ok", async () => {
      const apiClient = mockApiClient({ ok: false, statusCode: 503, body: "Unhealthy" });
      render(<App appConfig={appConfig} apiClient={apiClient} />);

      expect(await screen.findByText(/API: unreachable \(503: Unhealthy\)/)).toBeInTheDocument();
    });

    it("renders the unreachable status (network error) when the health check never got a status code", async () => {
      const apiClient = mockApiClient({
        ok: false,
        statusCode: null,
        body: "Unable to reach https://api.dev.contigo.example/health. Cause: network down",
      });
      render(<App appConfig={appConfig} apiClient={apiClient} />);

      const status = await screen.findByTestId("api-health-status");
      expect(status).toHaveTextContent("API: unreachable (network error:");
      expect(status).toHaveTextContent("network down");
    });
  });
});
