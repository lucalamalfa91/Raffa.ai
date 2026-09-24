import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import AppShell, { isAskRoute } from "./AppShell";

/**
 * Task E25/F06/US01/T01 (NW-60; ADR-018/ADR-020; parent story us-01-hide-global-ask-bar
 * AC-1/AC-2/AC-3). `AppShell` always renders `GlobalAskBar` above the router outlet -- these tests
 * prove the one route-scoped exception this task adds: suppressed on `/ask` and
 * `/ask/:conversationId` (the route whose own composer replaces it, `routes/ask/index.tsx`),
 * present on every other screen. Stubs the routed children (rather than mounting the real
 * `AskRoute`/document screens) to keep this a shell-level unit test, matching the task's own "Tests
 * required" table (level: unit).
 */

function fakeApiClient(): ApiClient {
  return {
    // GlobalAskBar.tsx fetches this once, unconditionally, the moment it mounts -- the one
    // apiClient call a route where the bar still renders needs satisfied so the effect does not
    // throw on an unmocked method. Ask routes never call it, which is also asserted below as proof
    // the bar is genuinely unmounted there, not merely hidden.
    getCapabilities: vi.fn().mockResolvedValue({ ok: false, statusCode: null, catalog: null, error: "not scripted" }),
  } as unknown as ApiClient;
}

function renderShellAt(path: string, apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route
          element={
            <AppShell
              workspaceId="11111111-1111-1111-1111-111111111111"
              workspaceName="Acme Procurement"
              role="admin"
              userLabel="user@example.test"
              onSignOut={vi.fn()}
              apiClient={apiClient}
            />
          }
        >
          <Route path="ask" element={<div>Ask screen stub</div>} />
          <Route path="ask/:conversationId" element={<div>Ask screen stub</div>} />
          <Route path="documents" element={<div>Documents screen stub</div>} />
          <Route path="contracts" element={<div>Portfolio screen stub</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe("isAskRoute", () => {
  it("matches /ask exactly", () => {
    expect(isAskRoute("/ask")).toBe(true);
  });

  it("matches /ask/:conversationId", () => {
    expect(isAskRoute("/ask/22222222-2222-2222-2222-222222222222")).toBe(true);
  });

  it("matches /ask with a trailing slash", () => {
    expect(isAskRoute("/ask/")).toBe(true);
  });

  it("matches a descendant remainder without a leading slash (React Router 7 splat parent)", () => {
    expect(isAskRoute("ask")).toBe(true);
    expect(isAskRoute("ask/22222222-2222-2222-2222-222222222222")).toBe(true);
  });

  it("does not match a route that only shares the /ask prefix", () => {
    expect(isAskRoute("/askew")).toBe(false);
  });

  it("does not match other app routes", () => {
    expect(isAskRoute("/documents")).toBe(false);
    expect(isAskRoute("/contracts")).toBe(false);
    expect(isAskRoute("/")).toBe(false);
  });
});

describe("AppShell global Ask bar suppression", () => {
  it("AC-1: suppresses the global Ask bar on /ask, rendering the route's own composer instead", () => {
    const apiClient = fakeApiClient();
    renderShellAt("/ask", apiClient);

    expect(screen.queryByRole("search")).not.toBeInTheDocument();
    expect(screen.getByText("Ask screen stub")).toBeInTheDocument();
    // Genuinely unmounted, not just visually hidden -- GlobalAskBar's own capabilities fetch never fires.
    expect(apiClient.getCapabilities).not.toHaveBeenCalled();
  });

  it("AC-1: suppresses the global Ask bar on /ask/:conversationId", () => {
    const apiClient = fakeApiClient();
    renderShellAt("/ask/22222222-2222-2222-2222-222222222222", apiClient);

    expect(screen.queryByRole("search")).not.toBeInTheDocument();
    expect(screen.getByText("Ask screen stub")).toBeInTheDocument();
    expect(apiClient.getCapabilities).not.toHaveBeenCalled();
  });

  it("renders no global Ask bar on non-Ask routes either", () => {
    for (const path of ["/documents", "/contracts"]) {
      const apiClient = fakeApiClient();
      const { unmount } = renderShellAt(path, apiClient);
      expect(screen.queryByRole("search")).not.toBeInTheDocument();
      expect(apiClient.getCapabilities).not.toHaveBeenCalled();
      unmount();
    }
  });
});
