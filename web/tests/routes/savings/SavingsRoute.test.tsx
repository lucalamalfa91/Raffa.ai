import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import SavingsRoute from "../../../src/routes/savings";
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
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    deleteAllDocuments: vi.fn(),
    prioritiseDocument: vi.fn(),
    // Supplier names come from the portfolio; the default here is an honest "portfolio unavailable".
    getPortfolio: vi.fn().mockResolvedValue({ ok: false, statusCode: 503, portfolio: null, error: "Service Unavailable" }),
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
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    postConversationFeedback: vi.fn(),
    deleteConversation: vi.fn(),
    renameConversation: vi.fn(),
    restoreConversation: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): this suite never reaches the Renewals screen -- bare
    // vi.fn() is enough, same convention as the other unexercised calls above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
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
    savingsRealized: [{ currency: "CHF", amount: 85_000, count: 1 }],
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
    processingDocumentCount: 0,
  };
  return { ok: true, statusCode: 200, portfolio, error: null };
}

/** Lands wherever an Ask launch navigates, and shows the query string and router state it carried. */
function AskProbe() {
  const location = useLocation();
  return (
    <div>
      ASK_SCREEN
      <span data-testid="ask-search">{location.search}</span>
      <span data-testid="ask-state">{JSON.stringify(location.state)}</span>
    </div>
  );
}

