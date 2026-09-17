import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import GlobalAskBar from "./GlobalAskBar";

/**
 * Task E27/F03/US01/T01 (bar-scope; parent story us-01-bar-scope AC-1/AC-2/AC-3; closes NW-77,
 * ADR-012 cl. 49 / ADR-020 §37.1 per `reports/architecture/waves/w19.md`). `askSuggestions.test.ts`
 * proves the pure predicates (`contractIdForPath`, `getAskBarCopy`'s supplier naming) in isolation;
 * this file proves the one thing those cannot -- that the real, mounted component reads
 * `useLocation()`, fetches `getContract360`, and actually calls `navigate` with the scoped path and
 * the query still in state. Harness mirrors `AppShell.test.tsx`'s own shape (render the bar beside a
 * bare `<Routes>` that probes the post-navigate location, `MemoryRouter initialEntries`) and
 * `routes/quotes/index.test.tsx`'s `sessionStorage` seeding for `loadCurrentWorkspace`, matching the
 * task's own "Tests required" table (level: unit).
 */

const CONTRACT_ID = "11111111-1111-1111-1111-111111111111";

function fakeApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getCapabilities: vi.fn().mockResolvedValue({ ok: false, statusCode: null, catalog: null, error: "not scripted" }),
    getContract360: vi.fn().mockResolvedValue({ ok: false, statusCode: null, contract: null, error: "not scripted" }),
    ...overrides,
  } as unknown as ApiClient;
}

/** Minimal `GetContract360Result` shape -- only `header.supplierName`, the one field
 * `GlobalAskBar.tsx` reads, the same "cast rather than fill every generated field" convention
 * `AppShell.test.tsx#fakeApiClient` already uses for its own wire-shaped mocks. */
function contract360Result(supplierName: string | null): unknown {
  return { ok: true, statusCode: 200, contract: { header: { supplierName } }, error: null };
}

/** Renders wherever `navigate("/ask...")` actually lands, so a test can read back the real
 * `location.search`/`location.state` `GlobalAskBar#submit` produced -- never asserted by mocking
 * `useNavigate` itself, which would only prove this file's own guess at the call shape. */
function AskProbe() {
  const location = useLocation();
  return (
    <div data-testid="ask-probe">
      <span data-testid="ask-search">{location.search}</span>
      <span data-testid="ask-state">{JSON.stringify(location.state)}</span>
    </div>
  );
}

function renderBar(path: string, apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <GlobalAskBar kbReady={true} role="procurement" apiClient={apiClient} />
      <Routes>
        <Route path="/ask" element={<AskProbe />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  window.sessionStorage.clear();
  window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: "w-1", name: "Acme Co" }));
});

describe("GlobalAskBar -- Contract 360 scope (AC-1)", () => {
  it("navigates /ask?scope=<contractId> with the typed query still in state, from /contracts/:id", async () => {
    const apiClient = fakeApiClient();
    renderBar(`/contracts/${CONTRACT_ID}`, apiClient);

    const input = screen.getByLabelText("Ask Raffa");
    await userEvent.type(input, "When must we give notice?{Enter}");

    await waitFor(() => expect(screen.getByTestId("ask-search").textContent).toBe(`?scope=${CONTRACT_ID}`));
    expect(JSON.parse(screen.getByTestId("ask-state").textContent ?? "null")).toEqual({
      query: "When must we give notice?",
      newChat: true,
    });
  });

  it("AC-3: does not scope navigation from a non-contract screen, e.g. /documents", async () => {
    const apiClient = fakeApiClient();
    renderBar("/documents", apiClient);

    const input = screen.getByLabelText("Ask Raffa");
    await userEvent.type(input, "Which documents are not askable yet?{Enter}");

    await waitFor(() => expect(screen.getByTestId("ask-probe")).toBeInTheDocument());
    expect(screen.getByTestId("ask-search").textContent).toBe("");
    expect(JSON.parse(screen.getByTestId("ask-state").textContent ?? "null")).toEqual({
      query: "Which documents are not askable yet?",
      newChat: true,
    });
  });
});

describe("GlobalAskBar -- notice chip supplier name (AC-2)", () => {
  it("names the real supplier once getContract360 resolves, and clicking that chip still scopes navigation", async () => {
    const apiClient = fakeApiClient({
      getContract360: vi.fn().mockResolvedValue(contract360Result("Acme Corp")),
    });
    renderBar(`/contracts/${CONTRACT_ID}`, apiClient);

    const chip = await screen.findByRole("button", { name: "When must we give notice to Acme Corp?" });
    expect(apiClient.getContract360).toHaveBeenCalledWith("w-1", CONTRACT_ID);

    await userEvent.click(chip);

    await waitFor(() => expect(screen.getByTestId("ask-search").textContent).toBe(`?scope=${CONTRACT_ID}`));
  });

  it('shows "this supplier" (never a blank or stale name) while getContract360 has not resolved yet', () => {
    const apiClient = fakeApiClient({ getContract360: vi.fn().mockImplementation(() => new Promise(() => {})) });
    renderBar(`/contracts/${CONTRACT_ID}`, apiClient);

    expect(screen.getByRole("button", { name: "When must we give notice to this supplier?" })).toBeInTheDocument();
  });

  it("AC-3: never fetches getContract360 (and shows the static pair) on a non-contract screen", async () => {
    const apiClient = fakeApiClient();
    renderBar("/contracts", apiClient);

    expect(await screen.findByRole("button", { name: "Which of these have uncapped liability?" })).toBeInTheDocument();
    expect(apiClient.getContract360).not.toHaveBeenCalled();
  });
});
