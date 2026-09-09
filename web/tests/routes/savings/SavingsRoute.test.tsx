import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import SavingsRoute from "../../../src/routes/savings";
import { rememberRenewalAction } from "../../../src/routes/renewals/renewalActionStore";
import type {
  ApiClient,
  GetPortfolioResult,
  GetSavingsKpisResult,
  GetSavingsOpportunitiesResult,
  PortfolioPageBody,
  SavingsKpiSummaryBody,
  SavingsOpportunityBody,
} from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const CONTRACT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const SUPPLIER_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const TRACKED_CONTRACT_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
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
    // Supplier names come from the portfolio; the default here is an honest "portfolio unavailable".
    getPortfolio: vi.fn().mockResolvedValue({ ok: false, statusCode: 503, portfolio: null, error: "Service Unavailable" }),
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
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    ...overrides,
  };
}

function kpiSummary(overrides: Partial<SavingsKpiSummaryBody> = {}): SavingsKpiSummaryBody {
  return {
    annualSpendAnalyzed: [{ currency: "CHF", amount: 6_270_000, contractCount: 9 }],
    contractsAnalyzedCount: 9,
    savingsIdentified: [{ currency: "CHF", low: 410_000, high: 590_000, count: 6, averageConfidence: 0.82 }],
    savingsInProgress: [{ currency: "CHF", low: 240_000, high: 240_000, count: 2, averageConfidence: 0.75 }],
    savingsRealized: [{ currency: "CHF", low: 85_000, high: 85_000, count: 1, averageConfidence: 0.91 }],
    upcomingRenewalsCount: 4,
    ...overrides,
  };
}

function opportunity(overrides: Partial<SavingsOpportunityBody> = {}): SavingsOpportunityBody {
  return {
    id: "opp-1",
    supplierId: SUPPLIER_ID,
    contractId: CONTRACT_ID,
    type: "Renewal",
    currentSpend: 640_000,
    currency: "CHF",
    estimatedSavingsLow: 80_000,
    estimatedSavingsHigh: 120_000,
    confidence: 0.92,
    confidenceLevel: "High",
    status: "Identified",
    owner: null,
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: "2026-08-01T00:00:00Z",
    realizedAmount: null,
    ...overrides,
  };
}

function kpisOk(kpis: SavingsKpiSummaryBody): GetSavingsKpisResult {
  return { ok: true, statusCode: 200, kpis, error: null };
}

function opportunitiesOk(items: SavingsOpportunityBody[]): GetSavingsOpportunitiesResult {
  return { ok: true, statusCode: 200, opportunities: { items, totalCount: items.length }, error: null };
}

function portfolioOk(items: Array<{ contractId: string; supplierName: string | null }>): GetPortfolioResult {
  const portfolio: PortfolioPageBody = {
    items: items.map((item) => ({
      contractId: item.contractId,
      supplierId: SUPPLIER_ID,
      supplierName: item.supplierName,
      type: "Msa",
      status: "active",
      startDate: "2025-01-01",
      endDate: "2026-12-31",
      renewalDate: "2026-12-31",
      cancellationDeadline: "2026-10-01",
      autoRenewal: true,
      annualSpend: 640_000,
      currency: "CHF",
      risk: "High",
    })),
    totalCount: items.length,
    page: 1,
    pageSize: 100,
  };
  return { ok: true, statusCode: 200, portfolio, error: null };
}

