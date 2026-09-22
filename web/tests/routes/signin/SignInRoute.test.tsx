import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { InteractionStatus } from "@azure/msal-browser";
import SignInRoute from "../../../src/routes/signin";
import type { AppConfig } from "../../../src/config/appConfig";
import type { ApiClient, WorkspaceSummaryBody } from "../../../src/api/client";
import type { WorkspacePickerState } from "../../../src/routes/signin/WorkspacePickerScreen";

// SignInRoute (src/routes/signin/index.tsx) is screen 1's thin composition root (ADR-018 route
// `/signin`): it owns only the MSAL useMsal() branch between SignInScreen and WorkspacePickerScreen.
//
// Task E14/F03/US02/T01 (wave w14 "workspace is real") moved the actual workspace resolution -- the
// async apiClient.listWorkspaces() call, and the empty/one/many-with-hint decision -- up into
// App.tsx's own AuthenticatedGate. This route now renders whichever outcome that gate's `picker`
// prop carries; it no longer fetches, creates, or lists anything itself. This suite proves the
// wiring (picker === null -> SignInScreen; picker !== null -> WorkspacePickerScreen, with
// state/onRetry/onEnter passed through unchanged) -- not the resolution logic itself (see
// WorkspacePickerScreen.test.tsx for the resolution-order/segment-contract coverage, and
// tests/App.test.tsx for the end-to-end gate this route is one leaf of).
const useMsalMock = vi.fn();

vi.mock("@azure/msal-react", () => ({
  useMsal: () => useMsalMock(),
}));

const appConfig: AppConfig = {
  apiBaseUrl: "https://api.dev.raffa.example",
  oidcAuthority: "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000",
  oidcClientId: "11111111-1111-1111-1111-111111111111",
  oidcRedirectUri: "https://web.dev.raffa.example",
  oidcApiScopes: ["api://11111111-1111-1111-1111-111111111111/Raffa.Read"],
};

function mockApiClient(): ApiClient {
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
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    deleteConversation: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): this suite never reaches the Renewals screen -- bare
    // vi.fn() is enough, same convention as the other unexercised calls above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
  };
}

const acme: WorkspaceSummaryBody = {
  id: "w-1",
  name: "Acme Procurement",
  createdAt: "2026-09-06T08:00:00Z",
  role: "Admin",
  contractCount: 0,
};

describe("SignInRoute (composition root: SignInScreen <-> WorkspacePickerScreen)", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it("starts the Entra redirect with the configured scopes when unauthenticated", async () => {
    const loginRedirect = vi.fn();
    useMsalMock.mockReturnValue({
      instance: { loginRedirect, logoutRedirect: vi.fn() },
      accounts: [],
      inProgress: InteractionStatus.None,
    });

    render(<SignInRoute appConfig={appConfig} apiClient={mockApiClient()} picker={null} />);
    await userEvent.click(screen.getByRole("button", { name: /continue with microsoft entra id/i }));

    expect(loginRedirect).toHaveBeenCalledWith(expect.objectContaining({ scopes: appConfig.oidcApiScopes }));
  });

  it("reflects MSAL's own in-flight redirect state (the return leg) on the idle CTA", () => {
    useMsalMock.mockReturnValue({
      instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
      accounts: [],
      inProgress: InteractionStatus.HandleRedirect,
    });

    render(<SignInRoute appConfig={appConfig} apiClient={mockApiClient()} picker={null} />);

    expect(screen.getByRole("button", { name: /redirecting to login\.microsoftonline\.com/i })).toBeDisabled();
  });

  it("shows the sign-in screen (not the picker) whenever picker is null, even with an account -- App.tsx's gate has not resolved yet", () => {
    useMsalMock.mockReturnValue({
      instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
      accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
      inProgress: InteractionStatus.None,
    });

    render(<SignInRoute appConfig={appConfig} apiClient={mockApiClient()} picker={null} />);

    expect(screen.getByRole("button", { name: /continue with microsoft entra id/i })).toBeInTheDocument();
  });

  it("renders the workspace picker, wired to the picker prop's state/onEnter, once an account and a picker are both present", async () => {
    useMsalMock.mockReturnValue({
      instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
      accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
      inProgress: InteractionStatus.None,
    });
    const onEnter = vi.fn();
    const state: WorkspacePickerState = { phase: "pick", workspaces: [acme] };

    render(
      <SignInRoute appConfig={appConfig} apiClient={mockApiClient()} picker={{ state, onRetry: vi.fn(), onEnter }} />,
    );

    expect(screen.getByRole("heading", { name: /choose a workspace/i })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /acme procurement/i }));
    expect(onEnter).toHaveBeenCalledWith(acme);
  });

  it("surfaces the error state's Retry through the picker prop's onRetry", async () => {
    useMsalMock.mockReturnValue({
      instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
      accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
      inProgress: InteractionStatus.None,
    });
    const onRetry = vi.fn();
    const state: WorkspacePickerState = { phase: "error", message: "The workspace list is unavailable." };

    render(
      <SignInRoute appConfig={appConfig} apiClient={mockApiClient()} picker={{ state, onRetry, onEnter: vi.fn() }} />,
    );

    await userEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("signs out from the workspace picker", async () => {
    const logoutRedirect = vi.fn();
    useMsalMock.mockReturnValue({
      instance: { loginRedirect: vi.fn(), logoutRedirect },
      accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
      inProgress: InteractionStatus.None,
    });
    const state: WorkspacePickerState = { phase: "empty" };

    render(
      <SignInRoute
        appConfig={appConfig}
        apiClient={mockApiClient()}
        picker={{ state, onRetry: vi.fn(), onEnter: vi.fn() }}
      />,
    );
    await userEvent.click(screen.getByRole("button", { name: /sign out/i }));

    expect(logoutRedirect).toHaveBeenCalledTimes(1);
  });
});
