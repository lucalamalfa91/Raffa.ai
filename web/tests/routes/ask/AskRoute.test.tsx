import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation, useParams } from "react-router-dom";
import AskRoute from "../../../src/routes/ask";
import type { ApiClient, AskContigoResult, GetDocumentResult } from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    askContigo: vi.fn(),
    ...overrides,
  };
}

function answered(overrides: Partial<NonNullable<AskContigoResult["response"]>> = {}): AskContigoResult {
  return {
    ok: true,
    statusCode: 200,
    response: {
      question: "What liability do we have with AWS?",
      intent: "Semantic",
      canDetermine: true,
      answer: "AWS liability is capped at USD 500,000.",
      citations: [{ documentId: "Document:doc-1", page: null, section: "chunk 0" }],
      message: null,
      ...overrides,
    },
    error: null,
  };
}

function documentOk(overrides: Partial<NonNullable<GetDocumentResult["document"]>> = {}): GetDocumentResult {
  return {
    ok: true,
    statusCode: 200,
    document: {
      id: "doc-1",
      contractId: "22222222-2222-2222-2222-222222222222",
      fileName: "AWS_Enterprise_Agreement.pdf",
      mimeType: "application/pdf",
      documentType: "Msa",
      processingStatus: "Completed",
      createdAt: "2025-01-01T00:00:00Z",
      ...overrides,
    },
    error: null,
  };
}

/** Stand-in for ../contracts/contract360/index.tsx -- proves AskRoute navigates with the exact
 * `{ state: { tab } }` shape that screen's own `isContract360TabName` guard reads, without pulling
 * that whole screen's own fetch machinery into this suite. */
function Contract360Stub() {
  const { contractId } = useParams<{ contractId: string }>();
  const location = useLocation();
  const tab = (location.state as { tab?: string } | null)?.tab ?? "none";
  return <div>CONTRACT_360 contractId={contractId} tab={tab}</div>;
}

