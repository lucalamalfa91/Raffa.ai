import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import type { ApiClient } from "../../../api/client";
import type { AppConfig } from "../../../config/appConfig";
import AcceptInvitationRoute from "./index";

/**
 * Task E14/F03/US02/T01 (wave w14 "workspace is real"; ADR-020 screen-11 footer; ADR-025 §C/D.3,
 * its own w14 footer Rules C10a/C10b). The ten states, the fragment read, the cleared address bar,
 * and the "never persisted" property (Rule C10).
 */

const navigateMock = vi.fn();
const loginRedirectMock = vi.fn();
const logoutRedirectMock = vi.fn();
let msalAccounts: Array<{ username: string; homeAccountId: string }> = [];

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => navigateMock };
});

vi.mock("@azure/msal-react", () => ({
  useMsal: () => ({
    instance: { loginRedirect: loginRedirectMock, logoutRedirect: logoutRedirectMock },
    accounts: msalAccounts,
  }),
}));

const appConfig: AppConfig = {
  apiBaseUrl: "https://api.dev.raffa.example",
  oidcAuthority: "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000",
  oidcClientId: "11111111-1111-1111-1111-111111111111",
  oidcRedirectUri: "https://web.dev.raffa.example",
  oidcApiScopes: ["api://11111111-1111-1111-1111-111111111111/Raffa.Read"],
};

function apiClientWith(overrides: Partial<ApiClient>): ApiClient {
  return {
    getInvitation: vi.fn(),
    acceptInvitation: vi.fn().mockResolvedValue({ ok: false, statusCode: null, acceptance: null, error: "not scripted" }),
    ...overrides,
  } as unknown as ApiClient;
}

function setHash(hash: string) {
  window.history.replaceState(null, "", `/invite/accept${hash}`);
}

function renderAccept(apiClient: ApiClient) {
  return render(
    <MemoryRouter>
      <AcceptInvitationRoute apiClient={apiClient} appConfig={appConfig} />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  msalAccounts = [];
  navigateMock.mockClear();
  loginRedirectMock.mockReset().mockResolvedValue(undefined);
  logoutRedirectMock.mockClear();
  window.sessionStorage.clear();
  window.localStorage.clear();
  setHash("");
});

afterEach(() => {
  setHash("");
});

const TOKEN = "0123456789abcdef0123456789abcdef.super-secret-value";

