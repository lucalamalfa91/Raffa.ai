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
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    getPortfolio,
    // This suite only exercises /contracts (PortfolioRoute itself) -- every other call is a bare
    // vi.fn(), the same convention every route suite in this folder follows.
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
    askContigo: vi.fn(),
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
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
  return { items, page: 1, pageSize: 100, totalCount: items.length };
}

function ok(items: PortfolioListItem[]): GetPortfolioResult {
  return { ok: true, statusCode: 200, portfolio: page(items), error: null };
}

function renderPortfolio(apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={["/contracts"]}>
      <Routes>
        <Route path="/contracts" element={<PortfolioRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/documents" element={<div>DOCUMENTS_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("PortfolioRoute (V2, screens-v2.md #6 / markup.html PORTFOLIO block)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
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

  it("reroute state (R-WEB-02): with no validated contract the tier's own copy and 'Upload a contract' show, no table", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([item({ status: "needs_review" }), item({ contractId: "p", status: "processing" })]))));

    expect(await screen.findByText("Nothing to triage yet")).toBeInTheDocument();
    expect(screen.getByText("The portfolio lights up from validated contracts. Upload one to start.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Upload a contract" })).toHaveAttribute("href", "/documents");
    expect(screen.getByText("Lights up from validated contracts")).toBeInTheDocument();
    expect(screen.queryByRole("table")).toBeNull();
    expect(screen.queryByRole("button", { name: "More columns" })).toBeNull();
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
    expect(within(rows[0]).getByText("Active")).toBeInTheDocument();
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
    expect(screen.getByText("High risk")).toBeInTheDocument();
    expect(screen.getByText("Yes")).toBeInTheDocument();

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