function renderAsk(apiClient: ApiClient, initialEntry: { pathname: string; state?: unknown } = { pathname: "/ask" }) {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/ask" element={<AskRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<Contract360Stub />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("AskRoute", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("contigo.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const askContigo = vi.fn();

    renderAsk(mockApiClient({ askContigo }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(askContigo).not.toHaveBeenCalled();
  });

  it("AC-4 empty state: shows the routing/citation/abstain contract before any question is asked", () => {
    renderAsk(mockApiClient());

    expect(screen.getByText(/every answer cites its source, or says it cannot determine reliably/i)).toBeInTheDocument();
  });

  it("renders the 'Try' suggestions rail (day1-demo.html's own suggestionsTxt)", () => {
    renderAsk(mockApiClient());

    expect(screen.getByRole("button", { name: "What is our Microsoft annual spend?" })).toBeInTheDocument();
  });

  describe("AC-1: asking a question", () => {
    it("submitting the typed question calls askContigo with the current workspace id, echoes a 'You' bubble, and clears the input", async () => {
      const askContigo = vi.fn().mockResolvedValue(answered());
      renderAsk(mockApiClient({ askContigo }));

      const input = screen.getByRole("textbox", { name: /ask contigo a question/i });
      await userEvent.type(input, "What liability do we have with AWS?{Enter}");

      expect(askContigo).toHaveBeenCalledWith(WORKSPACE_ID, { question: "What liability do we have with AWS?" });
      expect(input).toHaveValue("");
      // Scoped to the chat log (role="log"): the same question text is also a permanently visible
      // "Try" suggestion button in the right rail (ASK_SUGGESTIONS), so an unscoped screen.getByText
      // matches both the new "You" bubble and that suggestion and throws on ambiguity.
      expect(within(screen.getByRole("log")).getByText("What liability do we have with AWS?")).toBeInTheDocument();
    });

    it("AC-4 thinking state: shows the authorise -> intent -> retrieve indicator while the request is in flight, then replaces it", async () => {
      let resolveAsk!: (value: AskContigoResult) => void;
      const pending = new Promise<AskContigoResult>((resolve) => {
        resolveAsk = resolve;
      });
      renderAsk(mockApiClient({ askContigo: vi.fn().mockReturnValue(pending) }));

      const input = screen.getByRole("textbox", { name: /ask contigo a question/i });
      await userEvent.type(input, "What liability do we have with AWS?{Enter}");

      expect(screen.getByText(/authorising scope/i)).toBeInTheDocument();

      resolveAsk(answered());

      expect(await screen.findByText("AWS liability is capped at USD 500,000.")).toBeInTheDocument();
      expect(screen.queryByText(/authorising scope/i)).not.toBeInTheDocument();
    });

    it("does nothing on Enter with a blank/whitespace-only question", async () => {
      const askContigo = vi.fn();
      renderAsk(mockApiClient({ askContigo }));

      const input = screen.getByRole("textbox", { name: /ask contigo a question/i });
      await userEvent.type(input, "   {Enter}");

      expect(askContigo).not.toHaveBeenCalled();
    });

    it("clicking a 'Try' suggestion asks it directly", async () => {
      const askContigo = vi.fn().mockResolvedValue(answered());
      renderAsk(mockApiClient({ askContigo }));

      fireEvent.click(screen.getByRole("button", { name: "What liability do we have with AWS?" }));

      await waitFor(() => expect(askContigo).toHaveBeenCalledWith(WORKSPACE_ID, { question: "What liability do we have with AWS?" }));
    });

    it("seeds and asks the query carried in router state from the global Ask bar, exactly once", async () => {
      const askContigo = vi.fn().mockResolvedValue(answered());
      renderAsk(mockApiClient({ askContigo }), { pathname: "/ask", state: { query: "What liability do we have with AWS?" } });

      await waitFor(() => expect(askContigo).toHaveBeenCalledTimes(1));
      expect(askContigo).toHaveBeenCalledWith(WORKSPACE_ID, { question: "What liability do we have with AWS?" });
    });
  });

  describe("AC-1/AC-2: answered turn", () => {
    it("shows the 'Clause retrieval…' route line and a numbered citation chip (doc · page · §)", async () => {
      const askContigo = vi.fn().mockResolvedValue(
        answered({
          citations: [{ documentId: "Document:doc-1", page: 19, section: "11.2" }],
        }),
      );
      renderAsk(mockApiClient({ askContigo }));

      await userEvent.type(screen.getByRole("textbox", { name: /ask contigo a question/i }), "What liability do we have with AWS?{Enter}");

      expect(await screen.findByText("Clause retrieval…")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /\[1\] Document:doc-1 · p\.19 §11\.2/ })).toBeInTheDocument();
    });

    it("shows the 'Structured query…' route line for a Structured-intent turn", async () => {
      const askContigo = vi.fn().mockResolvedValue(
        answered({
          intent: "Structured",
          canDetermine: false,
          answer: null,
          citations: [],
          message: "This looks like a structured/deterministic question ... ask a semantic/legal question instead.",
        }),
      );
      renderAsk(mockApiClient({ askContigo }));

      await userEvent.type(
        screen.getByRole("textbox", { name: /ask contigo a question/i }),
        "Which contracts renew in the next 45 days?{Enter}",
      );

      expect(await screen.findByText("Structured query…")).toBeInTheDocument();
    });
  });

  describe("AC-3: abstain", () => {
    it("renders the abstain block with the bold prefix and the fixed no-evidence reason for a Semantic canDetermine:false turn", async () => {
      const askContigo = vi.fn().mockResolvedValue(
        answered({ canDetermine: false, answer: null, citations: [], message: null }),
      );
      const { container } = renderAsk(mockApiClient({ askContigo }));

      await userEvent.type(
        screen.getByRole("textbox", { name: /ask contigo a question/i }),
        "What is our total liability exposure across all contracts?{Enter}",
      );

      expect(await screen.findByText(/cannot determine reliably\./i)).toBeInTheDocument();
      expect(screen.getByText(/found no supporting evidence in your accessible contracts/i)).toBeInTheDocument();
      expect(container.querySelector(".abstain-block")).not.toBeNull();
    });

    it("AC-4 unknown-question fallback also renders through the abstain block, with the backend's own message as the reason", async () => {
      const askContigo = vi.fn().mockResolvedValue(
        answered({
          intent: "Structured",
          canDetermine: false,
          answer: null,
          citations: [],
          message: "This looks like a structured/deterministic question ... ask a semantic/legal question instead.",
        }),
      );
      renderAsk(mockApiClient({ askContigo }));

      await userEvent.type(
        screen.getByRole("textbox", { name: /ask contigo a question/i }),
        "Which contracts renew in the next 45 days?{Enter}",
      );

      expect(await screen.findByText(/cannot determine reliably\./i)).toBeInTheDocument();
      expect(screen.getByText(/ask a semantic\/legal question instead/i)).toBeInTheDocument();
    });

    it("a transport/400 failure renders as its own error state, never the abstain block (an honest AI abstention is a different claim)", async () => {
      const askContigo = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, response: null, error: "A non-empty 'question' is required." });
      const { container } = renderAsk(mockApiClient({ askContigo }));

      await userEvent.type(screen.getByRole("textbox", { name: /ask contigo a question/i }), "anything{Enter}");

      expect(await screen.findByRole("alert")).toHaveTextContent("A non-empty 'question' is required.");
      expect(container.querySelector(".abstain-block")).toBeNull();
    });
  });

  describe("AC-2: citation chip opens Contract 360 > Clauses", () => {
    it("resolves a Document-sourced citation and navigates to /contracts/:id with tab=Clauses in router state", async () => {
      const askContigo = vi.fn().mockResolvedValue(answered());
      const getDocument = vi.fn().mockResolvedValue(documentOk());
      renderAsk(mockApiClient({ askContigo, getDocument }));

      await userEvent.type(screen.getByRole("textbox", { name: /ask contigo a question/i }), "What liability do we have with AWS?{Enter}");
      fireEvent.click(await screen.findByRole("button", { name: /\[1\] Document:doc-1/ }));

      expect(getDocument).toHaveBeenCalledWith(WORKSPACE_ID, "doc-1");
      expect(await screen.findByText("CONTRACT_360 contractId=22222222-2222-2222-2222-222222222222 tab=Clauses")).toBeInTheDocument();
    });

    it("shows an inline, honest notice instead of navigating when the document has no linked contract yet", async () => {
      const askContigo = vi.fn().mockResolvedValue(answered());
      const getDocument = vi.fn().mockResolvedValue(documentOk({ contractId: null }));
      renderAsk(mockApiClient({ askContigo, getDocument }));

      await userEvent.type(screen.getByRole("textbox", { name: /ask contigo a question/i }), "What liability do we have with AWS?{Enter}");
      fireEvent.click(await screen.findByRole("button", { name: /\[1\] Document:doc-1/ }));

      expect(await screen.findByText(/not linked to a contract yet/i)).toBeInTheDocument();
      expect(screen.queryByText(/^CONTRACT_360/)).not.toBeInTheDocument();
    });

    it("names the gap for a Clause-sourced citation instead of guessing a contract id", async () => {
      const askContigo = vi.fn().mockResolvedValue(
        answered({ citations: [{ documentId: "Clause:cl-1", page: 27, section: "17.2" }] }),
      );
      const getDocument = vi.fn();
      renderAsk(mockApiClient({ askContigo, getDocument }));

      await userEvent.type(screen.getByRole("textbox", { name: /ask contigo a question/i }), "What liability do we have with AWS?{Enter}");
      fireEvent.click(await screen.findByRole("button", { name: /\[1\] Clause:cl-1/ }));

      expect(getDocument).not.toHaveBeenCalled();
      expect(await screen.findByText(/can't jump to contract 360 from a clause-level citation/i)).toBeInTheDocument();
    });
  });
});