describe("AcceptInvitationRoute", () => {
  it("state 5 -- no token present: a normal outcome, never worded as an error or 'invalid'", () => {
    setHash("");
    renderAccept(apiClientWith({}));

    expect(screen.getByRole("heading", { name: /open your invitation link again/i })).toBeInTheDocument();
    expect(screen.queryByText(/invalid/i)).not.toBeInTheDocument();
  });

  it("Rule C9/C10b -- reads the token from the fragment and clears the address bar before any render", () => {
    setHash(`#${TOKEN}`);
    expect(window.location.hash).toBe(`#${TOKEN}`);

    renderAccept(apiClientWith({ getInvitation: vi.fn().mockReturnValue(new Promise(() => {})) }));

    // Cleared synchronously, in the same tick as the first render -- not in an effect that runs
    // after paint, and not merely "eventually" once the network call settles.
    expect(window.location.hash).toBe("");
  });

  it("state 1 -- loading, while the pre-accept read is in flight", () => {
    setHash(`#${TOKEN}`);
    renderAccept(apiClientWith({ getInvitation: vi.fn().mockReturnValue(new Promise(() => {})) }));

    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("state 2 -- valid, signed out: the offer and the verbatim Entra CTA, never the Join button", async () => {
    setHash(`#${TOKEN}`);
    msalAccounts = [];
    renderAccept(
      apiClientWith({
        getInvitation: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          invitation: { workspaceName: "Acme Procurement", role: "Procurement", expiresAt: "2026-12-01T00:00:00Z" },
          error: null,
        }),
      }),
    );

    expect(await screen.findByRole("heading", { name: /join acme procurement/i })).toBeInTheDocument();
    expect(screen.getByText(/invited as procurement/i)).toBeInTheDocument();
    const cta = screen.getByRole("button", { name: /continue with microsoft entra id/i });
    expect(screen.queryByRole("button", { name: /^join acme procurement$/i })).not.toBeInTheDocument();

    // Fix 2026-09-14: `loginRedirect`, not `loginPopup` -- see this screen's own header comment
    // (src/routes/invite/accept/index.tsx) for why. The click unloads this page; nothing here
    // simulates the return leg (App.tsx's AuthenticatedGate owns that, covered in App.test.tsx).
    await userEvent.click(cta);
    expect(loginRedirectMock).toHaveBeenCalledTimes(1);
  });

  it("a signed-out click never calls acceptInvitation itself -- the join, if any, happens after the redirect returns, elsewhere", async () => {
    setHash(`#${TOKEN}`);
    msalAccounts = [];
    const acceptInvitation = vi.fn();
    renderAccept(
      apiClientWith({
        getInvitation: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          invitation: { workspaceName: "Acme Procurement", role: "Procurement", expiresAt: "2026-12-01T00:00:00Z" },
          error: null,
        }),
        acceptInvitation,
      }),
    );

    await userEvent.click(await screen.findByRole("button", { name: /continue with microsoft entra id/i }));

    expect(loginRedirectMock).toHaveBeenCalledTimes(1);
    expect(acceptInvitation).not.toHaveBeenCalled();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("a visitor already signed in still sees state 3's explicit Join button, and nothing accepts on their behalf", async () => {
    setHash(`#${TOKEN}`);
    msalAccounts = [{ username: "user@example.test", homeAccountId: "home-1" }];
    const acceptInvitation = vi.fn();
    renderAccept(
      apiClientWith({
        getInvitation: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          invitation: { workspaceName: "Acme Procurement", role: "Procurement", expiresAt: "2026-12-01T00:00:00Z" },
          error: null,
        }),
        acceptInvitation,
      }),
    );

    expect(await screen.findByRole("button", { name: /^join acme procurement$/i })).toBeInTheDocument();
    expect(loginRedirectMock).not.toHaveBeenCalled();
    expect(acceptInvitation).not.toHaveBeenCalled();
  });

  it("state 3/4 -- valid, signed in: clicking Join calls acceptInvitation and disables the CTA meanwhile", async () => {
    setHash(`#${TOKEN}`);
    msalAccounts = [{ username: "user@example.test", homeAccountId: "home-1" }];
    let resolveAccept: (value: unknown) => void = () => {};
    const acceptInvitation = vi.fn().mockReturnValue(new Promise((resolve) => (resolveAccept = resolve)));
    renderAccept(
      apiClientWith({
        getInvitation: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          invitation: { workspaceName: "Acme Procurement", role: "Admin", expiresAt: "2026-12-01T00:00:00Z" },
          error: null,
        }),
        acceptInvitation,
      }),
    );

    const joinButton = await screen.findByRole("button", { name: /^join acme procurement$/i });
    await userEvent.click(joinButton);

    expect(acceptInvitation).toHaveBeenCalledWith(TOKEN);
    expect(await screen.findByRole("button", { name: /joining/i })).toBeDisabled();

    resolveAccept({
      ok: true,
      statusCode: 200,
      acceptance: { workspaceId: "w-1", workspaceName: "Acme Procurement", role: "Admin" },
      error: null,
    });

    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith("/", { replace: true }));
    expect(JSON.parse(window.sessionStorage.getItem("raffa.signin.currentWorkspace") ?? "null")).toEqual({
      id: "w-1",
      name: "Acme Procurement",
    });
  });

  it("state 6 -- wrong signed-in account: never echoes the invited address, offers sign-out", async () => {
    setHash(`#${TOKEN}`);
    msalAccounts = [{ username: "wrong@example.test", homeAccountId: "home-2" }];
    renderAccept(
      apiClientWith({
        getInvitation: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          invitation: { workspaceName: "Acme Procurement", role: "Admin", expiresAt: "2026-12-01T00:00:00Z" },
          error: null,
        }),
        acceptInvitation: vi.fn().mockResolvedValue({ ok: false, statusCode: 403, acceptance: null, error: "..." }),
      }),
    );

    await userEvent.click(await screen.findByRole("button", { name: /^join acme procurement$/i }));

    expect(await screen.findByText(/sent to a different address/i)).toBeInTheDocument();
    expect(screen.getByText(/wrong@example\.test/)).toBeInTheDocument();
    const signOutButton = screen.getByRole("button", { name: /sign out and use a different account/i });
    await userEvent.click(signOutButton);
    expect(logoutRedirectMock).toHaveBeenCalledTimes(1);
  });

  it("states 7/9 -- expired and revoked/unknown share the same first sentence, on purpose", async () => {
    setHash(`#${TOKEN}`);
    const expiredClient = apiClientWith({
      getInvitation: vi.fn().mockResolvedValue({ ok: false, statusCode: 410, invitation: null, error: "expired" }),
    });
    const { unmount } = renderAccept(expiredClient);
    const expiredHeading = await screen.findByRole("heading", { name: /this invitation is no longer valid/i });
    expect(expiredHeading).toBeInTheDocument();
    unmount();

    setHash(`#${TOKEN}`);
    const revokedClient = apiClientWith({
      getInvitation: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, invitation: null, error: "unknown" }),
    });
    renderAccept(revokedClient);
    expect(await screen.findByRole("heading", { name: /this invitation is no longer valid/i })).toBeInTheDocument();
  });

  it("state 8 -- already accepted: names the workspace already held from the pre-accept read, and 'Go to' re-enters", async () => {
    setHash(`#${TOKEN}`);
    msalAccounts = [{ username: "user@example.test", homeAccountId: "home-1" }];
    renderAccept(
      apiClientWith({
        getInvitation: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          invitation: { workspaceName: "Acme Procurement", role: "Admin", expiresAt: "2026-12-01T00:00:00Z" },
          error: null,
        }),
        acceptInvitation: vi.fn().mockResolvedValue({ ok: false, statusCode: 409, acceptance: null, error: "..." }),
      }),
    );

    await userEvent.click(await screen.findByRole("button", { name: /^join acme procurement$/i }));

    const heading = await screen.findByRole("heading", { name: /you have already joined acme procurement/i });
    expect(heading).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /^go to acme procurement$/i }));
    expect(navigateMock).toHaveBeenCalledWith("/", { replace: true });
  });

  it("state 10 -- transport error on the pre-accept read: ADR-018's error treatment, with a working Retry", async () => {
    setHash(`#${TOKEN}`);
    const getInvitation = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: 503, invitation: null, error: "Request failed with HTTP 503." })
      .mockResolvedValueOnce({
        ok: true,
        statusCode: 200,
        invitation: { workspaceName: "Acme Procurement", role: "Admin", expiresAt: "2026-12-01T00:00:00Z" },
        error: null,
      });
    msalAccounts = [{ username: "user@example.test", homeAccountId: "home-1" }];
    renderAccept(apiClientWith({ getInvitation }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/503/);
    await userEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByRole("heading", { name: /join acme procurement/i })).toBeInTheDocument();
    expect(getInvitation).toHaveBeenCalledTimes(2);
  });

  it("Rule C10 -- the raw token is never written to sessionStorage or localStorage, before or after a successful accept", async () => {
    setHash(`#${TOKEN}`);
    msalAccounts = [{ username: "user@example.test", homeAccountId: "home-1" }];
    renderAccept(
      apiClientWith({
        getInvitation: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          invitation: { workspaceName: "Acme Procurement", role: "Admin", expiresAt: "2026-12-01T00:00:00Z" },
          error: null,
        }),
        acceptInvitation: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          acceptance: { workspaceId: "w-1", workspaceName: "Acme Procurement", role: "Admin" },
          error: null,
        }),
      }),
    );

    await userEvent.click(await screen.findByRole("button", { name: /^join acme procurement$/i }));
    await waitFor(() => expect(navigateMock).toHaveBeenCalled());

    const everyStoredValue = [
      ...Array.from({ length: window.sessionStorage.length }, (_, i) => window.sessionStorage.getItem(window.sessionStorage.key(i) ?? "")),
      ...Array.from({ length: window.localStorage.length }, (_, i) => window.localStorage.getItem(window.localStorage.key(i) ?? "")),
    ].join("\n");

    expect(everyStoredValue).not.toContain(TOKEN);
  });
});
