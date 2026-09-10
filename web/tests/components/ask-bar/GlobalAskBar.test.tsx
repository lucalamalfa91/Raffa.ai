import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import GlobalAskBar from "../../../src/components/ask-bar/GlobalAskBar";
import type { ApiClient, CapabilityBody } from "../../../src/api/client";

/** Renders whatever router state `/ask` was reached with, so a test can assert on `newChat`/`query`
 * without `routes/ask/**` itself (this task's own "do not touch" boundary) -- a plain
 * `<div>ASK SCREEN</div>` (the pre-V2 probe) could not tell a plain navigation apart from a
 * `newChat: true` one. */
function AskProbe() {
  const location = useLocation();
  return <div>ASK SCREEN {JSON.stringify(location.state)}</div>;
}

/** Minimal full `ApiClient` mock -- this suite only ever exercises `getCapabilities` (task
 * E13/F09/US01/T04, gap G-CAPABILITIES); every other method is a bare `vi.fn()`. Defaults to a
 * pending (never-resolving) promise so a test that does not care about capability-sourced copy
 * keeps seeing the static fallback for its whole run, the same "never resolves == stays on the
 * fallback forever" shape a real unconfigured fetch never actually produces but is a safe, simple
 * stand-in for "the catalog has not loaded yet". */
function mockApiClient(getCapabilities: ApiClient["getCapabilities"] = vi.fn(() => new Promise<never>(() => {}))): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn(),
    validateDocument: vi.fn(),
    postRenewalAction: vi.fn(),
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
    getCapabilities,
    getMarketRecord: vi.fn(),
  };
}

function capability(overrides: Partial<CapabilityBody> = {}): CapabilityBody {
  return {
    key: "ask",
    title: "Ask Raffa",
    routePattern: "/ask",
    description: "Ask about dates, spend, notice periods and clauses.",
    exampleQuestions: ["What can Raffa do?", "When does this contract expire?", "What liabilities do we have?"],
    roleGate: "any",
    availability: "always",
    howTo: [],
    ...overrides,
  };
}

