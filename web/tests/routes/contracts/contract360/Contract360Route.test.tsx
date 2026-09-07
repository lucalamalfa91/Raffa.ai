import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import Contract360Route from "../../../../src/routes/contracts/contract360";
import type { ApiClient, Contract360Body, GetContract360Result, RenewalPipelineItemBody, RenewalPriorityBody } from "../../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const CONTRACT_ID = "22222222-2222-2222-2222-222222222222";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [], totalCount: 0 }, error: null }),
    getRenewalPriority: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, priority: null, error: "No contract found." }),
    // Task E07/F03/US01/T01 (field-review-correction): this suite never reaches the Review screen --
    // bare vi.fn() is enough, same convention as getPortfolio above.
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    // Task E08/F01/US01/T01 (renewal-pipeline): this suite never reaches the Renewals screen --
    // bare vi.fn() is enough, same convention as getCorrectionHistory above.
    postRenewalAction: vi.fn(),
    // Task E08/F03/US01/T01 (quote-check-ui): this suite never reaches the Quote Check screen --
    // bare vi.fn() is enough, same convention as getPortfolio above.
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
    // Task E08/F02/US01/T01 (savings-home): this suite never reaches Home's own fetch-outcome
    // matrix -- bare vi.fn() is enough, same convention as getContract360 above.
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    ...overrides,
  };
}

function contract(overrides: Partial<Contract360Body> = {}): Contract360Body {
  return {
    contractId: CONTRACT_ID,
    header: {
      contractId: CONTRACT_ID,
      supplierId: "33333333-3333-3333-3333-333333333333",
      type: "Msa",
      status: "active",
      annualSpend: 500_000,
      totalContractValue: 1_500_000,
      startDate: "2025-01-01",
      endDate: "2026-01-01",
      renewalDate: "2026-01-01",
      cancellationDeadline: "2025-11-17",
      autoRenewal: true,
      risk: "High",
    },
    tabs: {
      overview: {
        currency: "CHF",
        effectiveDate: "2025-01-01",
        renewalTermMonths: 12,
        paymentTerms: "Net 45",
        governingLaw: "Switzerland, Zürich",
        parentContractId: null,
        version: 1,
        createdAt: "2025-01-01T00:00:00Z",
      },
      commercials: {
        annualSpend: 500_000,
        totalContractValue: 1_500_000,
        currency: "CHF",
        paymentTerms: "Net 45",
        autoRenewal: true,
        renewalTermMonths: 12,
        lineItemCount: 1,
        lineItemAnnualCostTotal: 66_000,
        lineItemTotalCostTotal: 198_000,
      },
      products: [
        {
          lineItemId: "li-1",
          productId: null,
          sku: "SKU-1",
          description: "Premium DBU",
          quantity: 120_000,
          unit: "DBU/yr",
          unitPrice: 0.55,
          listPrice: 0.6,
          discount: 12,
          billingPeriod: "annual",
          annualCost: 66_000,
          totalCost: 198_000,
          sourceDocumentId: "doc-1",
          sourceSpan: "§6.2",
          sourcePage: 9,
          confidence: 0.97,
        },
      ],
      clauses: [
        {
          clauseId: "cl-1",
          clauseType: "Liability cap",
          rawText: "12 months fees",
          normalizedValue: "12 months fees",
          riskLevel: "Medium",
          sourceDocumentId: "doc-1",
          sourceSpan: "§17.2",
          sourcePage: 27,
          confidence: 0.78,
        },
      ],
      obligations: [
        {
          obligationId: "ob-1",
          party: "Customer",
          obligationType: "True-up",
          description: "Annual true-up of committed DBU",
          dueDate: "2026-01-15",
          recurrenceRule: "annual",
          criticality: "high",
          status: "pending",
          sourceDocumentId: "doc-1",
          sourceSpan: "§6.3",
          sourcePage: 10,
          confidence: 0.88,
        },
      ],
      risks: [
        {
          riskId: "risk-1",
          riskType: "Auto-renewal",
          severity: "High",
          description: "Auto-renews without an explicit re-negotiation checkpoint",
          status: "open",
          clauseId: null,
          sourceDocumentId: "doc-1",
          sourceSpan: "§8.4",
          sourcePage: 12,
          confidence: 0.97,
        },
      ],
      documents: [
        {
          documentId: "doc-1",
          fileName: "Acme_MSA.pdf",
          mimeType: "application/pdf",
          documentType: "Msa",
          processingStatus: "Completed",
          createdAt: "2025-01-01T00:00:00Z",
        },
      ],
      benchmark: [],
      renewal: { endDate: "2026-01-01", renewalDate: "2026-01-01", cancellationDeadline: "2025-11-17", autoRenewal: true, renewalTermMonths: 12 },
      activity: [],
    },
    ...overrides,
  };
}