function renderSavings(apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={["/savings"]}>
      <Routes>
        <Route path="/savings" element={<SavingsRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/quotes" element={<div>QUOTE_CHECK_SCREEN</div>} />
        <Route path="/renewals" element={<div>RENEWALS_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("SavingsRoute (V2, ADR-024 / screens-v2.md #8)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("contigo.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const getSavingsKpis = vi.fn();
    const getSavingsOpportunities = vi.fn();
    const getPortfolio = vi.fn();

    renderSavings(mockApiClient({ getSavingsKpis, getSavingsOpportunities, getPortfolio }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(getSavingsKpis).not.toHaveBeenCalled();
    expect(getSavingsOpportunities).not.toHaveBeenCalled();
    expect(getPortfolio).not.toHaveBeenCalled();
  });

  it("shows a KPI skeleton while the request is in flight, then the three V2 cells with their meta lines", async () => {
    let resolveFetch!: (value: GetSavingsKpisResult) => void;
    const pending = new Promise<GetSavingsKpisResult>((resolve) => {
      resolveFetch = resolve;
    });
    const { container } = renderSavings(
      mockApiClient({
        getSavingsKpis: vi.fn().mockReturnValue(pending),
        getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk([])),
      }),
    );

    expect(container.querySelector(".savings-kpi-skeleton")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Savings" })).toBeInTheDocument();

    await act(async () => {
      resolveFetch(kpisOk(kpiSummary()));
    });

    expect(container.querySelector(".savings-kpi-skeleton")).not.toBeInTheDocument();
    const band = screen.getByRole("group", { name: "Savings KPIs" });
    const cells = band.querySelectorAll(".savings-kpi-cell");
    expect(cells).toHaveLength(3);
    expect(cells[0]).toHaveTextContent("Contracts analyzed");
    expect(cells[0]).toHaveTextContent("9");
    expect(cells[0]).toHaveTextContent("CHF 6,270,000 annual spend");
    expect(cells[1]).toHaveTextContent("Upcoming renewals");
    expect(cells[1]).toHaveTextContent("4");
    expect(cells[2]).toHaveTextContent("Savings identified");
    expect(cells[2]).toHaveTextContent("CHF 410,000–590,000");
    expect(cells[2]).toHaveTextContent("6 identified · 2 in progress · 1 realized");
  });

  it("benchmark-provider-unreachable -> KPIs stale-labelled, and Retry recovers", async () => {
    const getSavingsKpis = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: null, kpis: null, error: "network down" })
      .mockResolvedValueOnce(kpisOk(kpiSummary()));

    renderSavings(mockApiClient({ getSavingsKpis, getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk([])) }));

    expect(await screen.findByText("Benchmark provider unreachable")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getAllByText("—")).toHaveLength(3);
    expect(screen.getAllByText("Stale")).toHaveLength(3);

    fireEvent.click(screen.getByRole("button", { name: /retry refresh/i }));

    expect(await screen.findByText("CHF 410,000–590,000")).toBeInTheDocument();
    expect(screen.queryByText("Benchmark provider unreachable")).not.toBeInTheDocument();
    expect(screen.queryByText("Stale")).not.toBeInTheDocument();
    expect(getSavingsKpis).toHaveBeenCalledTimes(2);
  });

  it("opportunities error state: a failed fetch shows a plain-language message with a Retry that re-fetches", async () => {
    const getSavingsOpportunities = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: 503, opportunities: null, error: "Service Unavailable" })
      .mockResolvedValueOnce(opportunitiesOk([]));

    renderSavings(mockApiClient({ getSavingsKpis: vi.fn().mockResolvedValue(kpisOk(kpiSummary())), getSavingsOpportunities }));

    expect(await screen.findByText(/savings service.*temporarily unavailable/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /^retry$/i }));

    expect(await screen.findByText("No savings opportunities yet")).toBeInTheDocument();
    expect(getSavingsOpportunities).toHaveBeenCalledTimes(2);
  });

  it("reroute: zero opportunities (real + tracked) explains itself and sends the reader to Renewals", async () => {
    renderSavings(
      mockApiClient({
        getSavingsKpis: vi.fn().mockResolvedValue(kpisOk(kpiSummary({ savingsIdentified: [] }))),
        getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk([])),
      }),
    );

    expect(await screen.findByText("No savings opportunities yet")).toBeInTheDocument();
    expect(screen.getByText("Opportunities appear once a renewal is actioned or a saving is identified from validated contracts.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open renewals" })).toHaveAttribute("href", "/renewals");
    expect(screen.getByText("Lights up from validated contracts and actioned renewals")).toBeInTheDocument();
  });

  describe("once populated", () => {
    function renderPopulated(items: SavingsOpportunityBody[], overrides: Partial<ApiClient> = {}) {
      return renderSavings(
        mockApiClient({
          getSavingsKpis: vi.fn().mockResolvedValue(kpisOk(kpiSummary())),
          getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk(items)),
          getPortfolio: vi.fn().mockResolvedValue(portfolioOk([{ contractId: CONTRACT_ID, supplierName: "Salesforce" }, { contractId: TRACKED_CONTRACT_ID, supplierName: "Fabrikam" }])),
          ...overrides,
        }),
      );
    }

    it("renders the four V2 columns with the real supplier name, the estimate + confidence, the status, and the summary", async () => {
      renderPopulated([opportunity()]);

      const table = await screen.findByRole("table");
      expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Supplier", "Action", "Estimate", "Status"]);
      expect(await within(table).findByRole("link", { name: "Salesforce" })).toHaveAttribute("href", `/contracts/${CONTRACT_ID}`);
      expect(within(table).getByText("Renewal")).toBeInTheDocument();
      expect(within(table).getByText(/CHF 80,000–120,000/)).toBeInTheDocument();
      expect(within(table).getByText("High · 92%")).toHaveClass("tag-neutral");
      expect(within(table).getByText("Identified")).toHaveClass("tag-neutral");
      expect(screen.getByText("1 opportunity · CHF 410,000–590,000 identified")).toBeInTheDocument();
    });

    it("falls back to the id-fragment label when the portfolio cannot supply a name, never a fabricated one", async () => {
      renderPopulated([opportunity()], {
        getPortfolio: vi.fn().mockResolvedValue({ ok: false, statusCode: 503, portfolio: null, error: "Service Unavailable" }),
      });

      const table = await screen.findByRole("table");
      expect(within(table).getByRole("link", { name: "Supplier bbbbbbbb" })).toBeInTheDocument();
    });

    it("rows open Contract 360: the supplier link and a click anywhere on the row", async () => {
      renderPopulated([opportunity()]);

      const table = await screen.findByRole("table");
      fireEvent.click(within(table).getByText("Renewal"));
      expect(await screen.findByText("CONTRACT_360_SCREEN")).toBeInTheDocument();
    });

    it("a row with no linked contract opens Quote check", async () => {
      renderPopulated([opportunity({ contractId: null })]);

      const table = await screen.findByRole("table");
      expect(within(table).getByRole("link")).toHaveAttribute("href", "/quotes");
    });

    it("this session's own tracked renewal actions render first, alongside the real list", async () => {
      rememberRenewalAction({
        contractId: TRACKED_CONTRACT_ID,
        supplierId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
        annualSpend: 500_000,
        owner: "user@example.test",
        status: "InProgress",
        action: "In negotiation",
        updatedAt: "2026-09-06T09:00:00Z",
      });

      renderPopulated([opportunity()]);

      const table = await screen.findByRole("table");
      await within(table).findByRole("link", { name: "Fabrikam" });
      const bodyRows = within(table).getAllByRole("row").slice(1);
      expect(bodyRows).toHaveLength(2);
      expect(bodyRows[0]).toHaveTextContent("Fabrikam");
      expect(within(bodyRows[0]).getByText("In negotiation")).toHaveClass("tag-accent");
      expect(bodyRows[0]).toHaveTextContent("Not yet available");
      expect(screen.getByText("2 opportunities · CHF 410,000–590,000 identified")).toBeInTheDocument();
    });
  });
});
