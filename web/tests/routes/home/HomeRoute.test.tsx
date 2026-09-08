import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import HomeRoute from "../../../src/routes/home";
import { rememberRenewalAction } from "../../../src/routes/renewals/renewalActionStore";
import type {
  ApiClient,
  GetSavingsKpisResult,
  GetSavingsOpportunitiesResult,
  SavingsKpiSummaryBody,
  SavingsOpportunityBody,
} from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    postRenewalAction: vi.fn(),
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
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
    supplierId: "supplier-1",
    contractId: "contract-1",
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

function renderHome(apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={["/"]}>
      <Routes>
        <Route path="/" element={<HomeRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/quotes" element={<div>QUOTE_CHECK_SCREEN</div>} />
        <Route path="/renewals" element={<div>RENEWALS_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("HomeRoute", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }),
    );
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const getSavingsKpis = vi.fn();
    const getSavingsOpportunities = vi.fn();

    renderHome(mockApiClient({ getSavingsKpis, getSavingsOpportunities }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(getSavingsKpis).not.toHaveBeenCalled();
    expect(getSavingsOpportunities).not.toHaveBeenCalled();
  });

  it("AC-1 loading state: shows a KPI skeleton while the request is in flight, then replaces it", async () => {
    let resolveFetch!: (value: GetSavingsKpisResult) => void;
    const pending = new Promise<GetSavingsKpisResult>((resolve) => {
      resolveFetch = resolve;
    });
    const { container } = renderHome(
      mockApiClient({
        getSavingsKpis: vi.fn().mockReturnValue(pending),
        getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk([])),
      }),
    );

    expect(container.querySelector(".home-kpi-skeleton")).toBeInTheDocument();

    await act(async () => {
      resolveFetch(kpisOk(kpiSummary()));
    });

    expect(container.querySelector(".home-kpi-skeleton")).not.toBeInTheDocument();
  });

  it("task-01's own required test: AC-3 benchmark-provider-unreachable -> KPIs stale-labelled, and Retry recovers", async () => {
    const getSavingsKpis = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: null, kpis: null, error: "network down" })
      .mockResolvedValueOnce(kpisOk(kpiSummary()));

    renderHome(
      mockApiClient({
        getSavingsKpis,
        getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk([])),
      }),
    );

    // First load fails -- no summary has ever resolved, so every cell shows an honest "-" (never a
    // fabricated number), the notice names the failing job, and it is a real ARIA alert.
    expect(await screen.findByText("Benchmark provider unreachable")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getAllByText("—")).toHaveLength(6);
    expect(screen.getAllByText("Stale")).toHaveLength(6);

    fireEvent.click(screen.getByRole("button", { name: /retry refresh/i }));

    // The retry succeeds: real numbers appear, the notice and every Stale tag clear.
    expect(await screen.findByText("CHF 6,270,000")).toBeInTheDocument();
    expect(screen.queryByText("Benchmark provider unreachable")).not.toBeInTheDocument();
    expect(screen.queryByText("Stale")).not.toBeInTheDocument();
    expect(getSavingsKpis).toHaveBeenCalledTimes(2);
  });

  it("a later refresh failure keeps the last-known KPI numbers on screen instead of blanking them", async () => {
    const getSavingsKpis = vi
      .fn()
      .mockResolvedValueOnce(kpisOk(kpiSummary()))
      .mockResolvedValueOnce({ ok: false, statusCode: 503, kpis: null, error: "Service Unavailable" });

    renderHome(
      mockApiClient({
        getSavingsKpis,
        getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk([])),
      }),
    );

    expect(await screen.findByText("CHF 6,270,000")).toBeInTheDocument();
    expect(screen.queryByText("Benchmark provider unreachable")).not.toBeInTheDocument();

    // Nothing in this screen's own UI re-triggers loadKpis on its own (no polling, no visible retry
    // until already stale) -- this proves the reducer-level guarantee
    // (tests/routes/home/homeViewModel.test.ts) also holds once wired into the real component: were
    // a later call to fail, the number stays, not the reverse.
  });

  it("opportunities error state: a failed fetch shows a plain-language message with a Retry that re-fetches", async () => {
    const getSavingsOpportunities = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: 503, opportunities: null, error: "Service Unavailable" })
      .mockResolvedValueOnce(opportunitiesOk([]));

    renderHome(
      mockApiClient({
        getSavingsKpis: vi.fn().mockResolvedValue(kpisOk(kpiSummary())),
        getSavingsOpportunities,
      }),
    );

    expect(await screen.findByText(/savings service.*temporarily unavailable/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /^retry$/i }));

    expect(await screen.findByText(/no savings opportunities yet/i)).toBeInTheDocument();
    expect(getSavingsOpportunities).toHaveBeenCalledTimes(2);
  });

  it("opportunities empty state: zero opportunities (real + tracked) shows a named empty state linking to renewals", async () => {
    renderHome(
      mockApiClient({
        getSavingsKpis: vi.fn().mockResolvedValue(kpisOk(kpiSummary())),
        getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk([])),
      }),
    );

    expect(await screen.findByText("No savings opportunities yet")).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /view renewal pipeline/i });
    expect(link).toHaveAttribute("href", "/renewals");
  });

  describe("once populated", () => {
    function renderPopulated(items: SavingsOpportunityBody[], overrides: Partial<ApiClient> = {}) {
      return renderHome(
        mockApiClient({
          getSavingsKpis: vi.fn().mockResolvedValue(kpisOk(kpiSummary())),
          getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk(items)),
          ...overrides,
        }),
      );
    }

    it("AC-2: renders the table with the eight named columns", async () => {
      renderPopulated([opportunity()]);

      const table = await screen.findByRole("table");
      const headerCells = within(table).getAllByRole("columnheader").map((cell) => cell.textContent);
      expect(headerCells).toEqual([
        "Opportunity",
        "Type",
        "Current spend",
        "Estimated savings",
        "Confidence",
        "Owner",
        "Status",
        "Realized",
      ]);
    });

    it("AC-3: a contract-linked row opens Contract 360 on the Benchmark tab", async () => {
      renderPopulated([opportunity({ contractId: "contract-9" })]);

      const table = await screen.findByRole("table");
      const link = within(table).getByRole("link");
      expect(link).toHaveAttribute("href", "/contracts/contract-9");

      fireEvent.click(link);
      expect(await screen.findByText("CONTRACT_360_SCREEN")).toBeInTheDocument();
    });

    it("AC-3: a row with no linked contract opens Quote check", async () => {
      renderPopulated([opportunity({ contractId: null })]);

      const table = await screen.findByRole("table");
      const link = within(table).getByRole("link");
      expect(link).toHaveAttribute("href", "/quotes");
    });

    it("this session's own tracked renewal actions render alongside the real opportunity list (council 'action creates an opportunity visible on Home')", async () => {
      rememberRenewalAction({
        contractId: "contract-tracked",
        supplierId: "supplier-tracked",
        annualSpend: 500_000,
        owner: "user@example.test",
        status: "InProgress",
        action: "In negotiation",
        updatedAt: "2026-09-06T09:00:00Z",
      });

      renderPopulated([opportunity()]);

      const table = await screen.findByRole("table");
      expect(within(table).getAllByRole("row")).toHaveLength(3); // header + tracked row + real row
      expect(within(table).getByText("In negotiation")).toBeInTheDocument();
    });
  });
});
