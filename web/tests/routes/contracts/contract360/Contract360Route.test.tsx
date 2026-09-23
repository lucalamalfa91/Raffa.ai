import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import Contract360Route from "../../../../src/routes/contracts/contract360";
import { formatDateOnly } from "../../../../src/routes/contracts/portfolioTableFormatters";
import type { ApiClient, Contract360Body, ContractFieldEvidenceBody, ContractStrategyBody, GetContract360Result, RenewalPipelineItemBody, RenewalPriorityBody } from "../../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const CONTRACT_ID = "22222222-2222-2222-2222-222222222222";
const SUPPLIER_ID = "33333333-3333-3333-3333-333333333333";
const USER_LABEL = "user@example.test";
/** The recommended action is the primary button's own label (`{{ cur.action }}` in the mock). */
const PRIMARY_ACTION_LABEL = "Start renewal negotiation now";

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
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [], totalCount: 0 }, error: null }),
    getRenewalPriority: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, priority: null, error: "No contract found." }),
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
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, evidence: [], autoAcceptThreshold: 0.9, error: null }),
    getContractStrategy: vi.fn().mockResolvedValue({ ok: false, statusCode: 503, strategy: null, error: "unavailable" }),
    validateDocument: vi.fn(),
    postRenewalAction: vi.fn(),
    getQuote: vi.fn(),
    getNegotiationSteps: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, steps: [], error: null }),
    putNegotiationSteps: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, steps: [], error: null }),
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
    // Task E16/F02/US03/T01 (ADR-027 §D9): a fully extracted fixture is a `ready` one.
    readiness: { state: "ready", stage: null, documentCount: 1, completedDocumentCount: 1 },
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
          market: {
            matched: true,
            recordId: "MKT-DBU-CH",
            product: "Premium DBU",
            geography: "CH",
            currency: "CHF",
            termMonths: 12,
            unitPriceP25: 0.4,
            unitPriceP50: 0.5,
            unitPriceP75: 0.6,
            sampleSize: 40,
            provenance: "representative market data · mock feed · updated 2026-07-01",
            marketUpdatedAt: "2026-07-01T00:00:00Z",
            checkedAt: "2026-09-22T08:00:00Z",
            matchKind: "Exact",
          },
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
    contractStatus: "active",
    documentProcessingStatus: "Completed",
    renewalDate: TERM_END,
    daysUntilRenewal: 60,
    annualSpend: 500_000,
    cancellationDeadline: CANCEL_DEADLINE,
    daysUntilCancellationDeadline: 14,
    autoRenewal: true,
    action: "Start renewal negotiation now",
    priority: null,
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