function renderAtPath(path: string, kbReady = true, apiClient: ApiClient = mockApiClient()) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/ask" element={<AskProbe />} />
        <Route path="*" element={<GlobalAskBar kbReady={kbReady} apiClient={apiClient} />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("GlobalAskBar", () => {
  it("shows the prototype's default placeholder and exactly two suggestion chips outside a known section", () => {
    renderAtPath("/");

    expect(
      screen.getByPlaceholderText("Ask Raffa — spend, dates, clauses, liability…"),
    ).toBeInTheDocument();
    expect(screen.getAllByRole("button")).toHaveLength(2);
  });

  it("uses the same placeholder on every screen, including portfolio", () => {
    renderAtPath("/contracts");

    expect(
      screen.getByPlaceholderText("Ask Raffa — spend, dates, clauses, liability…"),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Which of these have uncapped liability?" })).toBeInTheDocument();
  });

  it("submits the typed query on Enter, clears the input, and navigates to /ask with a new-chat state (task E13/F09/US01/T01)", async () => {
    renderAtPath("/");
    const input = screen.getByRole("textbox", { name: /ask raffa/i });

    await userEvent.type(input, "What is overdue?{Enter}");

    expect(input).toHaveValue("");
    expect(await screen.findByText(/ASK SCREEN/)).toBeInTheDocument();
    expect(screen.getByText(/ASK SCREEN/)).toHaveTextContent('{"query":"What is overdue?","newChat":true}');
  });

  it("does nothing on Enter with a blank/whitespace-only query", async () => {
    renderAtPath("/");
    const input = screen.getByRole("textbox", { name: /ask raffa/i });

    await userEvent.type(input, "   {Enter}");

    expect(screen.queryByText(/ASK SCREEN/)).not.toBeInTheDocument();
  });

  it("asks the suggestion directly when a chip is clicked, also as a new chat", async () => {
    renderAtPath("/");

    await userEvent.click(
      screen.getByRole("button", { name: "When does Salesforce expire?" }),
    );

    expect(await screen.findByText(/ASK SCREEN/)).toBeInTheDocument();
    expect(screen.getByText(/ASK SCREEN/)).toHaveTextContent('"newChat":true');
  });

  it("focuses the input on Ctrl+K from anywhere on the screen", async () => {
    renderAtPath("/");
    const input = screen.getByRole("textbox", { name: /ask raffa/i });
    expect(input).not.toHaveFocus();

    await userEvent.keyboard("{Control>}k{/Control}");

    expect(input).toHaveFocus();
  });

  describe("kbReady off (ADR-024 V2 amendment; no validated contract yet)", () => {
    it("switches the placeholder to the prototype's off-copy regardless of route", () => {
      renderAtPath("/", false);
      expect(
        screen.getByPlaceholderText("Ask Raffa switches on after your first validated contract"),
      ).toBeInTheDocument();
    });

    it("overrides even a route-contextual placeholder while off", () => {
      renderAtPath("/contracts", false);
      expect(
        screen.getByPlaceholderText("Ask Raffa switches on after your first validated contract"),
      ).toBeInTheDocument();
    });

    it("disables the input and empties the chips while off (app.jsx disabled={kbOff} / askChips:[])", () => {
      renderAtPath("/", false);
      expect(screen.getByRole("textbox", { name: /ask raffa/i })).toBeDisabled();
      expect(screen.queryAllByRole("button")).toHaveLength(0);
    });
  });

  describe("capability-sourced suggestions (task E13/F09/US01/T04, gap G-CAPABILITIES)", () => {
    it("fetches the catalog once on mount", () => {
      const getCapabilities = vi.fn(() => new Promise<never>(() => {}));
      renderAtPath("/", true, mockApiClient(getCapabilities));

      expect(getCapabilities).toHaveBeenCalledTimes(1);
    });

    it("keeps the static fallback chips while the catalog is still loading", () => {
      renderAtPath("/");

      expect(screen.getByRole("button", { name: "When does Salesforce expire?" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "What liabilities do we have?" })).toBeInTheDocument();
    });

    it("swaps to the catalog's own first two exampleQuestions for the matching screen once it resolves", async () => {
      const getCapabilities = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        catalog: {
          version: "capabilities-v2.0",
          capabilities: [
            capability({
              key: "portfolio",
              exampleQuestions: ["Which of these have uncapped liability?", "Which contracts renew in the next 120 days?"],
            }),
          ],
        },
        error: null,
      });
      renderAtPath("/contracts", true, mockApiClient(getCapabilities));

      expect(await screen.findByRole("button", { name: "Which of these have uncapped liability?" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Which contracts renew in the next 120 days?" })).toBeInTheDocument();
      // The pre-existing static copy for this route is gone once the catalog wins.
      expect(screen.queryByText("Which contracts are missing a renewal date?")).not.toBeInTheDocument();
    });

    it("keeps the static fallback when the catalog resolves with no entry for the current screen", async () => {
      const getCapabilities = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        catalog: { version: "capabilities-v2.0", capabilities: [capability({ key: "documents" })] },
        error: null,
      });
      renderAtPath("/contracts", true, mockApiClient(getCapabilities));

      await waitFor(() => expect(getCapabilities).toHaveBeenCalled());
      expect(screen.getByRole("button", { name: "Which of these have uncapped liability?" })).toBeInTheDocument();
    });

    it("keeps the static fallback when the catalog fetch fails", async () => {
      const getCapabilities = vi.fn().mockResolvedValue({ ok: false, statusCode: null, catalog: null, error: "network down" });
      renderAtPath("/", true, mockApiClient(getCapabilities));

      await waitFor(() => expect(getCapabilities).toHaveBeenCalled());
      expect(screen.getByRole("button", { name: "When does Salesforce expire?" })).toBeInTheDocument();
    });
  });
});
