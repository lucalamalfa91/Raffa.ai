import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import ReviewRoute from "../../../../src/routes/contracts/review";
import type {
  ApiClient,
  Contract360Body,
  ContractFieldEvidenceBody,
  CorrectContractResult,
  CorrectionHistoryEntryBody,
  GetContract360Result,
  GetContractEvidenceResult,
  GetCorrectionHistoryResult,
  ValidateDocumentResult,
} from "../../../../src/api/client";

const REVIEW_READY = /facts need you/;
const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const CONTRACT_ID = "22222222-2222-2222-2222-222222222222";
const DOCUMENT_ID = "55555555-5555-5555-5555-555555555555";

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
    // Task E13/F09/US01/T03 (web-documents-v2): this suite does not exercise Documents -- bare
    // vi.fn() is enough, same convention as getPortfolio below.
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    deleteAllDocuments: vi.fn(),
    prioritiseDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, history: [], error: null }),
    correctContract: vi.fn(),
    // No evidence by default: every field keeps the conservative "Needs review" posture, which is
    // what the AC-4 gating tests below rely on; the evidence-pane tests pass real rows explicitly.
    getContractEvidence: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, evidence: [], autoAcceptThreshold: 0.87, error: null }),
    getContractStrategy: vi.fn(),
    validateDocument: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      validation: {
        documentId: DOCUMENT_ID,
        contractId: CONTRACT_ID,
        processingStatus: "Completed",
        validatedAt: "2026-09-09T10:00:00Z",
        acceptedFields: [],
        alreadyValidated: false,
      },
      error: null,
    } satisfies ValidateDocumentResult),
    // Task E13/F09/US01/T04 (web-ask-v2): this suite never reaches conversations/capabilities/
    // market -- bare vi.fn() is enough, same convention as getRenewalPriority above.
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    postConversationFeedback: vi.fn(),
    deleteConversation: vi.fn(),
    renameConversation: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E08/F01/US01/T01 (renewal-pipeline): this suite never reaches the Renewals screen --
    // bare vi.fn() is enough, same convention as the other calls above.
    postRenewalAction: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): same "never reaches the Renewals screen" reasoning as
    // postRenewalAction immediately above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
    getQuote: vi.fn(),
    getNegotiationSteps: vi.fn(),
    putNegotiationSteps: vi.fn(),
    // Task E08/F03/US01/T01 (quote-check-ui): this suite never reaches the Quote Check screen --
    // bare vi.fn() is enough, same convention as getRenewalPriority above.
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askRaffa: vi.fn(),
    // Task E08/F02/US01/T01 (savings-home): this suite never reaches Home's own fetch-outcome
    // matrix -- bare vi.fn() is enough, same convention as the other calls above.
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    ...overrides,
  };
}

/** Only the four *required* correctable fields populated (type/status/currency/autoRenewal), every
 * optional one left `null` -- keeps the review list short and deterministic for tests that need to
 * resolve every row (e.g. the AC-4 gating test), while `fullContract()` below covers the full field
 * set for AC-1/AC-3 coverage. */
function minimalContract(overrides: Partial<Contract360Body> = {}): Contract360Body {
  return {
    contractId: CONTRACT_ID,
    // Task E16/F02/US03/T01 (ADR-027 §D9): a reviewable fixture has one extracted document.
    readiness: { state: "ready", stage: null, documentCount: 1, completedDocumentCount: 1 },
    header: {
      contractId: CONTRACT_ID,
      supplierId: null,
      // Task E13/F03/US01/T02: supplierName is required now (null when unresolved).
      supplierName: null,
      type: "Msa",
      status: "active",
      annualSpend: null,
      totalContractValue: null,
      startDate: null,
      endDate: null,
      renewalDate: null,
      cancellationDeadline: null,
      autoRenewal: true,
      risk: null,
    },
    tabs: {
      overview: {
        currency: "CHF",
        effectiveDate: null,
        renewalTermMonths: null,
        paymentTerms: null,
        governingLaw: null,
        parentContractId: null,
        version: 1,
        createdAt: "2025-01-01T00:00:00Z",
      },
      commercials: {
        annualSpend: null,
        totalContractValue: null,
        currency: "CHF",
        paymentTerms: null,
        autoRenewal: true,
        renewalTermMonths: null,
        lineItemCount: 0,
        lineItemAnnualCostTotal: null,
        lineItemTotalCostTotal: null,
      },
      products: [],
      clauses: [],
      obligations: [],
      risks: [],
      // The document this review signs off: the routed screen has no document id of its own and
      // resolves it from here (reviewViewModel.ts#resolveReviewDocument).
      documents: [
        {
          documentId: DOCUMENT_ID,
          fileName: "acme-msa.pdf",
          mimeType: "application/pdf",
          documentType: "Msa",
          processingStatus: "NeedsReview",
          createdAt: "2026-09-09T09:00:00Z",
        },
      ],
      benchmark: [],
      renewal: { endDate: null, renewalDate: null, cancellationDeadline: null, autoRenewal: true, renewalTermMonths: null },
      activity: [],
    },
    ...overrides,
  };
}

