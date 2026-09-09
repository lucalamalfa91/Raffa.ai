import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import ReviewRoute from "../../../../src/routes/contracts/review";
import type {
  ApiClient,
  Contract360Body,
  CorrectContractResult,
  CorrectionHistoryEntryBody,
  GetContract360Result,
  GetCorrectionHistoryResult,
} from "../../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const CONTRACT_ID = "22222222-2222-2222-2222-222222222222";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    // Task E13/F09/US01/T03 (web-documents-v2): this suite does not exercise Documents -- bare
    // vi.fn() is enough, same convention as getPortfolio below.
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, history: [], error: null }),
    correctContract: vi.fn(),
    // Task E08/F01/US01/T01 (renewal-pipeline): this suite never reaches the Renewals screen --
    // bare vi.fn() is enough, same convention as the other calls above.
    postRenewalAction: vi.fn(),
    // Task E08/F03/US01/T01 (quote-check-ui): this suite never reaches the Quote Check screen --
    // bare vi.fn() is enough, same convention as getRenewalPriority above.
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
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
    header: {
      contractId: CONTRACT_ID,
      supplierId: null,
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
      documents: [],
      benchmark: [],
      renewal: { endDate: null, renewalDate: null, cancellationDeadline: null, autoRenewal: true, renewalTermMonths: null },
      activity: [],
    },
    ...overrides,
  };
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
    window.sessionStorage.setItem("contigo.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
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

    expect(await screen.findByText("Review extraction")).toBeInTheDocument();
    expect(container.querySelector(".review-skeleton")).not.toBeInTheDocument();
  });

  it("calls getContract360 then getCorrectionHistory with the current workspace id and the route's contractId", async () => {
    const getContract360 = vi.fn().mockResolvedValue(ok(minimalContract()));
    const getCorrectionHistory = vi.fn().mockResolvedValue(historyOk([]));
    renderReview(mockApiClient({ getContract360, getCorrectionHistory }));

    await screen.findByText("Review extraction");

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

    expect(await screen.findByText("Review extraction")).toBeInTheDocument();
    expect(getContract360).toHaveBeenCalledTimes(2);
  });

  describe("once populated (AC-1/AC-2)", () => {
    it("renders the 4 column headers and one row per correctable field that has a value", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(fullContract())) }));
      await screen.findByText("Review extraction");

      const headerRow = screen.getAllByRole("columnheader").map((cell) => cell.textContent);
      expect(headerRow).toEqual(["Field", "Extracted value", "Confidence", "Decision"]);

      expect(screen.getByRole("button", { name: "Contract type" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Annual spend" })).toBeInTheDocument();
      expect(screen.getByText("500,000")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Auto-renewal" })).toBeInTheDocument();
      expect(screen.getByText("Yes")).toBeInTheDocument();
    });

    it("does not render a row for a correctable field that was never extracted (null on the contract)", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText("Review extraction");

      // minimalContract() leaves governingLaw/paymentTerms/dates/spend null -- absent, never a "—" row.
      expect(screen.queryByRole("button", { name: "Governing law" })).toBeNull();
      expect(screen.queryByRole("button", { name: "Payment terms" })).toBeNull();
      // The four required fields still render.
      expect(screen.getByRole("button", { name: "Contract type" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Status" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Currency" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Auto-renewal" })).toBeInTheDocument();
    });

    it("shows an honest 'Needs review' tag for a pending field, never a fabricated confidence percentage", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText("Review extraction");

      expect(screen.getAllByText("Needs review").length).toBeGreaterThan(0);
      // Scoped to the field table, not the whole screen: task E11/F07/US01/T01's header legend
      // legitimately shows the static >95%/80-95%/<80% thresholds (design-system.md's own semantic
      // mapping, not a per-field score) right next to the progress line -- this assertion's real
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
      await screen.findByText("Review extraction");

      expect(screen.getByText("Corrected → 500000")).toBeInTheDocument();
      expect(screen.getAllByText("Corrected").length).toBeGreaterThan(0);
    });
  });

  describe("AC-4: gated 'Mark as validated'", () => {
    it("is disabled with a visible reason while any field is pending", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText("Review extraction");

      const cta = screen.getByRole("button", { name: /mark as validated/i });
      expect(cta).toBeDisabled();
      expect(screen.getByText(/still needs? review before this contract can be marked validated/i)).toBeInTheDocument();
    });

    it("becomes enabled once every field is Accepted, and navigates to Contract 360 on click", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText("Review extraction");

      for (const button of screen.getAllByRole("button", { name: "Accept" })) {
        fireEvent.click(button);
      }

      const cta = screen.getByRole("button", { name: /mark as validated/i });
      expect(cta).toBeEnabled();

      fireEvent.click(cta);
      expect(await screen.findByText("CONTRACT_360_SCREEN")).toBeInTheDocument();
    });

    it("a field with real correction history does not block the gate on its own", async () => {
      renderReview(
        mockApiClient({
          getContract360: vi.fn().mockResolvedValue(ok(minimalContract())),
          getCorrectionHistory: vi
            .fn()
            .mockResolvedValue(historyOk([correctionEntry({ fieldName: "type", previousValue: "Sow", newValue: "Msa" })])),
        }),
      );
      await screen.findByText("Review extraction");

      // 3 of the 4 fields (status/currency/autoRenewal) are still pending -- the 4th (type) is
      // resolved by real history, so accepting only those three should unblock the gate.
      for (const button of screen.getAllByRole("button", { name: "Accept" })) {
        fireEvent.click(button);
      }

      expect(screen.getByRole("button", { name: /mark as validated/i })).toBeEnabled();
    });
  });

  describe("AC-3: evidence pane + correction form", () => {
    it("selecting a field opens the evidence pane pre-filled with its current value", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(fullContract())) }));
      await screen.findByText("Review extraction");

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
      const correctContract = vi.fn().mockResolvedValue(correctionResult);
      const getContract360 = vi.fn().mockResolvedValue(ok(fullContract()));

      renderReview(mockApiClient({ getContract360, correctContract }));
      await screen.findByText("Review extraction");

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
      // A successful correction re-fetches the contract for real, rather than trusting a locally
      // patched copy -- the same "reload, don't guess" convention ../contract360/index.tsx follows.
      // (async: the correction's own `await` and the subsequent `load()` call both resolve after
      // this click handler yields, so this assertion must poll rather than check immediately.)
      await waitFor(() => expect(getContract360).toHaveBeenCalledTimes(2));
    });

    it("shows an inline error and does not clear the form on a failed correction (e.g. a rejected no-op)", async () => {
      const correctContract = vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 400,
        correction: null,
        error: "None of the supplied values differ from the contract's current values.",
      });
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(fullContract())), correctContract }));
      await screen.findByText("Review extraction");

      fireEvent.click(screen.getByRole("button", { name: "Payment terms" }));
      fireEvent.click(screen.getByRole("button", { name: /save correction/i }));

      expect(await screen.findByRole("alert")).toHaveTextContent(/none of the supplied values differ/i);
      expect(screen.getByLabelText("Correct value")).toHaveValue("Net 45");
    });

    it("Accept requires no form -- it is a direct, one-click decision from the list", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText("Review extraction");

      const acceptButtons = within(screen.getByRole("table")).getAllByRole("button", { name: "Accept" });
      fireEvent.click(acceptButtons[0]);

      expect(screen.getAllByText("Accepted by you").length).toBeGreaterThan(0);
    });
  });

  describe("Visual fidelity (E11/F07/US01/T01, gap G-REV)", () => {
    it("shows the confidence legend next to the progress line, copied verbatim from the export", async () => {
      renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(minimalContract())) }));
      await screen.findByText("Review extraction");

      expect(screen.getByText("auto-accepted")).toBeInTheDocument();
      expect(screen.getByText("flagged")).toBeInTheDocument();
      expect(screen.getByText("review required")).toBeInTheDocument();
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

    it("wraps the evidence pane's honest 'not yet available' note in the highlighted-passage card", async () => {
      const { container } = renderReview(mockApiClient({ getContract360: vi.fn().mockResolvedValue(ok(fullContract())) }));
      await screen.findByText("Review extraction");

      fireEvent.click(screen.getByRole("button", { name: "Payment terms" }));

      expect(await screen.findByText(/source passage not yet available/i)).toBeInTheDocument();
      expect(container.querySelector(".review-evidence-passage")).toBeInTheDocument();
    });
  });
});
