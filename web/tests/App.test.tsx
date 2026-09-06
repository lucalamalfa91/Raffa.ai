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
