import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import PortfolioRoute from "../../../src/routes/contracts";
import type { ApiClient, GetPortfolioResult, PortfolioListItem, PortfolioPageBody } from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

function mockApiClient(getPortfolio: ApiClient["getPortfolio"] = vi.fn()): ApiClient {
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
    // Task E16/F03/US01/T01 (ADR-020 w15 §2.2): the zero state reads the server's document counts
    // through a one-row listDocuments call, so it needs a resolved default -- an empty tenant, which
    // keeps every pre-existing zero-state assertion (the "Upload one to start." sentence) unchanged.
    listDocuments: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      page: { items: [], page: 1, pageSize: 1, totalCount: 0, counts: { all: 0, needsAttention: 0, needsReview: 0, processing: 0, rejected: 0 } },
      error: null,
    }),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    deleteAllDocuments: vi.fn(),
    prioritiseDocument: vi.fn(),
    getPortfolio,
    // This suite only exercises /contracts (PortfolioRoute itself) -- every other call is a bare
    // vi.fn(), the same convention every route suite in this folder follows.
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
    // Task E25/F04/US01/T01 (quote-benchmark-backend) added this member to `ApiClient` after this
    // helper was written; stubbed here (unrelated to this task's own scope) purely so this file's
    // own mock object satisfies the interface under `tsc --noEmit` again, the same "bare vi.fn(),
    // every other call is unused by this suite" convention every other entry above already follows.
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web) added these two to `ApiClient` after this helper was
    // written; stubbed here (unrelated to this task's own scope) purely so this file's own mock
    // object satisfies the interface under `tsc --noEmit` again, same convention as
    // `getQuoteBenchmarkHistory` immediately above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
  };
}

/** A notice deadline safely outside the 45-day window whatever today's date is, so a plain row never
 * reads as urgent by accident; urgent rows set their own deadline relative to now. */
function isoDaysFromNow(days: number): string {
  const date = new Date(Date.now() + days * 86_400_000);
  return date.toISOString().slice(0, 10);
}

function item(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "contract-1",
    supplierId: "33333333-3333-3333-3333-333333333333",
    supplierName: "Salesforce",
    type: "Msa",
    annualSpend: 640_000,
    currency: "CHF",
    startDate: "2024-03-01",
    endDate: "2027-01-15",
    renewalDate: "2027-01-15",
    cancellationDeadline: isoDaysFromNow(200),
    autoRenewal: true,
    status: "active",
    risk: "High",
    ...overrides,
  };
}

function page(items: PortfolioListItem[]): PortfolioPageBody {
  return { items, page: 1, pageSize: 100, totalCount: items.length, processingDocumentCount: 0 };
}

function ok(items: PortfolioListItem[]): GetPortfolioResult {
  return { ok: true, statusCode: 200, portfolio: page(items), error: null };
}

function renderPortfolio(apiClient: ApiClient, initialPath = "/contracts") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/contracts" element={<PortfolioRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/documents" element={<div>DOCUMENTS_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

// `?ids=` -- the Portfolio filtered to the contracts an Ask evidence card highlighted
// (`routes/ask/reply/evidenceGrouping.ts#buildEvidenceActions`). A client-side narrowing of the
// same page: one request, a notice saying how many of the highlighted contracts are shown, and
// "Show all contracts" one link away.
describe("PortfolioRoute ?ids= highlight filter", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }),
    );
  });

  const three = [
    item({ contractId: "c-1", supplierName: "Salesforce" }),
    item({ contractId: "c-2", supplierName: "Microsoft" }),
    item({ contractId: "c-3", supplierName: "Google Cloud" }),
  ];

  it("shows only the highlighted contracts, says so, and offers Show all", async () => {
    const getPortfolio = vi.fn().mockResolvedValue(ok(three));
    renderPortfolio(mockApiClient(getPortfolio), "/contracts?ids=c-1,c-3");

    const table = await screen.findByRole("table");
    expect(within(table).getByText("Salesforce")).toBeInTheDocument();
    expect(within(table).getByText("Google Cloud")).toBeInTheDocument();
    expect(within(table).queryByText("Microsoft")).not.toBeInTheDocument();
    // One request for the whole page -- the narrowing never becomes a second filter parameter.
    expect(getPortfolio).toHaveBeenCalledWith(WORKSPACE_ID, { pageSize: 100 });

    const notice = screen.getByTestId("portfolio-highlight-notice");
    expect(notice).toHaveTextContent("Showing 2 of 3 contracts — the ones highlighted in Ask.");
    expect(within(notice).getByRole("link", { name: "Show all contracts →" })).toHaveAttribute("href", "/contracts");
  });

  it("keeps the category filter on the Show all link", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok(three))), "/contracts?category=SaaS&ids=c-2");

    await screen.findByRole("table");
    expect(screen.getByRole("link", { name: "Show all contracts →" })).toHaveAttribute("href", "/contracts?category=SaaS");
  });

  it("explains an empty highlight instead of rendering an empty table or the zero state", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok(three))), "/contracts?ids=gone");

    expect(await screen.findByTestId("portfolio-highlight-notice")).toHaveTextContent(
      "None of the contracts highlighted in Ask is in the portfolio any more.",
    );
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.queryByText("Nothing to triage yet")).not.toBeInTheDocument();
  });

  it("renders no notice at all without ?ids=", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok(three))));

    await screen.findByRole("table");
    expect(screen.queryByTestId("portfolio-highlight-notice")).not.toBeInTheDocument();
  });
});

