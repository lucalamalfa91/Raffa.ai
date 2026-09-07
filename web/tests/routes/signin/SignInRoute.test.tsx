import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { InteractionStatus } from "@azure/msal-browser";
import SignInRoute from "../../../src/routes/signin";
import type { AppConfig } from "../../../src/config/appConfig";
import type { ApiClient, CreateWorkspaceResult } from "../../../src/api/client";

// SignInRoute (src/routes/signin/index.tsx) is the composition root for
// screen 1 (ADR-018 route `/signin`): it owns the MSAL useMsal() branch
// between SignInScreen and WorkspacePickerScreen. Mocking useMsal isolates
// that branch/wiring; MSAL's own redirect/PKCE plumbing is exercised by the
// library's own test suite (same convention tests/App.test.tsx already used
// for task E01/F07/US01/T02).
//
// This suite is the task's named "e2e | sign-in -> workspace list" proof
// (task-01-signin-workspace-picker.md "Tests required"): no browser-e2e tool
// (Playwright/Cypress/...) exists anywhere in this repo yet, so -- following
// the same precedent tests/App.test.tsx already set for AC-1's OIDC flow --
// this drives the full flow (unauthenticated -> click Continue -> signed in
// -> create a workspace -> workspace list) through Testing Library + jsdom,
// the closest available proof to "works in the browser" in this harness.
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

function mockApiClient(createWorkspace: ApiClient["createWorkspace"] = vi.fn()): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace,
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    getPortfolio: vi.fn(),
    // Task E07/F02/US01/T01 (contract-360): this suite never reaches Contract 360 -- bare vi.fn().
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
  };
}

describe("SignInRoute (sign-in -> workspace list)", () => {
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

    render(<SignInRoute appConfig={appConfig} apiClient={mockApiClient()} />);
    await userEvent.click(screen.getByRole("button", { name: /continue with microsoft entra id/i }));

    expect(loginRedirect).toHaveBeenCalledWith(expect.objectContaining({ scopes: appConfig.oidcApiScopes }));
  });

  it("walks sign-in -> create workspace -> workspace list end to end", async () => {
    useMsalMock.mockReturnValue({
      instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
      accounts: [],
      inProgress: InteractionStatus.None,
    });
    const created: CreateWorkspaceResult = {
      ok: true,
      statusCode: 201,
      workspace: { id: "w-1", name: "Acme Procurement", createdAt: "2026-09-06T08:00:00Z" },
      error: null,
    };
    const apiClient = mockApiClient(vi.fn().mockResolvedValue(created));

    const { rerender } = render(<SignInRoute appConfig={appConfig} apiClient={apiClient} />);

    // Stage 1: idle, unauthenticated.
    expect(screen.getByRole("button", { name: /continue with microsoft entra id/i })).toBeInTheDocument();

    // Stage 2: MSAL is processing the redirect response (return leg) --
    // still no account yet, but the CTA reflects "in flight".
    useMsalMock.mockReturnValue({
      instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
      accounts: [],
      inProgress: InteractionStatus.HandleRedirect,
    });
    rerender(<SignInRoute appConfig={appConfig} apiClient={apiClient} />);
    expect(screen.getByRole("button", { name: /redirecting to microsoft entra id/i })).toBeDisabled();

    // Stage 3: signed in -- the workspace picker takes over.
    useMsalMock.mockReturnValue({
      instance: { loginRedirect: vi.fn(), logoutRedirect: vi.fn() },
      accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
      inProgress: InteractionStatus.None,
    });
    rerender(<SignInRoute appConfig={appConfig} apiClient={apiClient} />);

    expect(screen.getByRole("heading", { name: /choose a workspace/i })).toBeInTheDocument();
    expect(screen.getByText(/no workspaces yet/i)).toBeInTheDocument();

    // Stage 4: create a workspace -> it becomes the (only) entry in the list
    // and the user lands in it.
    await userEvent.click(screen.getByRole("button", { name: /\+ create a new workspace/i }));
    await userEvent.type(screen.getByLabelText(/workspace name/i), "Acme Procurement");
    await userEvent.click(screen.getByRole("button", { name: /^create workspace$/i }));

    expect(await screen.findByRole("heading", { name: /you.re in acme procurement/i })).toBeInTheDocument();

    // Stage 5: switching workspace surfaces it in the (now non-empty) list --
    // this is the literal "workspace list" the test proves.
    await userEvent.click(screen.getByRole("button", { name: /switch workspace/i }));
    expect(screen.getByRole("button", { name: /acme procurement/i })).toBeInTheDocument();
    expect(screen.queryByText(/no workspaces yet/i)).not.toBeInTheDocument();
  });

  it("signs out from the workspace picker", async () => {
    const logoutRedirect = vi.fn();
    useMsalMock.mockReturnValue({
      instance: { loginRedirect: vi.fn(), logoutRedirect },
      accounts: [{ username: "user@example.test", homeAccountId: "home-1" }],
      inProgress: InteractionStatus.None,
    });

    render(<SignInRoute appConfig={appConfig} apiClient={mockApiClient()} />);
    await userEvent.click(screen.getByRole("button", { name: /sign out/i }));

    expect(logoutRedirect).toHaveBeenCalledTimes(1);
  });
});