function evidenceOk(rows: ContractFieldEvidenceBody[], autoAcceptThreshold = 0.87): GetContractEvidenceResult {
  return { ok: true, statusCode: 200, evidence: rows, autoAcceptThreshold, error: null };
}

function evidenceRow(overrides: Partial<ContractFieldEvidenceBody> = {}): ContractFieldEvidenceBody {
  return {
    fieldName: "paymentTerms",
    value: "Net 45",
    confidence: 0.96,
    decision: "auto_accepted",
    sourcePage: 2,
    sourceSpan: "payable within forty-five (45) days",
    sourceDocumentId: DOCUMENT_ID,
    sourceFileName: "acme-msa.pdf",
    passage: "All invoices are payable within forty-five (45) days of receipt.",
    highlightStart: 17,
    highlightLength: 35,
    box: null,
    modelId: "fixture-extract-model",
    extractedAt: "2026-09-09T10:00:00Z",
    ...overrides,
  };
}

function autoAcceptedEvidence(names: readonly string[] = ["type", "status", "currency", "autoRenewal"]): ContractFieldEvidenceBody[] {
  return names.map((fieldName) => evidenceRow({ fieldName, decision: "auto_accepted", confidence: 0.96 }));
}

function fullContract(overrides: Partial<Contract360Body> = {}): Contract360Body {
  const base = minimalContract();
  return {
    ...base,
    header: {
      ...base.header,
      annualSpend: 500_000,
      totalContractValue: 1_500_000,
      startDate: "2025-01-01",
      endDate: "2026-01-01",
      cancellationDeadline: "2025-11-17",
    },
    tabs: {
      ...base.tabs,
      overview: {
        ...base.tabs.overview,
        effectiveDate: "2025-01-01",
        renewalTermMonths: 12,
        paymentTerms: "Net 45",
        governingLaw: "Switzerland, Zürich",
      },
    },
    ...overrides,
  };
}

function recoveredContract(overrides: Partial<Contract360Body> = {}): Contract360Body {
  const base = fullContract();
  return {
    ...base,
    header: {
      ...base.header,
      supplierName: "Northwind Traders SA",
    },
    ...overrides,
  };
}

function ok(body: Contract360Body): GetContract360Result {
  return { ok: true, statusCode: 200, contract: body, error: null };
}

function historyOk(entries: CorrectionHistoryEntryBody[]): GetCorrectionHistoryResult {
  return { ok: true, statusCode: 200, history: entries, error: null };
}

function correctionEntry(overrides: Partial<CorrectionHistoryEntryBody> = {}): CorrectionHistoryEntryBody {
  return {
    fieldName: "annualSpend",
    previousValue: "480000",
    newValue: "500000",
    correctedBy: "unattributed",
    correctedAt: "2026-09-01T08:00:00Z",
    reason: "Corrected from the signed order form.",
    ...overrides,
  };
}

