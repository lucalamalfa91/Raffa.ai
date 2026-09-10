import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import Contract360Route from "../../../../src/routes/contracts/contract360";
import { loadNegotiationSteps } from "../../../../src/routes/contracts/contract360/negotiationStepsStore";
import { loadTrackedRenewalActions, rememberRenewalAction } from "../../../../src/routes/renewals/renewalActionStore";
import { formatDateOnly } from "../../../../src/routes/contracts/portfolioTableFormatters";
import type { ApiClient, Contract360Body, GetContract360Result, RenewalPipelineItemBody, RenewalPriorityBody } from "../../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const CONTRACT_ID = "22222222-2222-2222-2222-222222222222";
const SUPPLIER_ID = "33333333-3333-3333-3333-333333333333";
const USER_LABEL = "user@example.test";

function isoDaysFromNow(days: number): string {
  const date = new Date();
  date.setUTCHours(0, 0, 0, 0);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

const CANCEL_DEADLINE = isoDaysFromNow(14);
const TERM_END = isoDaysFromNow(60);

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
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [], totalCount: 0 }, error: null }),
    getRenewalPriority: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, priority: null, error: "No contract found." }),
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
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
    ...overrides,
  };
}

function contract(overrides: Partial<Contract360Body> = {}): Contract360Body {
  return {
    contractId: CONTRACT_ID,
    header: {
      contractId: CONTRACT_ID,
      supplierId: SUPPLIER_ID,
      supplierName: null,
      type: "Msa",
      status: "active",
      annualSpend: 500_000,
      totalContractValue: 1_500_000,
      startDate: "2025-01-01",
      endDate: TERM_END,
      renewalDate: TERM_END,
      cancellationDeadline: CANCEL_DEADLINE,
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
          rawText: "Each party's aggregate liability is capped at 12 months fees, save for confidentiality and IP.",
          normalizedValue: "12 months fees",
          riskLevel: "Medium",
          sourceDocumentId: "doc-1",
          sourceSpan: "§17.2",
          sourcePage: 27,
          confidence: 0.78,
        },
        {
          clauseId: "cl-2",
          clauseType: "Auto-renewal",
          rawText: "Renews for successive 12-month terms unless notice is given 90 days before term end.",
          normalizedValue: "Renews for successive 12-month terms unless notice ≥90 days before term end.",
          riskLevel: "High",
          sourceDocumentId: "doc-1",
          sourceSpan: "§8.4",
          sourcePage: 12,
          confidence: 0.97,
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
      renewal: { endDate: TERM_END, renewalDate: TERM_END, cancellationDeadline: CANCEL_DEADLINE, autoRenewal: true, renewalTermMonths: 12 },
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
    supplierId: SUPPLIER_ID,
    supplierName: null,
    status: "Determined",
    renewalDate: TERM_END,
    daysUntilRenewal: 60,
    annualSpend: 500_000,
    cancellationDeadline: CANCEL_DEADLINE,
    daysUntilCancellationDeadline: 14,
    autoRenewal: true,
    action: "Start renewal negotiation now",
    insightCard: {
      facts: {
        supplierId: SUPPLIER_ID,
        supplierName: null,
        renewalDate: TERM_END,
        daysUntilRenewal: 60,
        annualSpend: 500_000,
        cancellationDeadline: CANCEL_DEADLINE,
        daysUntilCancellationDeadline: 14,
      },
      recommendations: {
        recommendedAction: "Start renewal negotiation now",
        explanation: "Renews in 60 days with a cancellation notice due in 14 days.",
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

function renderContract360(apiClient: ApiClient, contractId = CONTRACT_ID, state?: unknown, search = "") {
  return render(
    <MemoryRouter initialEntries={[{ pathname: `/contracts/${contractId}`, search, state }]}>
      <Routes>
        <Route path="/contracts/:contractId" element={<Contract360Route apiClient={apiClient} userLabel={USER_LABEL} />} />
        <Route path="/renewals" element={<div>RENEWALS_SCREEN</div>} />
        <Route path="/contracts/:contractId/review" element={<div>REVIEW_SCREEN</div>} />
        <Route path="/ask" element={<div>ASK_SCREEN</div>} />
        <Route path="/savings" element={<div>SAVINGS_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

function populatedClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return mockApiClient({
    getContract360: vi.fn().mockResolvedValue(ok(contract())),
    getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [renewalPipelineItem()], totalCount: 1 }, error: null }),
    getRenewalPriority: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, priority: priorityFixture(), error: null }),
    ...overrides,
  });
}

describe("Contract360Route (V2 no tabs, ADR-024 / screens-v2.md #5)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
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

    expect(await screen.findByRole("heading", { level: 2, name: "MSA" })).toBeInTheDocument();
    expect(container.querySelector(".contract360-skeleton")).not.toBeInTheDocument();
  });

  it("calls getContract360 with the current workspace id and the route's contractId", () => {
    const getContract360 = vi.fn().mockResolvedValue(ok(contract()));
    renderContract360(mockApiClient({ getContract360 }));

    expect(getContract360).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID);
  });

  it("renders a named not-found state on a 404, not a generic error", async () => {
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

    expect(await screen.findByRole("heading", { level: 2, name: "MSA" })).toBeInTheDocument();
    expect(getContract360).toHaveBeenCalledTimes(2);
  });

  describe("header", () => {
    it("shows the supplier kicker, the title, the one-line meta, the two actions, a plain '← Back' and no tab strip", async () => {
      renderContract360(populatedClient());
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.getByText("Supplier 33333333")).toBeInTheDocument();
      expect(screen.getByText(/^MSA · CHF 500,000 \/ year · 1 document · /)).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Ask about it" })).toHaveAttribute("href", `/ask?scope=${CONTRACT_ID}`);
      expect(screen.getByRole("link", { name: "Review extraction" })).toHaveAttribute("href", `/contracts/${CONTRACT_ID}/review`);
      expect(screen.getByRole("button", { name: "← Back" })).toBeInTheDocument();
      expect(screen.queryByRole("navigation", { name: /contract 360 sections/i })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Commercials" })).not.toBeInTheDocument();
    });

    it("follows the origin for the back label: Ask Raffa, Savings", async () => {
      const { unmount } = renderContract360(populatedClient(), CONTRACT_ID, { from: "ask" });
      await screen.findByRole("heading", { level: 2, name: "MSA" });
      expect(screen.getByRole("link", { name: "← Ask Raffa" })).toHaveAttribute("href", "/ask");
      unmount();

      renderContract360(populatedClient(), CONTRACT_ID, { from: "savings" });
      await screen.findByRole("heading", { level: 2, name: "MSA" });
      expect(screen.getByRole("link", { name: "← Savings" })).toHaveAttribute("href", "/savings");
    });

    it("shows a wire-provided supplierName instead of the id-fragment fallback", async () => {
      renderContract360(
        populatedClient({ getContract360: vi.fn().mockResolvedValue(ok(contract({ header: { ...contract().header, supplierName: "Salesforce" } }))) }),
      );
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.getByText("Salesforce")).toBeInTheDocument();
      expect(screen.queryByText("Supplier 33333333")).not.toBeInTheDocument();
    });
  });

  describe("answers band", () => {
    it("renders Where you can save · When you must move · What to do from real fields, with honest gaps", async () => {
      renderContract360(populatedClient());
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      const band = screen.getByRole("region", { name: "Answers" });
      const cells = band.querySelectorAll(".contract360-answer");
      expect(cells).toHaveLength(3);

      expect(cells[0]).toHaveTextContent("Where you can save");
      expect(cells[0]).toHaveTextContent("Not yet available");
      expect(cells[0]).toHaveTextContent(/Benchmark Service/);

      expect(cells[1]).toHaveTextContent("When you must move");
      expect(within(cells[1] as HTMLElement).getByText(formatDateOnly(CANCEL_DEADLINE))).toHaveClass("deadline-critical");
      expect(cells[1]).toHaveTextContent("in 14 days");
      expect(cells[1]).toHaveTextContent("last day to give notice");
      expect(cells[1]).toHaveTextContent(`Term ends ${formatDateOnly(TERM_END)} and auto-renews for 12 months.`);

      expect(cells[2]).toHaveTextContent("What to do");
      expect(within(cells[2] as HTMLElement).getByText("Start renewal negotiation now")).toBeInTheDocument();
      expect(cells[2]).toHaveTextContent("Renews in 60 days with a cancellation notice due in 14 days.");
      expect(within(cells[2] as HTMLElement).getByRole("button", { name: "Start negotiation" })).toHaveClass("btn-primary");
      expect(within(cells[2] as HTMLElement).getByRole("button", { name: "Assign to me" })).toBeInTheDocument();

      // The recommendation never leaks into a fact table (ADR-019 facts vs AI).
      expect(band.querySelector("table")).toBeNull();
    });

    it("names an honest gap instead of a recommendation when this contract has no renewal-pipeline entry", async () => {
      renderContract360(
        populatedClient({ getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [], totalCount: 0 }, error: null }) }),
      );
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.getByText("No renewal recommendation for this contract")).toBeInTheDocument();
      expect(screen.getByText(/did not appear in the current renewal pipeline yet/i)).toBeInTheDocument();
    });

    it("'Start negotiation' posts the real action as the signed-in owner, then shows the tracker; steps tick and persist; 'Undo' re-posts Open", async () => {
      const postRenewalAction = vi
        .fn()
        .mockResolvedValueOnce({
          ok: true,
          statusCode: 200,
          action: { contractId: CONTRACT_ID, owner: USER_LABEL, status: "InProgress", action: "In negotiation", updatedAt: "2026-09-06T09:00:00Z" },
          error: null,
        })
        .mockResolvedValueOnce({
          ok: true,
          statusCode: 200,
          action: { contractId: CONTRACT_ID, owner: USER_LABEL, status: "NotStarted", action: "Open", updatedAt: "2026-09-06T09:05:00Z" },
          error: null,
        });
      renderContract360(populatedClient({ postRenewalAction }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      fireEvent.click(screen.getByRole("button", { name: "Start negotiation" }));

      expect(postRenewalAction).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, { owner: USER_LABEL, status: "InProgress", action: "In negotiation" });

      const tracker = await screen.findByRole("status");
      expect(tracker).toHaveTextContent("In negotiation");
      expect(tracker).toHaveTextContent(`owner ${USER_LABEL}`);
      expect(tracker).toHaveTextContent(`target Not yet available · close by ${formatDateOnly(CANCEL_DEADLINE)}`);
      expect(screen.queryByRole("button", { name: "Start negotiation" })).not.toBeInTheDocument();

      const steps = within(tracker).getAllByRole("button", { pressed: false });
      expect(steps.map((step) => step.textContent)).toEqual([
        "Notify Supplier 33333333 of intent to renegotiatethis week",
        "Request revised pricing and licence mix+10 days",
        "Counter with the market benchmark+20 days",
        `Sign, or send non-renewal noticeby ${formatDateOnly(CANCEL_DEADLINE)}`,
      ]);

      fireEvent.click(steps[0]);
      expect(steps[0]).toHaveAttribute("aria-pressed", "true");
      expect(loadNegotiationSteps(CONTRACT_ID)).toEqual([true, false, false, false]);

      expect(within(tracker).getByRole("link", { name: "Track it in Renewals →" })).toHaveAttribute("href", "/renewals");
      expect(loadTrackedRenewalActions()).toHaveLength(1);
      expect(loadTrackedRenewalActions()[0]).toMatchObject({ contractId: CONTRACT_ID, action: "In negotiation", supplierId: SUPPLIER_ID, annualSpend: 500_000 });

      fireEvent.click(within(tracker).getByRole("button", { name: "Undo" }));

      await waitFor(() => expect(postRenewalAction).toHaveBeenNthCalledWith(2, WORKSPACE_ID, CONTRACT_ID, { owner: USER_LABEL, status: "NotStarted", action: "Open" }));
      expect(await screen.findByRole("button", { name: "Start negotiation" })).toBeInTheDocument();
      expect(loadTrackedRenewalActions()).toEqual([]);
      expect(loadNegotiationSteps(CONTRACT_ID)).toEqual([false, false, false, false]);
    });

    it("'Assign to me' posts NotStarted / Assigned", async () => {
      const postRenewalAction = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        action: { contractId: CONTRACT_ID, owner: USER_LABEL, status: "NotStarted", action: "Assigned", updatedAt: "2026-09-06T09:00:00Z" },
        error: null,
      });
      renderContract360(populatedClient({ postRenewalAction }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      fireEvent.click(screen.getByRole("button", { name: "Assign to me" }));

      expect(postRenewalAction).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, { owner: USER_LABEL, status: "NotStarted", action: "Assigned" });
      expect(await screen.findByRole("status")).toHaveTextContent("Assigned");
    });

    it("shows the tracker straight away when this session already acted on the contract (shared with Renewals)", async () => {
      rememberRenewalAction({
        contractId: CONTRACT_ID,
        supplierId: SUPPLIER_ID,
        annualSpend: 500_000,
        owner: USER_LABEL,
        status: "InProgress",
        action: "In negotiation",
        updatedAt: "2026-09-06T09:00:00Z",
      });
      renderContract360(populatedClient());
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.getByRole("status")).toHaveTextContent("In negotiation");
      expect(screen.queryByRole("button", { name: "Start negotiation" })).not.toBeInTheDocument();
    });

    it("shows an inline error and keeps the actions when the write fails", async () => {
      const postRenewalAction = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, action: null, error: "'owner' is required." });
      renderContract360(populatedClient({ postRenewalAction }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      fireEvent.click(screen.getByRole("button", { name: "Start negotiation" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("'owner' is required.");
      expect(screen.getByRole("button", { name: "Start negotiation" })).toBeEnabled();
      expect(loadTrackedRenewalActions()).toEqual([]);
    });
  });

  describe("why — the clauses behind it", () => {
    it("lists the clauses (type · normalised · page § · risk · confidence) and opens the original wording on click", async () => {
      renderContract360(populatedClient());
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      const why = screen.getByRole("region", { name: "Why — the clauses behind it" });
      const rows = within(why).getAllByRole("listitem");
      expect(rows).toHaveLength(2);
      expect(rows[0]).toHaveTextContent("Liability cap");
      expect(rows[0]).toHaveTextContent("12 months fees");
      expect(rows[0]).toHaveTextContent("p.27 · §17.2");
      expect(within(rows[0]).getByText("Medium")).toHaveClass("tag-neutral");
      expect(within(rows[1]).getByText("High")).toHaveClass("tag-accent");
      expect(screen.queryByTestId("clause-highlight")).toBeNull();

      fireEvent.click(rows[0]);

      const evidence = screen.getByTestId("clause-highlight");
      expect(within(evidence).getByText("Acme_MSA.pdf · page 27 · §17.2")).toBeInTheDocument();
      expect(within(evidence).getByText("12 months fees").tagName).toBe("MARK");
      expect(evidence).toHaveTextContent("Each party's aggregate liability is capped at 12 months fees, save for confidentiality and IP.");
      expect(rows[0]).toHaveAttribute("aria-pressed", "true");
    });

    it("R-EVD-02 citation landing: ?clause=<id> highlights the cited wording without a click", async () => {
      renderContract360(populatedClient(), CONTRACT_ID, undefined, "?clause=cl-1");
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(within(screen.getByTestId("clause-highlight")).getByText("12 months fees")).toBeInTheDocument();
    });

    it("?page=<n> highlights the first clause on that page when no clause id is given", async () => {
      renderContract360(populatedClient(), CONTRACT_ID, undefined, "?page=12");
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(within(screen.getByTestId("clause-highlight")).getByText("Acme_MSA.pdf · page 12 · §8.4")).toBeInTheDocument();
    });

    it("an unmatched clause param highlights nothing rather than fabricating a match", async () => {
      renderContract360(populatedClient(), CONTRACT_ID, undefined, "?clause=does-not-exist");
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.queryByTestId("clause-highlight")).toBeNull();
    });

    it("says so, with a way to the review, when no clauses were extracted", async () => {
      renderContract360(populatedClient({ getContract360: vi.fn().mockResolvedValue(ok(contract({ tabs: { ...contract().tabs, clauses: [] } }))) }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.getByText(/no clauses extracted for this contract yet/i)).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Review the extraction" })).toHaveAttribute("href", `/contracts/${CONTRACT_ID}/review`);
    });
  });

  describe("details ▾", () => {
    it("is closed by default and opens to key terms, documents, facts to decide, the priority score and the extracted lists", async () => {
      renderContract360(populatedClient());
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      const toggle = screen.getByRole("button", { name: "All terms, documents and open facts ▾" });
      expect(toggle).toHaveAttribute("aria-expanded", "false");
      expect(screen.queryByText("Key terms")).not.toBeInTheDocument();

      fireEvent.click(toggle);

      expect(screen.getByRole("button", { name: "Hide details" })).toHaveAttribute("aria-expanded", "true");
      const details = screen.getByRole("region", { name: "Details" });
      expect(within(details).getByText("Key terms")).toBeInTheDocument();
      expect(within(details).getByText("Annual spend").closest(".contract360-detail-row")).toHaveTextContent("CHF 500,000");
      expect(within(details).getByText("Renewal term").closest(".contract360-detail-row")).toHaveTextContent("12 months");
      expect(within(details).getByText("Documents")).toBeInTheDocument();
      expect(within(details).getByText("Acme_MSA.pdf")).toBeInTheDocument();
      expect(within(details).getByText("Facts you still need to decide")).toBeInTheDocument();
      expect(within(details).getByText("Liability cap").closest(".contract360-detail-row")).toHaveTextContent("12 months fees");
      expect(within(details).getByRole("link", { name: "Review all →" })).toHaveAttribute("href", `/contracts/${CONTRACT_ID}/review`);
      expect(within(details).getByText("priority 72/100")).toBeInTheDocument();
      expect(within(details).getByText("Spend weight")).toBeInTheDocument();
      expect(within(details).getByText("Products")).toBeInTheDocument();
      expect(within(details).getByText("Premium DBU")).toBeInTheDocument();
      expect(within(details).getByText("Obligations")).toBeInTheDocument();
      expect(within(details).getByText("Risks")).toBeInTheDocument();

      // Every table in the drawer is deterministic facts; the recommendation text is never in one.
      details.querySelectorAll("table").forEach((table) => {
        expect(table.textContent).not.toContain("Start renewal negotiation now");
      });
    });

    it("says 'None — …' when every fact is above the threshold, and names the missing priority score honestly", async () => {
      const allConfident = contract();
      allConfident.tabs.clauses = allConfident.tabs.clauses.map((c) => ({ ...c, confidence: 0.99 }));
      allConfident.tabs.obligations = allConfident.tabs.obligations.map((o) => ({ ...o, confidence: 0.99 }));
      renderContract360(populatedClient({ getContract360: vi.fn().mockResolvedValue(ok(allConfident)), getRenewalPriority: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, priority: null, error: "x" }) }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      fireEvent.click(screen.getByRole("button", { name: "All terms, documents and open facts ▾" }));

      expect(screen.getByText("None — every fact is above 95% or signed off by you.")).toBeInTheDocument();
      expect(screen.getByText("priority not yet available")).toBeInTheDocument();
      expect(screen.getByText(/has not been computed for this contract yet/i)).toBeInTheDocument();
    });
  });
});