describe("PortfolioRoute (V2, screens-v2.md #6 / markup.html PORTFOLIO block)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }),
    );
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const getPortfolio = vi.fn();

    renderPortfolio(mockApiClient(getPortfolio));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(getPortfolio).not.toHaveBeenCalled();
  });

  it("shows a skeleton while the request is in flight, then the header summary and table", async () => {
    let resolveFetch!: (value: GetPortfolioResult) => void;
    const pending = new Promise<GetPortfolioResult>((resolve) => {
      resolveFetch = resolve;
    });
    const { container } = renderPortfolio(mockApiClient(vi.fn().mockReturnValue(pending)));

    expect(container.querySelector(".portfolio-skeleton")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Portfolio" })).toBeInTheDocument();

    await act(async () => {
      resolveFetch(ok([item()]));
    });

    expect(await screen.findByRole("table")).toBeInTheDocument();
    expect(container.querySelector(".portfolio-skeleton")).not.toBeInTheDocument();
  });

  it("calls getPortfolio with the current workspace id and the backend's own page-size ceiling", () => {
    const getPortfolio = vi.fn().mockResolvedValue(ok([]));
    renderPortfolio(mockApiClient(getPortfolio));

    expect(getPortfolio).toHaveBeenCalledWith(WORKSPACE_ID, { pageSize: 100 });
  });

  it("reroute state (R-WEB-02): with no listable contract the tier's own copy and 'Upload a contract' show, no table", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([item({ status: "Failed" }), item({ contractId: "p", status: "  " })]))));

    expect(await screen.findByText("Nothing to triage yet")).toBeInTheDocument();
    expect(screen.getByText("The portfolio lights up from validated contracts. Upload one to start.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Upload a contract" })).toHaveAttribute("href", "/documents");
    expect(screen.getByText("Lights up from validated contracts")).toBeInTheDocument();
    expect(screen.queryByRole("table")).toBeNull();
    expect(screen.queryByRole("button", { name: "More columns" })).toBeNull();
  });

  // Task E16/F03/US01/T01 (ADR-020 w15 §2.2, ADR-012 w15 §4): the zero state has three variants
  // and no new string, selected by the server's document counts -- never by the empty page.
  describe("zero-state variants read the server's document counts", () => {
    function countsPage(counts: { all: number; needsAttention: number; needsReview: number; processing: number; rejected: number }) {
      return { ok: true, statusCode: 200, page: { items: [], page: 1, pageSize: 1, totalCount: counts.all, counts }, error: null };
    }

    it("says the documents are still being processed, with 'Go to Documents', while any is in flight", async () => {
      const client = mockApiClient(vi.fn().mockResolvedValue(ok([])));
      (client.listDocuments as ReturnType<typeof vi.fn>).mockResolvedValue(
        countsPage({ all: 2, needsAttention: 2, needsReview: 0, processing: 2, rejected: 0 }),
      );
      renderPortfolio(client);

      expect(await screen.findByText("Your documents are still being processed. The portfolio lights up from validated contracts.")).toBeInTheDocument();
      expect(screen.getByRole("heading", { name: "Nothing to triage yet" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Go to Documents" })).toHaveAttribute("href", "/documents");
      expect(screen.queryByText(/Upload one to start/)).toBeNull();
    });

    it("drops 'Upload one to start.' once documents are held but none is in flight or validated", async () => {
      const client = mockApiClient(vi.fn().mockResolvedValue(ok([])));
      (client.listDocuments as ReturnType<typeof vi.fn>).mockResolvedValue(
        countsPage({ all: 1, needsAttention: 1, needsReview: 0, processing: 0, rejected: 0 }),
      );
      renderPortfolio(client);

      expect(await screen.findByRole("link", { name: "Go to Documents" })).toHaveAttribute("href", "/documents");
      expect(screen.getByText("The portfolio lights up from validated contracts.")).toBeInTheDocument();
      expect(screen.queryByText(/Upload one to start/)).toBeNull();
    });

    it("keeps the shipped sentence for a tenant holding only refused files -- `all` excludes Rejected", async () => {
      const client = mockApiClient(vi.fn().mockResolvedValue(ok([])));
      (client.listDocuments as ReturnType<typeof vi.fn>).mockResolvedValue(
        countsPage({ all: 0, needsAttention: 0, needsReview: 0, processing: 0, rejected: 2 }),
      );
      renderPortfolio(client);

      expect(await screen.findByText("The portfolio lights up from validated contracts. Upload one to start.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Upload a contract" })).toBeInTheDocument();
    });

    it("stops re-reading after five minutes without a change and offers 'Check again' beside the CTA", async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      try {
        const getPortfolio = vi.fn().mockResolvedValue(ok([]));
        const client = mockApiClient(getPortfolio);
        const listDocuments = client.listDocuments as ReturnType<typeof vi.fn>;
        listDocuments.mockResolvedValue(countsPage({ all: 1, needsAttention: 1, needsReview: 0, processing: 1, rejected: 0 }));
        renderPortfolio(client);

        await screen.findByText(/still being processed/);
        const callsBefore = listDocuments.mock.calls.length;
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4_000);
        });
        expect(listDocuments.mock.calls.length).toBeGreaterThan(callsBefore);

        await act(async () => {
          await vi.advanceTimersByTimeAsync(5 * 60_000);
        });
        expect(await screen.findByText("Nothing has changed for five minutes, so this page stopped checking for updates.")).toBeInTheDocument();
        // The sentence and the primary CTA are unchanged; nothing is re-labelled.
        expect(screen.getByText(/still being processed/)).toBeInTheDocument();
        expect(screen.getByRole("link", { name: "Go to Documents" })).toBeInTheDocument();

        const callsWhenPaused = listDocuments.mock.calls.length;
        await act(async () => {
          await vi.advanceTimersByTimeAsync(10_000);
        });
        expect(listDocuments.mock.calls.length).toBe(callsWhenPaused);

        fireEvent.click(screen.getByRole("button", { name: "Check again" }));
        await act(async () => {
          await vi.advanceTimersByTimeAsync(0);
        });
        expect(listDocuments.mock.calls.length).toBe(callsWhenPaused + 1);
      } finally {
        vi.useRealTimers();
      }
    });
  });

  it("error state: a hung getPortfolio leaves loading, then the unavailable/Retry path", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const getPortfolio = vi.fn().mockReturnValue(new Promise(() => {}));
      renderPortfolio(mockApiClient(getPortfolio));

      expect(await screen.findByRole("status")).toHaveTextContent(/Loading portfolio/);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(15_000);
      });

      expect(await screen.findByText(/temporarily unavailable/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /retry/i })).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it("error state: a 503 renders a plain-language message with a Retry that re-fetches", async () => {
    const getPortfolio = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: 503, portfolio: null, error: "Service Unavailable" })
      .mockResolvedValueOnce(ok([item()]));
    renderPortfolio(mockApiClient(getPortfolio));

    expect(await screen.findByText(/temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.getByRole("alert")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByRole("table")).toBeInTheDocument();
    expect(getPortfolio).toHaveBeenCalledTimes(2);
  });

  it("renders the pfSummary line from the validated rows: count, per-currency annual spend, urgent deadlines", async () => {
    renderPortfolio(
      mockApiClient(
        vi.fn().mockResolvedValue(
          ok([
            item({ contractId: "a", annualSpend: 640_000, cancellationDeadline: isoDaysFromNow(20) }),
            item({ contractId: "b", supplierName: "Microsoft", annualSpend: 1_200_000, cancellationDeadline: isoDaysFromNow(200) }),
            item({ contractId: "c", status: "needs_review", annualSpend: 9_999_999 }),
          ]),
        ),
      ),
    );

    expect(await screen.findByText("2 validated contracts · CHF 1.8M annual · 1 notice deadline within 45 days")).toBeInTheDocument();
  });

  it("renders the V2 columns, sorted by notice deadline, with the urgent row tinted and its day count shown", async () => {
    renderPortfolio(
      mockApiClient(
        vi.fn().mockResolvedValue(
          ok([
            item({ contractId: "later", supplierName: "Microsoft", cancellationDeadline: isoDaysFromNow(200) }),
            item({ contractId: "soon", supplierName: "Salesforce", cancellationDeadline: isoDaysFromNow(40) }),
          ]),
        ),
      ),
    );

    const table = await screen.findByRole("table");
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Supplier",
      "Contract",
      "Annual spend",
      "Ends",
      "Give notice by",
      "Status",
    ]);

    const rows = within(table).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByText("Salesforce")).toBeInTheDocument();
    expect(rows[0]).toHaveClass("row-critical");
    expect(within(rows[0]).getByText("· 40 d")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Microsoft")).toBeInTheDocument();
    expect(rows[1]).not.toHaveClass("row-critical");
    // Validated rows only, so no processing/review statuses ever appear; the business status does.
    expect(within(rows[0]).getByText("Active")).toHaveClass("tag", "tag-neutral", "portfolio-status-tag");
    // No filter control anywhere in the header: one uppercase label per column, as in the prototype.
    expect(within(table).queryByRole("textbox")).toBeNull();
    expect(within(table).queryByRole("combobox")).toBeNull();
  });

  it("formats the cells as the prototype's fixtures do: compact spend with the row's currency, DD Mon YYYY dates", async () => {
    renderPortfolio(
      mockApiClient(vi.fn().mockResolvedValue(ok([item({ annualSpend: 640_000, currency: "CHF", endDate: "2027-01-15", cancellationDeadline: "2026-10-18" })]))),
    );

    const table = await screen.findByRole("table");
    expect(within(table).getByText("CHF 640k")).toBeInTheDocument();
    expect(within(table).getByText("15 Jan 2027")).toBeInTheDocument();
    expect(within(table).getByText("18 Oct 2026")).toBeInTheDocument();
  });

  it("forwards ?category= from the URL to getPortfolio", async () => {
    const getPortfolio = vi.fn().mockResolvedValue(ok([]));
    renderPortfolio(mockApiClient(getPortfolio), "/contracts?category=Software");

    await screen.findByText("Nothing to triage yet");
    expect(getPortfolio).toHaveBeenCalledWith(WORKSPACE_ID, expect.objectContaining({ category: "Software" }));
  });

  it("lists validated contracts only: a still-processing or needs-review upload never reaches the table, and the reroute state shows when none is validated", async () => {
    renderPortfolio(
      mockApiClient(
        vi.fn().mockResolvedValue(
          ok([
            item({ contractId: "pending", supplierName: "Uploading Co", status: "processing", documentProcessingStatus: "Uploaded", fileName: "pending.pdf" }),
            item({ contractId: "review", supplierName: "Review Co", status: "needs_review", documentProcessingStatus: "NeedsReview" }),
          ]),
        ),
      ),
    );

    expect(await screen.findByText("Nothing to triage yet")).toBeInTheDocument();
    expect(screen.queryByRole("table")).toBeNull();
    expect(screen.queryByText("Uploading Co")).toBeNull();
    expect(screen.queryByText("Review Co")).toBeNull();
    expect(screen.queryByRole("group", { name: /readiness/i })).toBeNull();
  });

  it("'More columns' reveals Start · Auto · Risk and reads 'Fewer columns' while expanded", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([item({ risk: "High", autoRenewal: true })]))));

    await screen.findByRole("table");
    const toggle = screen.getByRole("button", { name: "More columns" });
    fireEvent.click(toggle);

    expect(screen.getByRole("button", { name: "Fewer columns" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Supplier",
      "Contract",
      "Annual spend",
      "Ends",
      "Give notice by",
      "Start",
      "Auto",
      "Risk",
      "Status",
    ]);
    const tbody = screen.getByRole("table").querySelector("tbody") as HTMLElement;
    expect(within(tbody).getByText("High")).toBeInTheDocument();
    expect(within(tbody).getByText("Yes")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Fewer columns" }));
    expect(screen.queryByText("Start")).toBeNull();
  });

  it("rows open Contract 360: the Contract cell is a real link, and clicking elsewhere on the row follows it too", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([item({ contractId: "c-1" }), item({ contractId: "c-2", supplierName: "Microsoft" })]))));

    const table = await screen.findByRole("table");
    const rows = within(table).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByRole("link", { name: "MSA" })).toHaveAttribute("href", "/contracts/c-1");

    fireEvent.click(within(rows[1]).getByText("Microsoft"));
    expect(await screen.findByText("CONTRACT_360_SCREEN")).toBeInTheDocument();
  });

  it("shows an honest em dash for a validated contract with no linked supplier, never an id fragment", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([item({ supplierId: null, supplierName: null })]))));

    const table = await screen.findByRole("table");
    expect(within(table).getByText("—")).toBeInTheDocument();
    expect(within(table).queryByText(/^Supplier [0-9a-f]{8}$/)).toBeNull();
  });
});