function renderReview(apiClient: ApiClient, contractId = CONTRACT_ID) {
  return render(
    <MemoryRouter initialEntries={[`/contracts/${contractId}/review`]}>
      <Routes>
        <Route path="/contracts/:contractId/review" element={<ReviewRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ReviewRoute", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const getContract360 = vi.fn();

    renderReview(mockApiClient({ getContract360 }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(getContract360).not.toHaveBeenCalled();
  });

  it("shows a loading skeleton while the request is in flight, then replaces it", async () => {
    let resolveFetch!: (value: GetContract360Result) => void;
    const pending = new Promise<GetContract360Result>((resolve) => {
      resolveFetch = resolve;
    });
    const { container } = renderReview(mockApiClient({ getContract360: vi.fn().mockReturnValue(pending) }));

    expect(container.querySelector(".review-skeleton")).toBeInTheDocument();

    resolveFetch(ok(minimalContract()));

    expect(await screen.findByText(REVIEW_READY)).toBeInTheDocument();
    expect(container.querySelector(".review-skeleton")).not.toBeInTheDocument();
  });

  it("calls getContract360 then getCorrectionHistory with the current workspace id and the route's contractId", async () => {
    const getContract360 = vi.fn().mockResolvedValue(ok(minimalContract()));
    const getCorrectionHistory = vi.fn().mockResolvedValue(historyOk([]));
    renderReview(mockApiClient({ getContract360, getCorrectionHistory }));

    await screen.findByText(REVIEW_READY);

    expect(getContract360).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID);
    expect(getCorrectionHistory).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID);
  });

  it("renders a named not-found state on a 404, not a generic error", async () => {
    renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, contract: null, error: null }) }));

    expect(await screen.findByText(/contract not found/i)).toBeInTheDocument();
  });

  it("renders a plain-language error with a Retry that re-fetches on a 503", async () => {
    const getContract360 = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: 503, contract: null, error: "Service Unavailable" })
      .mockResolvedValueOnce(ok(minimalContract()));
    renderReview(mockApiClient({ getContract360 }));

    expect(await screen.findByText(/temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.getByRole("alert")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByText(REVIEW_READY)).toBeInTheDocument();
    expect(getContract360).toHaveBeenCalledTimes(2);
  });

  describe("once populated (AC-1/AC-2)", () => {
    it("renders the 4 column headers and one row per correctable field that has a value", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(fullContract())) }));
      await screen.findByText(REVIEW_READY);

      const headerRow = screen.getAllByRole("columnheader").map((cell) => cell.textContent);
      expect(headerRow).toEqual(["Field", "Extracted value", "Confidence", "Decision"]);

      expect(screen.getByRole("button", { name: "Contract type" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Annual spend" })).toBeInTheDocument();
      expect(screen.getByText("500,000")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Auto-renewal" })).toBeInTheDocument();
      expect(screen.getByText("Yes")).toBeInTheDocument();
    });

    it("renders never-extracted canonical fields as empty fillable inputs, not table rows", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText(REVIEW_READY);

      expect(screen.queryByRole("button", { name: "Governing law" })).toBeNull();
      expect(screen.queryByRole("button", { name: "Payment terms" })).toBeNull();
      expect(screen.queryByRole("button", { name: "End date" })).toBeNull();
      expect(screen.getByLabelText("Governing law")).toHaveValue("");
      expect(screen.getByLabelText("End date")).toHaveValue("");
      expect(screen.getByRole("button", { name: "Contract type" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Status" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Currency" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Auto-renewal" })).toBeInTheDocument();
    });

    it("shows an honest 'Needs review' tag for a pending field, never a fabricated confidence percentage", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText(REVIEW_READY);

      expect(screen.getAllByText("Needs review").length).toBeGreaterThan(0);
      // Scoped to the field table, not the whole screen: the header legend paints the server's
      // threshold (not a per-field score) next to the progress line -- this assertion's real
      // intent is that no *row* ever shows a fabricated per-field percentage.
      expect(within(screen.getByRole("table")).queryByText(/%/)).toBeNull();
    });

    it("shows 'Corrected → <value>' for a field with real correction history, and does not count it as blocking", async () => {
      renderReview(
        mockApiClient({
          // fullContract() (not minimalContract()) so "annualSpend" -- correctionEntry()'s default
          // fieldName -- actually has a value and therefore renders a row at all.
          getContract360: vi.fn().mockResolvedValue(ok(fullContract())),
          getCorrectionHistory: vi.fn().mockResolvedValue(historyOk([correctionEntry()])),
        }),
      );
      await screen.findByText(REVIEW_READY);

      expect(screen.getByText("Corrected → 500000")).toBeInTheDocument();
      expect(screen.getAllByText("Corrected").length).toBeGreaterThan(0);
    });
  });

  describe("AC-4: gated 'Mark as validated'", () => {
    it("is disabled with a visible reason while any field is pending", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText(REVIEW_READY);

      const cta = screen.getByRole("button", { name: /mark as validated/i });
      expect(cta).toBeDisabled();
      expect(screen.getByText(/still needs? review before this contract can be marked validated/i)).toBeInTheDocument();
    });

    it("becomes enabled when the server has already accepted every field, writes the sign-off, then navigates to Contract 360", async () => {
      const client = mockApiClient({
        getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
        getContractEvidence: vi.fn().mockResolvedValue(evidenceOk(autoAcceptedEvidence())),
      });
      renderReview(client);
      await screen.findByText(REVIEW_READY);

      expect(screen.getByRole("heading", { name: "Not found in the document" })).toBeInTheDocument();
      const cta = screen.getByRole("button", { name: /mark as validated/i });
      expect(cta).toBeEnabled();

      fireEvent.click(cta);
      expect(await screen.findByText("CONTRACT_360_SCREEN")).toBeInTheDocument();
      expect(client.validateDocument).toHaveBeenCalledWith(WORKSPACE_ID, DOCUMENT_ID, {
        acceptedFields: [],
      });
    });

    it("a field with real correction history does not block the gate on its own", async () => {
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getCorrectionHistory: vi
            .fn()
            .mockResolvedValue(historyOk([correctionEntry({ fieldName: "type", previousValue: "Sow", newValue: "Msa" })])),
          getContractEvidence: vi
            .fn()
            .mockResolvedValue(evidenceOk(autoAcceptedEvidence(["status", "currency", "autoRenewal"]))),
        }),
      );
      await screen.findByText(REVIEW_READY);

      expect(screen.getByRole("button", { name: /mark as validated/i })).toBeEnabled();
    });
  });

  describe("AC-3: evidence pane + correction form", () => {
    it("selecting a field opens the evidence pane pre-filled with its current value", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(fullContract())) }));
      await screen.findByText(REVIEW_READY);

      fireEvent.click(screen.getByRole("button", { name: "Payment terms" }));

      expect(await screen.findByRole("heading", { name: "Payment terms", level: 6 })).toBeInTheDocument();
      expect(screen.getByText("Extracted value: Net 45")).toBeInTheDocument();
      expect(screen.getByLabelText("Correct value")).toHaveValue("Net 45");
    });

    it("submitting the correction form calls correctContract with the field name and new value, then re-fetches", async () => {
      const correctionResult: CorrectContractResult = {
        ok: true,
        statusCode: 200,
        correction: { contractId: CONTRACT_ID, versionNumber: 2, correctedFields: ["paymentTerms"], correctedAt: "2026-09-06T08:00:00Z" },
        error: null,
      };
      const original = fullContract();
      const updated = fullContract({
        tabs: {
          ...original.tabs,
          overview: { ...original.tabs.overview, paymentTerms: "Net 60" },
          commercials: { ...original.tabs.commercials, paymentTerms: "Net 60" },
        },
      });
      const correctContract = vi.fn().mockResolvedValue(correctionResult);
      const getContract360 = vi.fn().mockResolvedValueOnce(ok(original)).mockResolvedValue(ok(updated));
      const getCorrectionHistory = vi
        .fn()
        .mockResolvedValueOnce(historyOk([]))
        .mockResolvedValue(
          historyOk([
            correctionEntry({
              fieldName: "paymentTerms",
              previousValue: "Net 45",
              newValue: "Net 60",
              correctedAt: "2026-09-06T08:00:00Z",
              reason: "Renegotiated payment terms.",
            }),
          ]),
        );

      renderReview(mockApiClient({ getContract360, getCorrectionHistory, correctContract }));
      await screen.findByText(REVIEW_READY);

      fireEvent.click(screen.getByRole("button", { name: "Payment terms" }));
      fireEvent.change(await screen.findByLabelText("Correct value"), { target: { value: "Net 60" } });
      fireEvent.change(screen.getByLabelText("Reason (optional)"), { target: { value: "Renegotiated payment terms." } });
      fireEvent.click(screen.getByRole("button", { name: /save correction/i }));

      await waitFor(() =>
        expect(correctContract).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, {
          corrections: { paymentTerms: "Net 60" },
          reason: "Renegotiated payment terms.",
        }),
      );
      // A successful correction merges into the mounted table, then silently re-fetches. The
      // review route stays put — no skeleton, no navigation off `/review`.
      await waitFor(() => expect(getContract360).toHaveBeenCalledTimes(2));
      expect(screen.queryByText("Loading review…")).toBeNull();
      expect(screen.queryByText("CONTRACT_360_SCREEN")).toBeNull();
      const paymentRow = screen.getByRole("button", { name: "Payment terms" }).closest("tr")!;
      expect(paymentRow).toHaveTextContent("Corrected → Net 60");
      expect(within(paymentRow).queryByRole("button", { name: "Accept" })).toBeNull();
    });

    it("Save correction updates the row in place without a loading skeleton", async () => {
      const original = fullContract();
      const updated = fullContract({
        tabs: {
          ...original.tabs,
          overview: { ...original.tabs.overview, paymentTerms: "Net 60" },
          commercials: { ...original.tabs.commercials, paymentTerms: "Net 60" },
        },
      });
      const getContract360 = vi.fn().mockResolvedValueOnce(ok(original)).mockResolvedValue(ok(updated));
      const getCorrectionHistory = vi
        .fn()
        .mockResolvedValueOnce(historyOk([]))
        .mockResolvedValue(
          historyOk([
            correctionEntry({
              fieldName: "paymentTerms",
              previousValue: "Net 45",
              newValue: "Net 60",
              correctedAt: "2026-09-06T08:00:00Z",
            }),
          ]),
        );
      const correctContract = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        correction: { contractId: CONTRACT_ID, versionNumber: 2, correctedFields: ["paymentTerms"], correctedAt: "2026-09-06T08:00:00Z" },
        error: null,
      } satisfies CorrectContractResult);

      const { container } = renderReview(mockApiClient({ getContract360, getCorrectionHistory, correctContract }));
      await screen.findByText(REVIEW_READY);

      fireEvent.click(screen.getByRole("button", { name: "Payment terms" }));
      fireEvent.change(await screen.findByLabelText("Correct value"), { target: { value: "Net 60" } });
      fireEvent.click(screen.getByRole("button", { name: /save correction/i }));

      await waitFor(() => expect(correctContract).toHaveBeenCalled());
      expect(container.querySelector(".review-skeleton")).toBeNull();
      expect(screen.queryByText("Loading review…")).toBeNull();
      expect(screen.getByRole("table")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Payment terms" }).closest("tr")).toHaveTextContent("Corrected → Net 60");
    });

    it("shows an inline error and does not clear the form on a failed correction (e.g. a rejected no-op)", async () => {
      const correctContract = vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 400,
        correction: null,
        error: "None of the supplied values differ from the contract's current values.",
      });
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(fullContract())), correctContract }));
      await screen.findByText(REVIEW_READY);

      fireEvent.click(screen.getByRole("button", { name: "Payment terms" }));
      fireEvent.click(screen.getByRole("button", { name: /save correction/i }));

      expect(await screen.findByRole("alert")).toHaveTextContent(/none of the supplied values differ/i);
      expect(screen.getByLabelText("Correct value")).toHaveValue("Net 45");
    });

    it("paints human_accepted from the server as 'Accepted by you' with no click and no session store", async () => {
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue(
            evidenceOk([
              evidenceRow({ fieldName: "type", decision: "human_accepted", confidence: 0.71 }),
              ...autoAcceptedEvidence(["status", "currency", "autoRenewal"]),
            ]),
          ),
        }),
      );
      await screen.findByText(REVIEW_READY);

      expect(screen.getAllByText("Accepted by you").length).toBeGreaterThan(0);
      expect(screen.getAllByText("Accepted automatically · 96%").length).toBeGreaterThan(0);
    });

    it("paints the same tags on remount with the same server payload — nothing is session-held", async () => {
      const client = mockApiClient({
        getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
        getContractEvidence: vi.fn().mockResolvedValue(
          evidenceOk([
            evidenceRow({ fieldName: "type", decision: "human_accepted", confidence: 0.71 }),
            ...autoAcceptedEvidence(["status", "currency", "autoRenewal"]),
          ]),
        ),
      });
      const first = renderReview(client);
      await screen.findByText(REVIEW_READY);
      expect(screen.getAllByText("Accepted by you").length).toBeGreaterThan(0);
      expect(screen.getAllByText("Accepted automatically · 96%").length).toBeGreaterThan(0);

      first.unmount();
      renderReview(client);
      await screen.findByText(REVIEW_READY);
      expect(screen.getAllByText("Accepted by you").length).toBeGreaterThan(0);
      expect(screen.getAllByText("Accepted automatically · 96%").length).toBeGreaterThan(0);
    });
  });

  describe("Visual fidelity (E11/F07/US01/T01, gap G-REV)", () => {
    it("shows a two-item, server-fed legend and a title that names no threshold", async () => {
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue(evidenceOk([], 0.87)),
        }),
      );
      expect(await screen.findByRole("heading", { level: 2, name: "4 facts need you — you decide" })).toBeInTheDocument();

      const legend = screen.getByText("auto-accepted").closest(".review-legend") as HTMLElement;
      expect(legend.querySelectorAll(".review-legend-item")).toHaveLength(2);
      expect(within(legend).getByText("≥87%")).toBeInTheDocument();
      expect(within(legend).getByText("<87%")).toBeInTheDocument();
      expect(within(legend).queryByText(/flagged/i)).toBeNull();
    });

    it("shows a one-line header summary built from the real supplier id and contract type, never fabricated", async () => {
      const withSupplier = fullContract({
        header: { ...fullContract().header, supplierId: "33333333-3333-3333-3333-333333333333" },
      });
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(withSupplier)) }));

      // formatSupplier/getContractTypeLabel -- the same honest substitutes
      // ../contract360/Contract360Header.tsx already uses for this identical gap (no supplier-name or
      // filename field on `Contract`), not the export's own hardcoded demo string.
      expect(await screen.findByText("Supplier 33333333 · MSA")).toBeInTheDocument();
    });

    it("renders the real evidence card: file · page header, the passage with the span highlighted, model and confidence", async () => {
      const { container } = renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(fullContract())),
          getContractEvidence: vi.fn().mockResolvedValue(evidenceOk([evidenceRow()])),
        }),
      );
      await screen.findByText(REVIEW_READY);

      fireEvent.click(screen.getByRole("button", { name: "Payment terms" }));

      const card = container.querySelector(".review-evidence-passage");
      expect(card).not.toBeNull();
      expect(await screen.findByText("ACME-MSA.PDF · PAGE 2")).toBeInTheDocument();
      expect(card!.querySelector("mark.review-evidence-highlight")).toHaveTextContent("payable within forty-five (45) days");
      expect(card).toHaveTextContent("All invoices are payable within forty-five (45) days of receipt.");
      expect(screen.getByText("Extracted by fixture-extract-model · confidence 96%")).toBeInTheDocument();
      // The list row carries the same provenance and a real, non-fabricated confidence tag.
      expect(screen.getByText("p. 2 · “payable within forty-five (45) days”")).toBeInTheDocument();
      expect(screen.getByText("Accepted automatically · 96%")).toBeInTheDocument();
    });

    it("shows the quote alone when the page text could not be located, and says so when no source was recorded at all", async () => {
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(fullContract())),
          getContractEvidence: vi.fn().mockResolvedValue(
            evidenceOk([evidenceRow({ passage: null, highlightStart: null, highlightLength: null })]),
          ),
        }),
      );
      await screen.findByText(REVIEW_READY);

      fireEvent.click(screen.getByRole("button", { name: "Payment terms" }));
      expect(await screen.findByText("payable within forty-five (45) days", { selector: "mark" })).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "Annual spend" }));
      expect(await screen.findByText(/no source passage was recorded for this field/i)).toBeInTheDocument();
    });

    it("describes a classification verdict (no page, no span) as read from the whole document", async () => {
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue(
            evidenceOk([evidenceRow({ fieldName: "type", value: "Msa", confidence: 0.99, sourcePage: null, sourceSpan: null, passage: null, highlightStart: null, highlightLength: null, modelId: null })]),
          ),
        }),
      );
      await screen.findByText(REVIEW_READY);

      fireEvent.click(screen.getByRole("button", { name: "Contract type" }));

      expect(await screen.findByText("ACME-MSA.PDF · WHOLE DOCUMENT")).toBeInTheDocument();
      expect(screen.getByText(/read from the full document text/i)).toBeInTheDocument();
      expect(screen.getByText("Extracted · confidence 99%")).toBeInTheDocument();
    });

    it("builds a Supplier row from a weak, unapplied proposal and makes Accept write it through correctContract", async () => {
      const correctContract = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        correction: { contractId: CONTRACT_ID, versionNumber: 2, correctedFields: ["supplier"], correctedAt: "2026-09-09T10:00:00Z" },
        error: null,
      } satisfies CorrectContractResult);
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue(
            evidenceOk([evidenceRow({ fieldName: "supplier", value: "Fabrikam Software GmbH", confidence: 0.52, decision: "review_required", sourcePage: 1, sourceSpan: "between Raffa Demo AG and Fabrikam Software GmbH", passage: null, highlightStart: null, highlightLength: null })]),
          ),
          correctContract,
        }),
      );
      await screen.findByText(REVIEW_READY);

      const supplierRow = screen.getByRole("button", { name: "Supplier" }).closest("tr")!;
      expect(supplierRow).toHaveTextContent("Fabrikam Software GmbH");
      expect(supplierRow).toHaveTextContent("Proposed, not yet applied");
      expect(supplierRow).toHaveTextContent("Review · 52%");

      fireEvent.click(within(supplierRow).getByRole("button", { name: "Accept" }));

      await waitFor(() =>
        expect(correctContract).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, {
          corrections: { supplier: "Fabrikam Software GmbH" },
          reason: "Accepted as extracted.",
        }),
      );
    });

    it("Accept on an already-applied review_required field PATCHes the extracted value so the server can officialize it", async () => {
      const correctContract = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        correction: { contractId: CONTRACT_ID, versionNumber: 0, correctedFields: ["status"], correctedAt: "2026-09-17T14:00:00Z" },
        error: null,
      } satisfies CorrectContractResult);
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue(
            evidenceOk([
              evidenceRow({
                fieldName: "status",
                value: "active",
                confidence: 0,
                decision: "review_required",
                sourcePage: null,
                sourceSpan: null,
                passage: null,
                highlightStart: null,
                highlightLength: null,
              }),
              ...autoAcceptedEvidence(["type", "currency", "autoRenewal"]),
            ]),
          ),
          correctContract,
        }),
      );
      await screen.findByText(REVIEW_READY);

      const statusRow = screen.getByRole("button", { name: "Status" }).closest("tr")!;
      expect(statusRow).toHaveTextContent("Review · 0%");
      fireEvent.click(within(statusRow).getByRole("button", { name: "Accept" }));

      await waitFor(() =>
        expect(correctContract).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, {
          corrections: { status: "active" },
          reason: "Accepted as extracted.",
        }),
      );
    });

    it("Accept updates that row in place: Accepted by you, no Accept button, no skeleton or navigation", async () => {
      const pendingEvidence = evidenceOk([
        evidenceRow({
          fieldName: "status",
          value: "active",
          confidence: 0.5,
          decision: "review_required",
          sourcePage: null,
          sourceSpan: null,
          passage: null,
          highlightStart: null,
          highlightLength: null,
        }),
        ...autoAcceptedEvidence(["type", "currency", "autoRenewal"]),
      ]);
      const acceptedEvidence = evidenceOk([
        evidenceRow({
          fieldName: "status",
          value: "active",
          confidence: 0.5,
          decision: "human_accepted",
          sourcePage: null,
          sourceSpan: null,
          passage: null,
          highlightStart: null,
          highlightLength: null,
        }),
        ...autoAcceptedEvidence(["type", "currency", "autoRenewal"]),
      ]);
      const getContract360 = vi.fn().mockResolvedValue(ok(minimalContract()));
      const getContractEvidence = vi.fn().mockResolvedValueOnce(pendingEvidence).mockResolvedValue(acceptedEvidence);
      const correctContract = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        correction: { contractId: CONTRACT_ID, versionNumber: 0, correctedFields: ["status"], correctedAt: "2026-09-17T14:00:00Z" },
        error: null,
      } satisfies CorrectContractResult);

      const { container } = renderReview(
        mockApiClient({
          getContract360,
          getContractEvidence,
          correctContract,
        }),
      );
      await screen.findByText(REVIEW_READY);

      expect(screen.getByRole("heading", { level: 2, name: "1 facts need you — you decide" })).toBeInTheDocument();
      expect(screen.getByText(/3 of 4 fields resolved · 1 needs review/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /mark as validated/i })).toBeDisabled();

      const statusRowBefore = screen.getByRole("button", { name: "Status" }).closest("tr")!;
      fireEvent.click(within(statusRowBefore).getByRole("button", { name: "Accept" }));

      await waitFor(() => {
        const statusRow = screen.getByRole("button", { name: "Status" }).closest("tr")!;
        expect(within(statusRow).queryByRole("button", { name: "Accept" })).toBeNull();
        expect(statusRow).toHaveTextContent("Accepted by you");
        expect(statusRow).not.toHaveTextContent("Review · 50%");
      });

      expect(container.querySelector(".review-skeleton")).toBeNull();
      expect(screen.queryByText("Loading review…")).toBeNull();
      expect(screen.queryByText("CONTRACT_360_SCREEN")).toBeNull();
      expect(screen.getByRole("table")).toBeInTheDocument();
      expect(screen.getByRole("heading", { level: 2, name: "0 facts need you — you decide" })).toBeInTheDocument();
      expect(screen.getByText(/4 of 4 fields resolved/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /mark as validated/i })).toBeEnabled();
    });

    it("keeps the row as review_required when Accept's PATCH fails", async () => {
      const correctContract = vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 400,
        correction: null,
        error: "None of the supplied values differ from the contract's current values.",
      } satisfies CorrectContractResult);
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue(
            evidenceOk([
              evidenceRow({
                fieldName: "status",
                value: "active",
                confidence: 0,
                decision: "review_required",
                sourcePage: null,
                sourceSpan: null,
                passage: null,
                highlightStart: null,
                highlightLength: null,
              }),
              ...autoAcceptedEvidence(["type", "currency", "autoRenewal"]),
            ]),
          ),
          correctContract,
        }),
      );
      await screen.findByText(REVIEW_READY);

      const statusRow = screen.getByRole("button", { name: "Status" }).closest("tr")!;
      fireEvent.click(within(statusRow).getByRole("button", { name: "Accept" }));

      expect(await screen.findByRole("alert")).toHaveTextContent(/none of the supplied values differ/i);
      expect(within(statusRow).getByRole("button", { name: "Accept" })).toBeInTheDocument();
      expect(statusRow).toHaveTextContent("Review · 0%");
      expect(screen.queryByText("Loading review…")).toBeNull();
    });

    it("Save correction with the extracted value still PATCHes — confirming is not a client-side no-op", async () => {
      const correctContract = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        correction: { contractId: CONTRACT_ID, versionNumber: 0, correctedFields: ["status"], correctedAt: "2026-09-17T14:00:00Z" },
        error: null,
      } satisfies CorrectContractResult);
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue(
            evidenceOk([
              evidenceRow({
                fieldName: "status",
                value: "active",
                confidence: 0,
                decision: "review_required",
                sourcePage: null,
                sourceSpan: null,
                passage: null,
                highlightStart: null,
                highlightLength: null,
              }),
              ...autoAcceptedEvidence(["type", "currency", "autoRenewal"]),
            ]),
          ),
          correctContract,
        }),
      );
      await screen.findByText(REVIEW_READY);

      fireEvent.click(screen.getByRole("button", { name: "Status" }));
      fireEvent.click(await screen.findByRole("button", { name: /save correction/i }));

      await waitFor(() =>
        expect(correctContract).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, {
          corrections: { status: "active" },
          reason: null,
        }),
      );
    });

    it("shows the API error in the evidence pane when Accept is rejected", async () => {
      const correctContract = vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 400,
        correction: null,
        error: "None of the supplied values differ from the contract's current values.",
      } satisfies CorrectContractResult);
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue(
            evidenceOk([
              evidenceRow({
                fieldName: "status",
                value: "active",
                confidence: 0,
                decision: "review_required",
                sourcePage: null,
                sourceSpan: null,
                passage: null,
                highlightStart: null,
                highlightLength: null,
              }),
              ...autoAcceptedEvidence(["type", "currency", "autoRenewal"]),
            ]),
          ),
          correctContract,
        }),
      );
      await screen.findByText(REVIEW_READY);

      fireEvent.click(within(screen.getByRole("button", { name: "Status" }).closest("tr")!).getByRole("button", { name: "Accept" }));

      expect(await screen.findByRole("alert")).toHaveTextContent(/none of the supplied values differ/i);
      expect(screen.getByRole("heading", { name: "Status", level: 6 })).toBeInTheDocument();
    });

    it("shows a visible banner and keeps every field reviewable when the evidence call fails", async () => {
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue({ ok: false, statusCode: 503, evidence: null, error: "Service Unavailable" }),
        }),
      );
      await screen.findByText(REVIEW_READY);

      expect(screen.getByText(/extraction evidence could not be loaded/i)).toBeInTheDocument();
      expect(screen.getAllByText("Needs review").length).toBeGreaterThan(0);
    });
  });

  describe("Mark as validated writes the sign-off (POST /api/documents/{id}/validate)", () => {
    it("shows the server's own refusal under the CTA and stays on the screen when validation fails", async () => {
      const validateDocument = vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 409,
        validation: null,
        error: "This document is still being processed; wait for extraction to finish before validating it.",
      } satisfies ValidateDocumentResult);
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getContractEvidence: vi.fn().mockResolvedValue(evidenceOk(autoAcceptedEvidence())),
          validateDocument,
        }),
      );
      await screen.findByText(REVIEW_READY);

      fireEvent.click(screen.getByRole("button", { name: /mark as validated/i }));

      expect(await screen.findByRole("alert")).toHaveTextContent(/still being processed/i);
      expect(screen.queryByText("CONTRACT_360_SCREEN")).toBeNull();
      expect(screen.getByRole("button", { name: /mark as validated/i })).toBeEnabled();
    });

    it("reads an already-validated document as closed: the CTA says Validated and stays disabled", async () => {
      const validated = minimalContract();
      validated.tabs.documents[0].processingStatus = "Completed";
      const validateDocument = vi.fn();
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(validated)), validateDocument }));
      await screen.findByText(REVIEW_READY);

      expect(screen.getByRole("button", { name: "Validated" })).toBeDisabled();
      expect(screen.getByText(/this document is already validated/i)).toBeInTheDocument();
      expect(validateDocument).not.toHaveBeenCalled();
    });

    it("explains when the contract has no document to validate", async () => {
      const noDocuments = minimalContract({ tabs: { ...minimalContract().tabs, documents: [] } });
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(noDocuments)) }));
      await screen.findByText(REVIEW_READY);

      expect(screen.getByRole("button", { name: /mark as validated/i })).toBeDisabled();
      expect(screen.getByText(/no document to validate/i)).toBeInTheDocument();
    });
  });

  describe("NW-64: Not found in the document", () => {
    it("renders the section with its heading and sentence at the end of the field-list column", async () => {
      const { container } = renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText(REVIEW_READY);

      const heading = screen.getByRole("heading", { name: "Not found in the document" });
      expect(heading).toBeInTheDocument();
      expect(
        screen.getByText("Raffa could not find these in the file. Type the value if you have it — it is saved as your correction."),
      ).toBeInTheDocument();

      const column = container.querySelector(".review-field-column");
      const pane = container.querySelector(".review-evidence-pane");
      expect(column).not.toBeNull();
      expect(pane).not.toBeNull();
      expect(column!.contains(heading)).toBe(true);
      expect(pane!.contains(heading)).toBe(false);
      expect(screen.getByLabelText("End date")).toHaveValue("");
      expect(screen.getByLabelText("Cancellation deadline")).toHaveValue("");
    });

    it("does not render the section when every canonical field was recovered", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(recoveredContract())) }));
      await screen.findByText(REVIEW_READY);

      expect(screen.queryByRole("heading", { name: "Not found in the document" })).toBeNull();
      expect(screen.queryByText(/Raffa could not find these in the file/)).toBeNull();
      expect(screen.getByRole("button", { name: "End date" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Supplier" })).toBeInTheDocument();
    });

    it("never gives a missing field a confidence tag", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText(REVIEW_READY);

      const heading = screen.getByRole("heading", { name: "Not found in the document" });
      const section = heading.closest("section");
      expect(section).not.toBeNull();
      expect(section!.querySelector(".tag")).toBeNull();
      expect(within(section!).queryByText(/%/)).toBeNull();
      expect(within(section!).queryByText("Needs review")).toBeNull();
    });

    it("filling a missing row calls the existing correction path and the value comes back from load(), not local state", async () => {
      let current = minimalContract();
      const getContract360 = vi.fn().mockImplementation(async () => ok(current));
      const correctContract = vi.fn().mockImplementation(
        async (_tenant: string, _id: string, payload: { corrections: Record<string, string | null>; reason: string | null }) => {
          current = {
            ...current,
            header: { ...current.header, endDate: payload.corrections.endDate ?? null },
          };
          return {
            ok: true,
            statusCode: 200,
            correction: {
              contractId: CONTRACT_ID,
              versionNumber: 2,
              correctedFields: ["endDate"],
              correctedAt: "2026-09-16T10:00:00Z",
            },
            error: null,
          } satisfies CorrectContractResult;
        },
      );

      renderReview(mockApiClient({ getContract360, correctContract }));
      await screen.findByText(REVIEW_READY);

      const endDate = screen.getByLabelText("End date");
      fireEvent.change(endDate, { target: { value: "2026-12-31" } });
      fireEvent.blur(endDate);

      await waitFor(() =>
        expect(correctContract).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID, {
          corrections: { endDate: "2026-12-31" },
          reason: null,
        }),
      );
      await waitFor(() => expect(getContract360).toHaveBeenCalledTimes(2));
      expect(await screen.findByRole("button", { name: "End date" })).toBeInTheDocument();
      expect(screen.queryByLabelText("End date")).toBeNull();
    });
  });
});