function strategyPack(overrides: Partial<ContractStrategyBody> = {}): ContractStrategyBody {
  return {
    contractId: CONTRACT_ID,
    whenYouMustMove: {
      renewalDate: TERM_END,
      cancellationDeadline: CANCEL_DEADLINE,
      daysLeft: 14,
      passedDeadline: false,
      explanation: "14 day(s) until the cancellation deadline.",
    },
    whereYouCanPush: [
      { leverType: "Volume", rationale: "This line orders 120,000 — cite the order size.", citationKeys: [] },
    ],
    targets: [
      {
        description: "Premium DBU",
        openingTarget: 0.3,
        acceptableRangeLow: 0.4,
        acceptableRangeHigh: 0.5,
        walkAwayThreshold: 0.55,
        explanation: "Recommended target range [0.4, 0.5]. representative (source: A; n=214; as of 2026-01-01)",
      },
    ],
    nextSteps: [{ label: "Notify the supplier of intent to renegotiate", dueHint: "this week" }],
    openWeakFacts: [],
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

function fieldEvidence(overrides: Partial<ContractFieldEvidenceBody> = {}): ContractFieldEvidenceBody {
  return {
    fieldName: "annualSpend",
    value: "500000",
    confidence: 0.96,
    decision: "auto_accepted",
    sourcePage: 2,
    sourceSpan: "CHF 500,000 per year",
    sourceDocumentId: "doc-1",
    sourceFileName: "Acme_MSA.pdf",
    passage: null,
    highlightStart: null,
    highlightLength: null,
    box: null,
    modelId: "fixture-extract-model",
    extractedAt: "2026-09-09T10:00:00Z",
    ...overrides,
  };
}

function acceptedEvidence(): ContractFieldEvidenceBody[] {
  return [
    fieldEvidence({ fieldName: "annualSpend" }),
    fieldEvidence({ fieldName: "totalContractValue", value: "1500000" }),
    fieldEvidence({ fieldName: "startDate", value: "2025-01-01" }),
    fieldEvidence({ fieldName: "endDate", value: TERM_END }),
    fieldEvidence({ fieldName: "cancellationDeadline", value: CANCEL_DEADLINE }),
    fieldEvidence({ fieldName: "autoRenewal", value: "true" }),
    fieldEvidence({ fieldName: "renewalTermMonths", value: "12" }),
    fieldEvidence({ fieldName: "paymentTerms", value: "Net 45" }),
    fieldEvidence({ fieldName: "governingLaw", value: "Switzerland, Zürich" }),
    fieldEvidence({ fieldName: "effectiveDate", value: "2025-01-01" }),
  ];
}

function evidenceOk(fields: ContractFieldEvidenceBody[], autoAcceptThreshold = 0.9) {
  return { ok: true as const, statusCode: 200, evidence: fields, autoAcceptThreshold, error: null };
}

function populatedClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return mockApiClient({
    getContract360: vi.fn().mockResolvedValue(ok(contract())),
    getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [renewalPipelineItem()], totalCount: 1 }, error: null }),
    getRenewalPriority: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, priority: priorityFixture(), error: null }),
    getContractEvidence: vi.fn().mockResolvedValue(evidenceOk(acceptedEvidence())),
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

  // Task E16/F03/US01/T01 (ADR-020 w15 §2.3, ADR-027 §D9): the fifth state is driven by the
  // server's `readiness`, never inferred from an empty clause array -- a contract whose tabs are
  // empty *because* extraction has not run must not render as an empty aggregate.
  describe("readiness (the fifth state)", () => {
    const emptyTabs = (): Contract360Body["tabs"] => ({
      ...contract().tabs,
      products: [],
      clauses: [],
      obligations: [],
      risks: [],
      documents: [{ documentId: "doc-1", fileName: "Acme_MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Processing", createdAt: "2025-01-01T00:00:00Z" }],
    });

    it("renders 'still being prepared' with 'Go to Documents' while readiness is processing, instead of an empty contract", async () => {
      const body = contract({
        readiness: { state: "processing", stage: "Extracting facts", documentCount: 1, completedDocumentCount: 0 },
        tabs: emptyTabs(),
      });
      renderContract360(populatedClient({ getContract360: vi.fn().mockResolvedValue(ok(body)) }));

      expect(await screen.findByRole("heading", { name: "This contract is still being prepared." })).toBeInTheDocument();
      expect(screen.getByText("Raffa.ai is still extracting the facts. It will open here once they pass validation.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Go to Documents" })).toHaveAttribute("href", "/documents");
      expect(screen.queryByRole("heading", { level: 2, name: "MSA" })).toBeNull();
      expect(screen.queryByText(/Where you can save/)).toBeNull();
    });

    it("renders 'no validated facts yet' when every linked document ended without a validated one", async () => {
      const body = contract({
        readiness: { state: "unavailable", stage: null, documentCount: 1, completedDocumentCount: 0 },
        tabs: emptyTabs(),
      });
      const client = populatedClient({ getContract360: vi.fn().mockResolvedValue(ok(body)) });
      renderContract360(client);

      expect(await screen.findByRole("heading", { name: "This contract has no validated facts yet." })).toBeInTheDocument();
      expect(screen.getByText("Raffa.ai could not finish processing its documents. Open Documents to see what happened to each one.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Go to Documents" })).toBeInTheDocument();
      expect(screen.queryByText(/still being prepared/)).toBeNull();
      // Nothing beyond `readiness` is on screen, so nothing beyond the contract is read.
      expect(client.getRenewals).not.toHaveBeenCalled();
      expect(client.getContractStrategy).not.toHaveBeenCalled();
    });

    it("re-reads a processing contract every 2 s and opens the page the moment readiness flips to ready", async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      try {
        const getContract360 = vi
          .fn()
          .mockResolvedValueOnce(ok(contract({ readiness: { state: "processing", stage: "Classifying", documentCount: 1, completedDocumentCount: 0 }, tabs: emptyTabs() })))
          .mockResolvedValue(ok(contract()));
        renderContract360(populatedClient({ getContract360 }));

        await screen.findByRole("heading", { name: "This contract is still being prepared." });
        await act(async () => {
          await vi.advanceTimersByTimeAsync(2_000);
        });

        expect(await screen.findByRole("heading", { level: 2, name: "MSA" })).toBeInTheDocument();
        expect(getContract360).toHaveBeenCalledTimes(2);
        const callsWhenReady = getContract360.mock.calls.length;
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4_000);
        });
        expect(getContract360).toHaveBeenCalledTimes(callsWhenReady); // the poll stops once ready
      } finally {
        vi.useRealTimers();
      }
    });

    // A demo tab parked on a "still being prepared" contract used to fire the five answer/proof
    // reads (each behind its own CORS preflight on the cross-origin API host) on every 2 s re-read
    // for the whole five-minute budget, and throw every one of them away.
    it("reads the answers and proof once, on the re-read that finds the contract ready -- never while it is still being prepared", async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      try {
        const processing = () =>
          ok(contract({ readiness: { state: "processing", stage: "Classifying", documentCount: 1, completedDocumentCount: 0 }, tabs: emptyTabs() }));
        const getContract360 = vi
          .fn()
          .mockResolvedValueOnce(processing())
          .mockResolvedValueOnce(processing())
          .mockResolvedValueOnce(processing())
          .mockResolvedValue(ok(contract()));
        const client = populatedClient({ getContract360 });
        const answerReads = [client.getRenewals, client.getRenewalPriority, client.getNegotiationSteps, client.getContractStrategy, client.getContractEvidence];
        renderContract360(client);

        await screen.findByRole("heading", { name: "This contract is still being prepared." });
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4_000);
        });
        expect(getContract360).toHaveBeenCalledTimes(3);
        for (const read of answerReads) expect(read).not.toHaveBeenCalled();

        await act(async () => {
          await vi.advanceTimersByTimeAsync(2_000);
        });
        expect(await screen.findByRole("heading", { level: 2, name: "MSA" })).toBeInTheDocument();
        for (const read of answerReads) expect(read).toHaveBeenCalledTimes(1);
      } finally {
        vi.useRealTimers();
      }
    });

    it("skips a 2 s re-read while the previous one is still in flight, instead of stacking requests on a slow API", async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      try {
        const processing = () =>
          ok(contract({ readiness: { state: "processing", stage: "Classifying", documentCount: 1, completedDocumentCount: 0 }, tabs: emptyTabs() }));
        let releaseSecondRead: (value: GetContract360Result) => void = () => {};
        const getContract360 = vi
          .fn()
          .mockResolvedValueOnce(processing())
          .mockImplementationOnce(
            () =>
              new Promise<GetContract360Result>((resolve) => {
                releaseSecondRead = resolve;
              }),
          )
          .mockResolvedValue(processing());
        renderContract360(populatedClient({ getContract360 }));

        await screen.findByRole("heading", { name: "This contract is still being prepared." });
        await act(async () => {
          await vi.advanceTimersByTimeAsync(2_000);
        });
        expect(getContract360).toHaveBeenCalledTimes(2); // the second read is now in flight
        await act(async () => {
          await vi.advanceTimersByTimeAsync(6_000);
        });
        expect(getContract360).toHaveBeenCalledTimes(2); // three ticks passed; none stacked a third read

        await act(async () => {
          releaseSecondRead(processing());
        });
        await act(async () => {
          await vi.advanceTimersByTimeAsync(2_000);
        });
        expect(getContract360).toHaveBeenCalledTimes(3); // the cadence resumes once it answered
      } finally {
        vi.useRealTimers();
      }
    });

    it("renders a family document with its own status tag under Key terms, never omitting an in-flight one", async () => {
      const body = contract({
        tabs: {
          ...contract().tabs,
          documents: [
            { documentId: "doc-1", fileName: "Acme_MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Completed", createdAt: "2025-01-01T00:00:00Z" },
            { documentId: "doc-2", fileName: "Acme_SOW.pdf", mimeType: "application/pdf", documentType: "Sow", processingStatus: "Processing", createdAt: "2025-01-02T00:00:00Z" },
            { documentId: "doc-3", fileName: "carbonara.pdf", mimeType: "application/pdf", documentType: "Other", processingStatus: "Rejected", createdAt: "2025-01-03T00:00:00Z" },
          ],
        },
      });
      renderContract360(populatedClient({ getContract360: vi.fn().mockResolvedValue(ok(body)) }));

      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(await screen.findByText(/Acme_SOW\.pdf/)).toBeInTheDocument();
      expect(screen.getByText("Processing")).toHaveClass("tag");
      expect(screen.getByText("Not added")).toHaveClass("tag", "tag-outline");
    });
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

    it("returns to the exact Renewals row it came from, and never follows a returnTo off the origin's own path", async () => {
      const { unmount } = renderContract360(populatedClient(), CONTRACT_ID, { from: "renewals", returnTo: `/renewals?select=${CONTRACT_ID}` });
      await screen.findByRole("heading", { level: 2, name: "MSA" });
      expect(screen.getByRole("link", { name: "← Renewals" })).toHaveAttribute("href", `/renewals?select=${CONTRACT_ID}`);
      unmount();

      renderContract360(populatedClient(), CONTRACT_ID, { from: "renewals", returnTo: "https://example.test/elsewhere" });
      await screen.findByRole("heading", { level: 2, name: "MSA" });
      expect(screen.getByRole("link", { name: "← Renewals" })).toHaveAttribute("href", "/renewals");
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
    it("renders Where you can save · When you must move · What to do; a failed strategy call degrades those answers only", async () => {
      const client = populatedClient();
      renderContract360(client);
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(client.getContractStrategy).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID);

      const band = screen.getByRole("region", { name: "Answers" });
      const cells = band.querySelectorAll(".contract360-answer");
      expect(cells).toHaveLength(3);

      expect(cells[0]).toHaveTextContent("Where you can save");
      expect(cells[0]).toHaveTextContent("Not yet available");
      expect(cells[0]).toHaveTextContent(/Benchmark Service/);
      // The saving is tracked in Savings, focused on this contract.
      expect(within(cells[0] as HTMLElement).getByRole("link", { name: "Track it in Savings →" })).toHaveAttribute(
        "href",
        `/savings?contract=${CONTRACT_ID}`,
      );

      expect(cells[1]).toHaveTextContent("When you must move");
      expect(cells[1]).toHaveTextContent("Not yet available");
      expect(cells[1]).toHaveTextContent(/could not be loaded/i);

      expect(cells[2]).toHaveTextContent("What to do");
      expect(within(cells[2] as HTMLElement).getAllByText("Start renewal negotiation now")).toHaveLength(2);
      expect(cells[2]).toHaveTextContent("Renews in 60 days with a cancellation notice due in 14 days.");
      expect(within(cells[2] as HTMLElement).getByRole("button", { name: PRIMARY_ACTION_LABEL })).toHaveClass("btn-primary");
      expect(within(cells[2] as HTMLElement).getByRole("button", { name: "Assign to me" })).toBeInTheDocument();

      // The recommendation never leaks into a fact table (ADR-019 facts vs AI).
      expect(band.querySelector("table")).toBeNull();
    });

    it("maps a successful strategy pack onto both answer cells with provenance, and does not render OpenWeakFacts", async () => {
      const pack = strategyPack();
      renderContract360(
        populatedClient({
          getContractStrategy: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, strategy: pack, error: null }),
        }),
      );
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      const band = screen.getByRole("region", { name: "Answers" });
      const cells = band.querySelectorAll(".contract360-answer");

      // 120,000 DBU a year at CHF 0.55: 0.05 over the median, 0.15 over P25.
      expect(within(cells[0] as HTMLElement).getByText("CHF 6–18k / yr")).toHaveClass("contract360-answer-value");
      expect(cells[0]).toHaveTextContent("You pay CHF 0.55 per unit against a market median of CHF 0.5 (+10%).");
      // Where the saving comes from sits behind the "i", not on the page.
      const saveInfo = within(cells[0] as HTMLElement).getByRole("button", { name: "Where this saving comes from" });
      expect(saveInfo).toHaveAccessibleDescription(/Representative market data from 214 comparable contracts · source A · as of 01\/01\/2026/);
      expect(cells[0]).not.toHaveTextContent("Not yet available");

      expect(within(cells[1] as HTMLElement).getByText(formatDateOnly(CANCEL_DEADLINE))).toHaveClass("deadline-critical");
      expect(cells[1]).toHaveTextContent("in 14 days");
      expect(cells[1]).toHaveTextContent("last day to give notice");
      expect(cells[1]).toHaveTextContent(`Term ends ${formatDateOnly(TERM_END)} and auto-renews for 12 months.`);

      expect(band).not.toHaveTextContent(/weak/i);
      expect(screen.queryByText(/open weak facts/i)).toBeNull();
    });

    it("a missing end date reads Add the end date and links to Review, never Not determined", async () => {
      const pack = strategyPack({
        whenYouMustMove: {
          renewalDate: null,
          cancellationDeadline: null,
          daysLeft: null,
          passedDeadline: false,
          explanation: "No renewal date or cancellation deadline could be determined for this contract.",
        },
      });
      renderContract360(
        populatedClient({
          getContractStrategy: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, strategy: pack, error: null }),
        }),
      );
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.getByRole("link", { name: "Add the end date" })).toHaveAttribute("href", `/contracts/${CONTRACT_ID}/review`);
      expect(screen.queryByText("Not determined")).toBeNull();
    });

    it("a rejected strategy fetch degrades that one answer and still renders the header, Why and Details", async () => {
      const getContractStrategy = vi.fn().mockResolvedValue({ ok: false, statusCode: 503, strategy: null, error: "strategy down" });
      renderContract360(populatedClient({ getContractStrategy }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(getContractStrategy).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID);
      expect(screen.getByRole("heading", { level: 2, name: "MSA" })).toBeInTheDocument();
      expect(screen.getByText("Supplier 33333333")).toBeInTheDocument();

      const saveCell = screen.getByRole("region", { name: "Answers" }).querySelectorAll(".contract360-answer")[0];
      expect(saveCell).toHaveTextContent("Not yet available");
      expect(saveCell).toHaveTextContent(/Benchmark Service/);

      expect(screen.getByRole("region", { name: "Clauses that matter" })).toBeInTheDocument();
      expect(screen.getByText("Liability cap")).toBeInTheDocument();
      expect(screen.getByRole("region", { name: "Key terms" })).toBeInTheDocument();
      expect(screen.queryByText(/contract unavailable/i)).toBeNull();
    });

    it("names an honest gap instead of a recommendation when this contract has no renewal-pipeline entry", async () => {
      renderContract360(
        populatedClient({ getRenewals: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, renewals: { items: [], totalCount: 0 }, error: null }) }),
      );
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.getByText("No renewal recommendation for this contract")).toBeInTheDocument();
      expect(screen.getByText(/did not appear in the current renewal pipeline yet/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Start negotiation" })).toHaveClass("btn-primary");
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
      const putNegotiationSteps = vi.fn().mockImplementation((_workspaceId: string, _contractId: string, body: { steps: string[] }) =>
        Promise.resolve({ ok: true, statusCode: 200, steps: body.steps, error: null }),
      );
      const getNegotiationSteps = vi
        .fn()
        .mockResolvedValueOnce({ ok: true, statusCode: 200, steps: [], error: null })
        .mockResolvedValue({ ok: true, statusCode: 200, steps: ["Notify"], error: null });
      renderContract360(populatedClient({ postRenewalAction, putNegotiationSteps, getNegotiationSteps }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      fireEvent.click(screen.getByRole("button", { name: PRIMARY_ACTION_LABEL }));

      expect(postRenewalAction).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, { owner: USER_LABEL, status: "InProgress", action: "In negotiation" });

      const tracker = await screen.findByRole("status");
      expect(tracker).toHaveTextContent("In negotiation");
      expect(tracker).toHaveTextContent(`owner ${USER_LABEL}`);
      expect(tracker).toHaveTextContent(`target Not yet available · close by Not yet available`);
      expect(screen.queryByRole("button", { name: PRIMARY_ACTION_LABEL })).not.toBeInTheDocument();

      const steps = within(tracker).getAllByRole("button", { pressed: false });
      expect(steps.map((step) => step.textContent)).toEqual([
        "Notify Supplier 33333333 of intent to renegotiatethis week",
        "Request revised pricing and licence mix+10 days",
        "Counter with the market benchmark+20 days",
        `Sign, or send non-renewal noticeby Not yet available`,
      ]);

      fireEvent.click(steps[0]);
      await waitFor(() => expect(putNegotiationSteps).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, { steps: ["Notify"] }));
      expect(steps[0]).toHaveAttribute("aria-pressed", "true");

      // Renewals opens with this contract selected, not at the top of its list.
      expect(within(tracker).getByRole("link", { name: "Track it in Renewals →" })).toHaveAttribute("href", `/renewals?select=${CONTRACT_ID}`);
      expect(window.sessionStorage.getItem("raffa.renewals.actions")).toBeNull();
      expect(window.sessionStorage.getItem(`raffa.contract360.steps.${CONTRACT_ID}`)).toBeNull();

      fireEvent.click(within(tracker).getByRole("button", { name: "Undo" }));

      await waitFor(() => expect(putNegotiationSteps).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, { steps: [] }));
      await waitFor(() => expect(postRenewalAction).toHaveBeenNthCalledWith(2, WORKSPACE_ID, CONTRACT_ID, { owner: USER_LABEL, status: "NotStarted", action: "Open" }));
      expect(putNegotiationSteps.mock.invocationCallOrder[putNegotiationSteps.mock.calls.length - 1]).toBeLessThan(
        postRenewalAction.mock.invocationCallOrder[1],
      );
      expect(await screen.findByRole("button", { name: PRIMARY_ACTION_LABEL })).toBeInTheDocument();
    });

    it("a failed tick PUT reverts the optimistic tick and names the negotiation steps in the error state", async () => {
      const postRenewalAction = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        action: { contractId: CONTRACT_ID, owner: USER_LABEL, status: "InProgress", action: "In negotiation", updatedAt: "2026-09-06T09:00:00Z" },
        error: null,
      });
      const putNegotiationSteps = vi.fn().mockResolvedValue({ ok: false, statusCode: 500, steps: null, error: "write failed" });
      renderContract360(populatedClient({ postRenewalAction, putNegotiationSteps }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      fireEvent.click(screen.getByRole("button", { name: PRIMARY_ACTION_LABEL }));
      const tracker = await screen.findByRole("status");
      const steps = within(tracker).getAllByRole("button", { pressed: false });
      fireEvent.click(steps[0]);

      await waitFor(() => expect(putNegotiationSteps).toHaveBeenCalled());
      expect(steps[0]).toHaveAttribute("aria-pressed", "false");
      const alert = await screen.findByRole("alert");
      expect(within(alert).getByRole("heading", { level: 4, name: "Negotiation steps unavailable" })).toBeInTheDocument();
      expect(within(alert).getByRole("button", { name: "Retry" })).toBeInTheDocument();
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
      expect(await screen.findByRole("button", { name: PRIMARY_ACTION_LABEL })).toBeInTheDocument();
    });

    it("shows the tracker straight away when the server already recorded an in-progress action", async () => {
      renderContract360(
        populatedClient({
          getRenewals: vi.fn().mockResolvedValue({
            ok: true,
            statusCode: 200,
            renewals: {
              items: [
                renewalPipelineItem({
                  savedAction: {
                    contractId: CONTRACT_ID,
                    owner: USER_LABEL,
                    status: "InProgress",
                    action: "In negotiation",
                    updatedAt: "2026-09-06T09:00:00Z",
                  },
                }),
              ],
              totalCount: 1,
            },
            error: null,
          }),
        }),
      );
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.getByRole("status")).toHaveTextContent("In negotiation");
      expect(screen.queryByRole("button", { name: PRIMARY_ACTION_LABEL })).not.toBeInTheDocument();
    });

    it("shows an inline error and keeps the actions when the write fails", async () => {
      const postRenewalAction = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, action: null, error: "'owner' is required." });
      renderContract360(populatedClient({ postRenewalAction }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      fireEvent.click(screen.getByRole("button", { name: PRIMARY_ACTION_LABEL }));

      expect(await screen.findByRole("alert")).toHaveTextContent("'owner' is required.");
      expect(screen.getByRole("button", { name: PRIMARY_ACTION_LABEL })).toBeEnabled();
      expect(window.sessionStorage.getItem("raffa.renewals.actions")).toBeNull();
    });
  });

  describe("03 clauses that matter", () => {
    it("groups the clauses by leverage (push · raise · standard), with the at-the-table note and the source, and opens the original wording on click", async () => {
      renderContract360(populatedClient());
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      const section = screen.getByRole("region", { name: "Clauses that matter" });
      const push = within(section).getByRole("list", { name: "Push to change" });
      expect(within(push).getByText("Push to change")).toHaveClass("tag-accent");
      expect(within(push).getByText("Costs you money or freedom — lead with these")).toBeInTheDocument();
      expect(within(push).getAllByRole("listitem")).toHaveLength(1);
      expect(within(push).getByRole("listitem")).toHaveTextContent("Auto-renewal");
      expect(within(push).getByText("This clause costs money if it stays.")).toBeInTheDocument();

      const raise = within(section).getByRole("list", { name: "Worth raising" });
      expect(within(raise).getByText("Worth raising")).toHaveClass("tag-neutral");
      const row = within(raise).getByRole("listitem");
      expect(row).toHaveTextContent("Liability cap");
      // Below the auto-accept bar but sourced (p.27): the value reads, as a product row's does.
      expect(within(row).getByText("12 months fees")).toHaveClass("contract360-clause-normalized");
      expect(within(row).getByText("Worth raising in negotiation.")).toBeInTheDocument();
      expect(within(row).getByRole("link", { name: "p.27 · §17.2" })).toHaveAttribute("href", "/documents/doc-1/viewer?page=27&clause=cl-1");
      expect(screen.queryByRole("button", { name: /standard clause/ })).toBeNull();
      expect(screen.queryByTestId("clause-highlight")).toBeNull();
      expect(section.textContent).not.toMatch(/%/);
      expect(section.textContent).not.toMatch(/\bMedium\b/);
      expect(section.textContent).not.toMatch(/\bHigh\b/);

      fireEvent.click(within(row).getByRole("button"));

      const evidence = screen.getByTestId("clause-highlight");
      expect(within(evidence).getByText("Acme_MSA.pdf · p.27 · §17.2")).toBeInTheDocument();
      expect(within(evidence).getByText("12 months fees").tagName).toBe("MARK");
      expect(evidence).toHaveTextContent("Each party's aggregate liability is capped at 12 months fees, save for confidentiality and IP.");
      expect(within(evidence).getByRole("link", { name: "Open in document viewer" })).toHaveAttribute(
        "href",
        "/documents/doc-1/viewer?page=27&clause=cl-1",
      );
      expect(within(row).getByRole("button")).toHaveAttribute("aria-pressed", "true");
      expect(row).toHaveClass("is-selected");
    });

    it("keeps the standard clauses behind 'Show N standard clauses ▾'", async () => {
      const body = contract({
        tabs: {
          ...contract().tabs,
          clauses: [
            ...contract().tabs.clauses,
            { clauseId: "cl-3", clauseType: "Confidentiality", rawText: "Mutual, 5 years.", normalizedValue: "Mutual, 5 years post-termination.", riskLevel: "Low", sourceDocumentId: "doc-1", sourceSpan: "§18", sourcePage: 28, confidence: 0.98 },
          ],
        },
      });
      renderContract360(populatedClient({ getContract360: vi.fn().mockResolvedValue(ok(body)) }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.queryByText("Confidentiality")).toBeNull();
      const toggle = screen.getByRole("button", { name: "Show 1 standard clause ▾" });
      fireEvent.click(toggle);
      expect(screen.getByRole("button", { name: "Hide 1 standard clause ▴" })).toHaveAttribute("aria-expanded", "true");
      const standard = screen.getByRole("list", { name: "Standard terms" });
      expect(within(standard).getByText("Market-standard — nothing to negotiate")).toBeInTheDocument();
      expect(within(standard).getByRole("listitem")).toHaveTextContent("Confidentiality");
    });

    it("R-EVD-02 citation landing: ?clause=<id> highlights the cited wording without a click", async () => {
      renderContract360(populatedClient(), CONTRACT_ID, undefined, "?clause=cl-1");
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(within(screen.getByTestId("clause-highlight")).getByText("12 months fees")).toBeInTheDocument();
    });

    it("?page=<n> highlights the first clause on that page when no clause id is given", async () => {
      renderContract360(populatedClient(), CONTRACT_ID, undefined, "?page=12");
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(within(screen.getByTestId("clause-highlight")).getByText("Acme_MSA.pdf · p.12 · §8.4")).toBeInTheDocument();
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

  describe("01 · 02 · 03 · 05 · 06 -- the sections never collapse", () => {
    it("renders leverage, products, obligations, the priority score with its parts, the risks and the key terms without any toggle", async () => {
      renderContract360(
        populatedClient({
          getContractStrategy: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, strategy: strategyPack(), error: null }),
        }),
      );
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      expect(screen.queryByRole("button", { name: /All terms, documents and open facts/ })).toBeNull();
      expect(screen.queryByRole("button", { name: "Hide details" })).toBeNull();

      const leverage = screen.getByRole("region", { name: "Leverage" });
      expect(within(leverage).getByText("02")).toBeInTheDocument();
      // Key terms lead the page as 01.
      const regions = screen.getAllByRole("region").map((region) => region.getAttribute("aria-label"));
      expect(regions.indexOf("Key terms")).toBeLessThan(regions.indexOf("Leverage"));
      expect(within(screen.getByRole("region", { name: "Key terms" })).getByText("01")).toBeInTheDocument();
      expect(within(leverage).getByText("Order size lever")).toBeInTheDocument();
      expect(within(leverage).getByText("This line orders 120,000 — cite the order size.")).toBeInTheDocument();

      const products = screen.getByRole("region", { name: "Products & pricing" });
      expect(within(products).getByText("Premium DBU")).toBeInTheDocument();
      expect(within(products).getByText("SKU-1 · DBU/yr")).toBeInTheDocument();
      expect(within(products).getByText("CHF 0.55")).toBeInTheDocument();
      expect(within(products).getByText("CHF 66,000")).toBeInTheDocument();
      expect(within(products).getByText("CHF 500k")).toBeInTheDocument();
      // The line's stored market comparison: P50 with its region · term · n, and the delta vs it.
      expect(within(products).getByText("CHF 0.5")).toBeInTheDocument();
      expect(within(products).getByText("CH · 12 mo · 40 contracts")).toBeInTheDocument();
      expect(within(products).getByText("+10%")).toHaveClass("is-accent");
      expect(within(products).getByRole("button", { name: "What the market median is" })).toHaveAccessibleDescription(/half pay less, half pay more/);
      expect(within(products).getByRole("button", { name: "How the saving is worked out" })).toHaveAccessibleDescription(/cheapest quarter of customers/);

      const obligations = screen.getByRole("region", { name: "Obligations" });
      expect(within(obligations).getByText("You must")).toBeInTheDocument();
      expect(within(obligations).getByText("Supplier 33333333 must")).toBeInTheDocument();
      expect(within(obligations).getByText("Annual true-up of committed DBU")).toBeInTheDocument();
      expect(within(obligations).getByText("by 15/01/2026")).toBeInTheDocument();

      const risks = screen.getByRole("region", { name: "Risk factors" });
      expect(within(risks).getByText("72")).toBeInTheDocument();
      expect(within(risks).getByText("Spend weight")).toBeInTheDocument();
      expect(within(risks).getAllByText("20 / 20")).toHaveLength(2);
      expect(within(risks).getByText("10 / 20")).toBeInTheDocument();
      expect(within(risks).getByText("Auto-renews without an explicit re-negotiation checkpoint")).toBeInTheDocument();
      expect(within(risks).getByText("High")).toHaveClass("tag-accent");

      const terms = screen.getByRole("region", { name: "Key terms" });
      expect(within(terms).getByText("Annual spend").closest(".contract360-term")).toHaveTextContent("CHF 500,000");
      expect(within(terms).getByText("Renewal term").closest(".contract360-term")).toHaveTextContent("12 months");
      expect(within(terms).getByText("Documents")).toBeInTheDocument();
      expect(within(terms).getByText(/Acme_MSA\.pdf/)).toBeInTheDocument();
      expect(within(terms).queryByRole("link", { name: /facts still need you/i })).not.toBeInTheDocument();
      expect(terms.textContent).not.toMatch(/Review · \d+%/);
      expect(terms.textContent).not.toMatch(/Accepted automatically/);

      // The recommendation never leaks into a fact section (ADR-019 facts vs AI).
      [products, obligations, risks, terms].forEach((section) => {
        expect(section.textContent).not.toContain("Start renewal negotiation now");
      });
    });

    it("says so, in place, when the strategy has no lever yet", async () => {
      renderContract360(populatedClient());
      await screen.findByRole("heading", { level: 2, name: "MSA" });
      expect(within(screen.getByRole("region", { name: "Leverage" })).getByText(/Levers light up once/)).toBeInTheDocument();
    });

    it("the trailing count line is absent at N = 0 and names N when review_required decisions exist", async () => {
      const twoPending = [
        ...acceptedEvidence().filter((row) => row.fieldName !== "endDate" && row.fieldName !== "paymentTerms"),
        fieldEvidence({ fieldName: "endDate", decision: "review_required", confidence: 0.99 }),
        fieldEvidence({ fieldName: "paymentTerms", decision: "review_required", confidence: 0.4 }),
      ];
      renderContract360(populatedClient({ getContractEvidence: vi.fn().mockResolvedValue(evidenceOk(twoPending)) }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });
      const countLink = screen.getByRole("link", { name: "2 facts still need you — Review all →" });
      expect(countLink).toHaveAttribute("href", `/contracts/${CONTRACT_ID}/review`);
    });

    it("an unofficialized key term keeps its cell as an em-dash and never drops it", async () => {
      const pendingSpend = [
        ...acceptedEvidence().filter((row) => row.fieldName !== "annualSpend"),
        fieldEvidence({ fieldName: "annualSpend", decision: "review_required", confidence: 0.71 }),
      ];
      renderContract360(populatedClient({ getContractEvidence: vi.fn().mockResolvedValue(evidenceOk(pendingSpend)) }));
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      const spendCell = screen.getByText("Annual spend").closest(".contract360-term");
      expect(spendCell).toHaveTextContent("—");
      expect(spendCell).not.toHaveTextContent("500,000");
      expect(screen.getByText("Total contract value").closest(".contract360-term")).toHaveTextContent("CHF 1,500,000");
    });
  });

  describe("close the cycle", () => {
    it("'Terminated — I sent notice' records a Completed action with the notice date, then shows the outcome; 'Reopen' goes back to In negotiation", async () => {
      const postRenewalAction = vi
        .fn()
        .mockImplementation((_ws: string, _id: string, body: { owner: string; status: string; action: string }) =>
          Promise.resolve({ ok: true, statusCode: 200, action: { contractId: CONTRACT_ID, owner: body.owner, status: body.status, action: body.action, updatedAt: "2026-09-08T00:00:00Z" }, error: null }),
        );
      renderContract360(
        populatedClient({
          postRenewalAction,
          getRenewals: vi.fn().mockResolvedValue({
            ok: true,
            statusCode: 200,
            renewals: { items: [renewalPipelineItem({ savedAction: { contractId: CONTRACT_ID, owner: USER_LABEL, status: "InProgress", action: "In negotiation", updatedAt: "2026-09-01T00:00:00Z" } })], totalCount: 1 },
            error: null,
          }),
        }),
      );
      await screen.findByRole("heading", { level: 2, name: "MSA" });

      const tracker = screen.getByRole("status");
      expect(within(tracker).getByText("Close the cycle")).toBeInTheDocument();
      expect(within(tracker).getByRole("link", { name: "Renewed — upload the signed document" })).toHaveAttribute("href", "/documents");
      fireEvent.click(within(tracker).getByRole("button", { name: "Terminated — I sent notice" }));
      const dateInput = within(tracker).getByLabelText("Date notice was sent");
      fireEvent.change(dateInput, { target: { value: "2026-09-08" } });
      fireEvent.click(within(tracker).getByRole("button", { name: /^Confirm — contract ends/ }));

      await waitFor(() =>
        expect(postRenewalAction).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, { owner: USER_LABEL, status: "Completed", action: "Terminated — notice sent 08/09/2026" }),
      );
      const outcome = await screen.findByRole("status");
      expect(outcome).toHaveTextContent("Terminated");
      expect(outcome).toHaveTextContent("notice sent 08/09/2026");
      expect(within(outcome).getByText("Notice sent")).toHaveClass("tag-outline");
      expect(within(outcome).getByText("Contract ends")).toBeInTheDocument();
      expect(within(outcome).getByText("CHF 500,000 / yr")).toBeInTheDocument();
      expect(within(outcome).getByRole("link", { name: "See it in Renewals →" })).toHaveAttribute("href", `/renewals?select=${CONTRACT_ID}`);
      expect(screen.queryByText("Close the cycle")).toBeNull();

      fireEvent.click(within(outcome).getByRole("button", { name: "Reopen" }));
      await waitFor(() => expect(postRenewalAction).toHaveBeenLastCalledWith(WORKSPACE_ID, CONTRACT_ID, { owner: USER_LABEL, status: "InProgress", action: "In negotiation" }));
      expect(await screen.findByText("Close the cycle")).toBeInTheDocument();
    });
  });
});