function ok(body: Contract360Body): GetContract360Result {
  return { ok: true, statusCode: 200, contract: body, error: null };
}

function renewalPipelineItem(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalPipelineItemBody {
  return {
    contractId: CONTRACT_ID,
    supplierId: null,
    status: "Determined",
    renewalDate: "2026-01-01",
    daysUntilRenewal: 30,
    annualSpend: 500_000,
    cancellationDeadline: "2025-11-17",
    daysUntilCancellationDeadline: 14,
    autoRenewal: true,
    action: "Start renewal negotiation now",
    insightCard: {
      facts: {
        supplierId: null,
        renewalDate: "2026-01-01",
        daysUntilRenewal: 30,
        annualSpend: 500_000,
        cancellationDeadline: "2025-11-17",
        daysUntilCancellationDeadline: 14,
      },
      recommendations: {
        recommendedAction: "Start renewal negotiation now",
        explanation: "Renews in 30 days with a cancellation notice due in 14 days.",
        annualUpliftPercent: null,
        marketPosition: null,
        potentialSavingsRange: null,
      },
    },
    ...overrides,
  };
}

function priorityFixture(overrides: Partial<RenewalPriorityBody> = {}): RenewalPriorityBody {
  return {
    contractId: CONTRACT_ID,
    totalScore: 72,
    components: {
      spendWeight: { score: 20, explanation: "AnnualSpend (500000) is 500,000 or more: maximum spend weight (20)." },
      timeUrgency: { score: 20, explanation: "30 day(s) until renewal, within the 30-day window: maximum time urgency (20)." },
      benchmarkOpportunity: { score: 10, explanation: "R3 benchmark data is not available: neutral (10)." },
      priceIncreaseRisk: { score: 7, explanation: "AnnualUpliftPercent is unknown: minimum (0)." },
      contractRisk: { score: 15, explanation: "ContractRisk is High: contract risk (15)." },
    },
    ...overrides,
  };
}

function renderContract360(apiClient: ApiClient, contractId = CONTRACT_ID, state?: unknown) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: `/contracts/${contractId}`, state }]}>
      <Routes>
        <Route path="/contracts/:contractId" element={<Contract360Route apiClient={apiClient} />} />
        <Route path="/renewals" element={<div>RENEWALS_SCREEN</div>} />
        <Route path="/contracts/:contractId/review" element={<div>REVIEW_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("Contract360Route", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("contigo.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const getContract360 = vi.fn();

    renderContract360(mockApiClient({ getContract360 }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(getContract360).not.toHaveBeenCalled();
  });

  it("shows a loading skeleton while the request is in flight, then replaces it", async () => {
    let resolveFetch!: (value: GetContract360Result) => void;
    const pending = new Promise<GetContract360Result>((resolve) => {
      resolveFetch = resolve;
    });
    const { container } = renderContract360(mockApiClient({ getContract360: vi.fn().mockReturnValue(pending) }));

    expect(container.querySelector(".contract360-skeleton")).toBeInTheDocument();

    resolveFetch(ok(contract()));

    expect(await screen.findByRole("heading", { name: "MSA" })).toBeInTheDocument();
    expect(container.querySelector(".contract360-skeleton")).not.toBeInTheDocument();
  });

  it("calls getContract360 with the current workspace id and the route's contractId", () => {
    const getContract360 = vi.fn().mockResolvedValue(ok(contract()));
    renderContract360(mockApiClient({ getContract360 }));

    expect(getContract360).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID);
  });

  it("renders a named not-found state on a 404, not a generic error (AC-4-equivalent)", async () => {
    renderContract360(mockApiClient({ getContract360: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, contract: null, error: null }) }));

    expect(await screen.findByText(/contract not found/i)).toBeInTheDocument();
  });

  it("renders a plain-language error with a Retry that re-fetches on a 503", async () => {
    const getContract360 = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: 503, contract: null, error: "Service Unavailable" })
      .mockResolvedValueOnce(ok(contract()));
    renderContract360(mockApiClient({ getContract360 }));

    expect(await screen.findByText(/temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.getByRole("alert")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByRole("heading", { name: "MSA" })).toBeInTheDocument();
    expect(getContract360).toHaveBeenCalledTimes(2);
  });

  describe("once populated", () => {
    function renderPopulated(overrides: Partial<ApiClient> = {}) {
      return renderContract360(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(contract())),
          getRenewals: vi
            .fn()
            .mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [renewalPipelineItem()], totalCount: 1 }, error: null }),
          getRenewalPriority: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, priority: priorityFixture(), error: null }),
          ...overrides,
        }),
      );
    }

    it("AC-1: header shows the type/status tags and the linked-document count", async () => {
      renderPopulated();

      expect(await screen.findByRole("heading", { name: "MSA" })).toBeInTheDocument();
      expect(screen.getByText("1 document")).toBeInTheDocument();
    });

    it("AC-2: fact row shows annual spend, TCV, and the priority score", async () => {
      renderPopulated();
      await screen.findByRole("heading", { name: "MSA" });

      expect(screen.getByText("500,000")).toBeInTheDocument();
      expect(screen.getByText("1,500,000")).toBeInTheDocument();
      expect(screen.getByText("priority 72/100")).toBeInTheDocument();
    });

    it("AC-3: Overview renders the recommendation card and the fact-based sections as structurally separate blocks (ADR-019 facts vs AI)", async () => {
      const { container } = renderPopulated();
      await screen.findByRole("heading", { name: "MSA" });

      const recommendationCard = container.querySelector(".ai-recommendation");
      expect(recommendationCard).not.toBeNull();
      expect(within(recommendationCard as HTMLElement).getByText("Start renewal negotiation now")).toBeInTheDocument();

      // The AI-labelled block never contains a fact table...
      expect(recommendationCard?.querySelector("table")).toBeNull();
      // ...and no fact table anywhere on the page repeats the recommendation's own text.
      container.querySelectorAll("table").forEach((table) => {
        expect(table.textContent).not.toContain("Start renewal negotiation now");
      });

      expect(screen.getByText("Needs your attention")).toBeInTheDocument();
      expect(screen.getByText("Top risks")).toBeInTheDocument();
    });

    it("AC-4: switching tabs renders a different tab's deterministic facts, in the exact ADR-020 order", async () => {
      renderPopulated();
      await screen.findByRole("heading", { name: "MSA" });

      const tabs = screen.getByRole("navigation", { name: /contract 360 sections/i });
      expect(within(tabs).getAllByRole("button").map((button) => button.textContent)).toEqual([
        "Overview",
        "Commercials",
        "Products",
        "Clauses",
        "Obligations",
        "Risks",
        "Documents",
        "Benchmark",
        "Renewal",
        "Activity",
      ]);

      fireEvent.click(within(tabs).getByRole("button", { name: "Products" }));
      expect(await screen.findByText("Premium DBU")).toBeInTheDocument();

      fireEvent.click(within(tabs).getByRole("button", { name: "Benchmark" }));
      expect(await screen.findByText(/benchmark service ships in r3/i)).toBeInTheDocument();
    });

    it("task E07/F04/US01/T01 (ask-contigo-ui, AC-2): opens directly on the tab named in router state, e.g. from an Ask Contigo citation", async () => {
      renderContract360(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(contract())),
          getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [], totalCount: 0 }, error: null }),
          getRenewalPriority: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, priority: null, error: "No contract found." }),
        }),
        CONTRACT_ID,
        { tab: "Clauses" },
      );
      await screen.findByRole("heading", { name: "MSA" });

      const tabs = screen.getByRole("navigation", { name: /contract 360 sections/i });
      expect(within(tabs).getByRole("button", { name: "Clauses" })).toHaveAttribute("aria-pressed", "true");
      expect(screen.getByText("Liability cap")).toBeInTheDocument();
    });

    it("falls back to Overview for an unrecognised router-state tab instead of trusting an arbitrary string", async () => {
      renderContract360(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(contract())) }), CONTRACT_ID, {
        tab: "NotARealTab",
      });
      await screen.findByRole("heading", { name: "MSA" });

      const tabs = screen.getByRole("navigation", { name: /contract 360 sections/i });
      expect(within(tabs).getByRole("button", { name: "Overview" })).toHaveAttribute("aria-pressed", "true");
    });

    it("'Why this score' switches to the Renewal tab in place (no navigation) and shows the real priority-score components", async () => {
      renderPopulated();
      await screen.findByRole("heading", { name: "MSA" });

      fireEvent.click(screen.getByRole("button", { name: /why this score/i }));

      expect(await screen.findByText("Spend weight")).toBeInTheDocument();
      expect(screen.getByText(/500,000 or more/)).toBeInTheDocument();
    });

    it("'Open in renewals' is a real link to /renewals", async () => {
      renderPopulated();
      await screen.findByRole("heading", { name: "MSA" });

      expect(screen.getByRole("link", { name: /open in renewals/i })).toHaveAttribute("href", "/renewals");
    });

    it("names an honest gap instead of a recommendation when this contract has no renewal-pipeline entry", async () => {
      renderPopulated({
        getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [], totalCount: 0 }, error: null }),
      });
      await screen.findByRole("heading", { name: "MSA" });

      expect(screen.getByText(/no renewal recommendation for this contract/i)).toBeInTheDocument();
    });
  });
});