function renderSavings(apiClient: ApiClient, initialEntry = "/savings") {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/savings" element={<SavingsRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/quotes" element={<div>QUOTE_CHECK_SCREEN</div>} />
        <Route path="/renewals" element={<div>RENEWALS_SCREEN</div>} />
        <Route path="/contracts" element={<div>PORTFOLIO_SCREEN</div>} />
        <Route path="/ask" element={<AskProbe />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("SavingsRoute (V2, ADR-024 / screens-v2.md #8)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
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

  it("shows a KPI skeleton while the request is in flight, then the four V2 cells with their meta lines", async () => {
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
    expect(cells).toHaveLength(4);
    expect(cells[0]).toHaveTextContent("Savings verified");
    expect(cells[0]).toHaveClass("is-hero");
    expect(cells[0]).toHaveTextContent("CHF 85,000");
    expect(cells[0]).toHaveTextContent("from 1 recorded outcome");
    expect(cells[0]).toHaveTextContent("1.4% of CHF annual spend");
    expect(cells[1]).toHaveTextContent("Savings identified");
    expect(cells[1]).toHaveTextContent("CHF 410,000–590,000");
    expect(cells[1]).toHaveTextContent("6 opportunities · avg. confidence 82%");
    expect(cells[1]).not.toHaveTextContent("realized");
    expect(cells[2]).toHaveTextContent("Savings in progress");
    expect(cells[2]).toHaveTextContent("CHF 240,000");
    expect(cells[3]).toHaveTextContent("Savings potential");
    expect(cells[3]).toHaveTextContent("10–13%");

    // Portfolio context under the band: each figure leads to the screen that owns it.
    const context = screen.getByRole("navigation", { name: "Portfolio context" });
    expect(within(context).getByRole("link", { name: /Contracts analyzed\s*9/ })).toHaveAttribute("href", "/contracts");
    expect(within(context).getByRole("link", { name: /Annual spend analyzed\s*CHF 6,270,000/ })).toHaveAttribute("href", "/contracts");
    expect(within(context).getByRole("link", { name: /Upcoming renewals\s*4/ })).toHaveAttribute("href", "/renewals");
  });

  it("benchmark-provider-unreachable -> KPIs stale-labelled, and Retry recovers", async () => {
    const getSavingsKpis = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: null, kpis: null, error: "network down" })
      .mockResolvedValueOnce(kpisOk(kpiSummary()));

    renderSavings(mockApiClient({ getSavingsKpis, getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk([])) }));

    expect(await screen.findByText("Benchmark provider unreachable")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(within(screen.getByRole("group", { name: "Savings KPIs" })).getAllByText("—")).toHaveLength(4);
    expect(screen.getAllByText("Stale")).toHaveLength(4);
    expect(screen.getByText("Savings verified")).toBeInTheDocument();

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
    expect(screen.getByRole("link", { name: "Ask Raffa where to save" })).toHaveAttribute("href", "/ask");
    expect(screen.getByText("Lights up from validated contracts and actioned renewals")).toBeInTheDocument();
    // No charts over nothing.
    expect(screen.queryByRole("heading", { name: "When you saved" })).not.toBeInTheDocument();
  });

  it("the header's Ask Raffa opens a new chat that asks where to save", async () => {
    renderSavings(
      mockApiClient({
        getSavingsKpis: vi.fn().mockResolvedValue(kpisOk(kpiSummary())),
        getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk([])),
      }),
    );

    fireEvent.click(await screen.findByRole("link", { name: "Ask Raffa where to save →" }));
    expect(await screen.findByText("ASK_SCREEN")).toBeInTheDocument();
    expect(screen.getByTestId("ask-state").textContent).toBe(JSON.stringify({ query: "Where can we save?", newChat: true }));
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

      const table = await screen.findByRole("table", { name: "Opportunities" });
      expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
        "Supplier",
        "Lever",
        "Current spend",
        "Estimate",
        "Status",
        "Notice in",
        "Next step",
      ]);
      expect(await within(table).findByRole("link", { name: "Salesforce" })).toHaveAttribute("href", `/contracts/${CONTRACT_ID}`);
      expect(within(table).getByText("CHF 640,000")).toBeInTheDocument();
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

      const table = await screen.findByRole("table", { name: "Opportunities" });
      expect(within(table).getByRole("link", { name: "Supplier bbbbbbbb" })).toBeInTheDocument();
    });

    it("rows open Contract 360: the supplier link and a click anywhere on the row", async () => {
      renderPopulated([opportunity()]);

      const table = await screen.findByRole("table", { name: "Opportunities" });
      fireEvent.click(within(table).getByText("Renewal"));
      expect(await screen.findByText("CONTRACT_360_SCREEN")).toBeInTheDocument();
    });

    it("a row with no linked contract opens Quote check", async () => {
      renderPopulated([opportunity({ contractId: null })]);

      const table = await screen.findByRole("table", { name: "Opportunities" });
      expect(within(table).getByRole("link", { name: "Supplier bbbbbbbb" })).toHaveAttribute("href", "/quotes");
      // No contract to bind Ask to, and no notice date to work in Renewals.
      expect(within(table).getByRole("link", { name: /Ask Raffa about/ })).toHaveAttribute("href", "/ask");
      expect(within(table).queryByRole("link", { name: /in Renewals/ })).not.toBeInTheDocument();
    });

    describe("dashboard", () => {
      beforeEach(() => {
        // Only Date is faked: notice days and the twelve-month window are counted from "today".
        vi.useFakeTimers({ toFake: ["Date"] });
        vi.setSystemTime(new Date("2026-09-23T10:00:00Z"));
      });
      afterEach(() => {
        vi.useRealTimers();
      });

      const items = () => [
        opportunity(),
        opportunity({ id: "opp-2", contractId: TRACKED_CONTRACT_ID, status: "InProgress", estimatedSavingsLow: 40_000, estimatedSavingsHigh: 60_000 }),
        opportunity({
          id: "opp-3",
          contractId: TRACKED_CONTRACT_ID,
          type: "Benchmark",
          status: "Realized",
          realizedAmount: 50_000,
          createdAt: "2026-05-01T00:00:00Z",
          updatedAt: "2026-09-15T00:00:00Z",
        }),
      ];

      it("leads with the pipeline, when you saved, where the savings are and the deadline queue", async () => {
        renderPopulated(items());

        const pipeline = await screen.findByRole("region", { name: "From identified to verified" });
        expect(within(pipeline).getByText("25% of the pipeline is already verified money")).toBeInTheDocument();
        expect(within(pipeline).getByRole("button", { name: /Show identified opportunities \(CHF 80k–120k, 1 opportunity\)/ })).toBeInTheDocument();

        const when = screen.getByRole("region", { name: "When you saved" });
        expect(within(when).getByText("Verified in 2026").nextSibling).toHaveTextContent("CHF 50,000");
        expect(within(when).getByText("Last verified saving").nextSibling).toHaveTextContent("15 Sep 2026");
        expect(within(when).getByRole("group", { name: "Savings by month, CHF" })).toBeInTheDocument();
        expect(within(when).getByLabelText(/^September 2026: CHF 50,000 verified from 1 outcome/)).toBeInTheDocument();

        const recent = screen.getByRole("region", { name: "Latest verified savings" });
        expect(within(recent).getByRole("link", { name: "Fabrikam" })).toHaveAttribute("href", `/contracts/${TRACKED_CONTRACT_ID}`);

        const where = screen.getByRole("region", { name: "Where the savings are" });
        expect(within(where).getByRole("button", { name: "Salesforce" })).toBeInTheDocument();
        fireEvent.click(within(where).getByRole("button", { name: "By lever" }));
        expect(within(where).getByText("Benchmark")).toBeInTheDocument();

        // Both contracts give notice on 01 Oct 2026, 8 days away: the two open savings are queued,
        // the realized Fabrikam one is not.
        const deadlines = screen.getByRole("region", { name: "Act before the notice deadline" });
        expect(within(deadlines).getAllByText("8")).toHaveLength(2);
        expect(within(deadlines).getAllByRole("link", { name: "Work it in Renewals" }).map((link) => link.getAttribute("href"))).toEqual([
          `/renewals?select=${CONTRACT_ID}`,
          `/renewals?select=${TRACKED_CONTRACT_ID}`,
        ]);
        expect(within(deadlines).getByRole("link", { name: "Ask Raffa how to approach the Salesforce renewal" })).toHaveAttribute(
          "href",
          `/ask?scope=${CONTRACT_ID}`,
        );
      });

      it("a pipeline stage or a supplier bar filters the table, and a second press clears it", async () => {
        renderPopulated(items());
        const pipeline = await screen.findByRole("region", { name: "From identified to verified" });
        const table = () => screen.getByRole("table", { name: "Opportunities" });
        expect(within(table()).getAllByRole("row")).toHaveLength(4);

        fireEvent.click(within(pipeline).getByRole("button", { name: /Show verified opportunities/ }));
        expect(within(table()).getAllByRole("row")).toHaveLength(2);
        expect(within(table()).getByText("Realized")).toBeInTheDocument();
        expect(screen.getByLabelText("Status")).toHaveValue("Realized");

        fireEvent.click(within(pipeline).getByRole("button", { name: /Show all opportunities/ }));
        expect(within(table()).getAllByRole("row")).toHaveLength(4);

        const where = screen.getByRole("region", { name: "Where the savings are" });
        fireEvent.click(within(where).getByRole("button", { name: "Fabrikam" }));
        expect(within(table()).getAllByRole("row")).toHaveLength(3);
        expect(screen.getByLabelText("Supplier")).toHaveValue("Fabrikam");
      });

      it("the next-step links open Renewals with the contract selected, and Ask bound to it", async () => {
        renderPopulated(items());
        const table = await screen.findByRole("table", { name: "Opportunities" });
        const notice = await within(table).findAllByText("8 d");
        expect(notice[0]).toHaveClass("deadline-critical");

        fireEvent.click(within(table).getByRole("link", { name: "Ask Raffa about the Salesforce saving" }));
        expect(await screen.findByText("ASK_SCREEN")).toBeInTheDocument();
        expect(screen.getByTestId("ask-search").textContent).toBe(`?scope=${CONTRACT_ID}`);
        expect(screen.getByTestId("ask-state").textContent).toBe(JSON.stringify({ query: "Where can we save with Salesforce?", newChat: true }));
      });

      it("shows the rows' contracts in Portfolio, with Savings named as the source", async () => {
        renderPopulated(items());
        const link = await screen.findByRole("link", { name: "Show these contracts in Portfolio →" });
        expect(link).toHaveAttribute("href", `/contracts?ids=${CONTRACT_ID},${TRACKED_CONTRACT_ID}&from=savings`);
      });

      it("?contract= focuses one contract's opportunities, and the focus can be dropped", async () => {
        renderSavings(
          mockApiClient({
            getSavingsKpis: vi.fn().mockResolvedValue(kpisOk(kpiSummary())),
            getSavingsOpportunities: vi.fn().mockResolvedValue(opportunitiesOk(items())),
            getPortfolio: vi.fn().mockResolvedValue(portfolioOk([{ contractId: CONTRACT_ID, supplierName: "Salesforce" }, { contractId: TRACKED_CONTRACT_ID, supplierName: "Fabrikam" }])),
          }),
          `/savings?contract=${TRACKED_CONTRACT_ID}`,
        );

        const notice = await screen.findByText("Showing the savings on one Fabrikam contract.");
        expect(within(notice.parentElement!).getByRole("link", { name: "Open the contract →" })).toHaveAttribute("href", `/contracts/${TRACKED_CONTRACT_ID}`);
        const table = screen.getByRole("table", { name: "Opportunities" });
        expect(within(table).getAllByRole("row")).toHaveLength(3);
        expect(within(table).queryByText("Salesforce")).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole("button", { name: "Show all opportunities" }));
        expect(within(screen.getByRole("table", { name: "Opportunities" })).getAllByRole("row")).toHaveLength(4);
        expect(screen.queryByText("Showing the savings on one Fabrikam contract.")).not.toBeInTheDocument();
      });
    });

    it("does not invent a tracked-action row alongside the real opportunities list", async () => {
      renderPopulated([opportunity()]);

      const table = await screen.findByRole("table", { name: "Opportunities" });
      const bodyRows = within(table).getAllByRole("row").slice(1);
      expect(bodyRows).toHaveLength(1);
      expect(bodyRows[0]).toHaveTextContent("Salesforce");
      expect(screen.getByText("1 opportunity · CHF 410,000–590,000 identified")).toBeInTheDocument();
      expect(within(table).queryByText("Not yet available")).not.toBeInTheDocument();
    });
  });
});
