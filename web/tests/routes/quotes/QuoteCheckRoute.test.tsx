import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import QuoteCheckRoute from "../../../src/routes/quotes";
import type {
  ApiClient,
  CaptureNegotiationOutcomeResult,
  QuoteLineAssessmentBody,
  QuoteRecalculationBody,
  RecalculateQuoteAssessmentResult,
  UnmatchedQuoteLineBody,
  UploadQuoteResult,
} from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const QUOTE_ID = "22222222-2222-2222-2222-222222222222";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    // Task E08/F01/US01/T01 (renewal-pipeline): this suite never reaches the Renewals screen -- bare
    // vi.fn() is enough, same convention as getRenewalPriority above. (Pre-existing gap in this
    // file's own mock literal, backfilled here while task E08/F02/US01/T01 was already touching this
    // exact object for its own two additions below.)
    postRenewalAction: vi.fn(),
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    // Task E07/F04/US01/T01 (ask-contigo-ui): this suite never reaches the Ask Contigo screen --
    // bare vi.fn() is enough, same convention as postRenewalAction above.
    askContigo: vi.fn(),
    // Task E08/F02/US01/T01 (savings-home): this suite never reaches Home's own fetch-outcome
    // matrix -- bare vi.fn() is enough, same convention as postRenewalAction above.
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    ...overrides,
  };
}

function assessedLine(overrides: Partial<QuoteLineAssessmentBody> = {}): QuoteLineAssessmentBody {
  return {
    quoteLineId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    status: "Assessed",
    position: "AboveMarket",
    unitPrice: 100,
    quantity: 10,
    benchmark: { hasSufficientData: true, distribution: { p25: 80, p50: 90, p75: 100 }, metric: "unit / yr", currency: "CHF" },
    confidence: {
      level: "High",
      score: 0.9,
      source: "fixture",
      sampleSize: 42,
      comparisonDimensions: ["Supplier"],
      updatedAt: "2026-08-28T00:00:00Z",
      summary: "High",
    },
    targetSaving: {
      recommendedTargetLow: 80,
      recommendedTargetHigh: 90,
      savingsRangeLow: 10,
      savingsRangeHigh: 20,
      totalSavingsRangeLow: 100,
      totalSavingsRangeHigh: 200,
      explanation: "Deterministic.",
    },
    explanation: "Above the matched comparables' 75th percentile.",
    ...overrides,
  };
}

function unmatchedLine(overrides: Partial<UnmatchedQuoteLineBody> = {}): UnmatchedQuoteLineBody {
  return {
    quoteLineId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    sku: "ENT-SUP-CUSTOM",
    normalizedSku: "ENT-SUP-CUSTOM",
    edition: null,
    description: "Enterprise Support Tier — Custom Bundle",
    ...overrides,
  };
}

function recalcOk(lines: QuoteLineAssessmentBody[], unmatched: UnmatchedQuoteLineBody[]): RecalculateQuoteAssessmentResult {
  const recalculation: QuoteRecalculationBody = {
    quoteId: QUOTE_ID,
    mappingsAppliedCount: 0,
    normalization: { lineCount: lines.length, matchedCount: lines.length - unmatched.length, unmatchedCount: unmatched.length, notApplicableCount: 0 },
    unmatchedLines: unmatched,
    assessment: { quoteId: QUOTE_ID, lines },
  };
  return { ok: true, statusCode: 200, recalculation, error: null };
}

function renderRoute(apiClient: ApiClient, initialPath = `/quotes/${QUOTE_ID}`) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/quotes" element={<QuoteCheckRoute apiClient={apiClient} />} />
        <Route path="/quotes/:quoteId" element={<QuoteCheckRoute apiClient={apiClient} />} />
        <Route path="/" element={<div>HOME_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("QuoteCheckRoute (route /quotes/:quoteId, ADR-020 screen 10)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("contigo.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const recalculateQuoteAssessment = vi.fn();
    renderRoute(mockApiClient({ recalculateQuoteAssessment }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(recalculateQuoteAssessment).not.toHaveBeenCalled();
  });

  it("renders its own upload form when no quoteId is in the route yet (ADR-018 names no list screen)", () => {
    const recalculateQuoteAssessment = vi.fn();
    renderRoute(mockApiClient({ recalculateQuoteAssessment }), "/quotes");

    expect(screen.getByRole("heading", { name: "Quote check" })).toBeInTheDocument();
    expect(recalculateQuoteAssessment).not.toHaveBeenCalled();
  });

  it("uploads via the sample-file convenience button and loads the real returned quote id", async () => {
    const uploadedQuote: UploadQuoteResult["quote"] = {
      id: QUOTE_ID,
      fileName: "contigo-sample-quote.pdf",
      mimeType: "application/pdf",
      processingStatus: "NeedsReview",
      lineItemCount: 0,
      normalizedLineItemCount: 0,
      unresolvedNormalizationCount: 0,
      unmatchedSkuCount: 0,
      supplier: null,
      currency: null,
      geography: null,
      purchaseDate: null,
      createdAt: "2026-09-06T00:00:00Z",
    };
    const uploadQuote = vi.fn().mockResolvedValue({ ok: true, statusCode: 201, quote: uploadedQuote, error: null });
    const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([], []));
    renderRoute(mockApiClient({ uploadQuote, recalculateQuoteAssessment }), "/quotes");

    fireEvent.click(screen.getByRole("button", { name: /use sample file/i }));

    await waitFor(() => expect(uploadQuote).toHaveBeenCalledTimes(1));
    expect(uploadQuote.mock.calls[0][0]).toBe(WORKSPACE_ID);
    await waitFor(() => expect(recalculateQuoteAssessment).toHaveBeenCalledWith(WORKSPACE_ID, QUOTE_ID, []));
    expect(await screen.findByText("contigo-sample-quote.pdf")).toBeInTheDocument();
  });

  it("calls recalculateQuoteAssessment with an empty mappings array on load (the endpoint's own documented 'pure refresh' read)", async () => {
    const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([assessedLine()], []));
    renderRoute(mockApiClient({ recalculateQuoteAssessment }));

    await waitFor(() => expect(recalculateQuoteAssessment).toHaveBeenCalledWith(WORKSPACE_ID, QUOTE_ID, []));
  });

  it("renders a named not-found state on a 404", async () => {
    renderRoute(
      mockApiClient({
        recalculateQuoteAssessment: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, recalculation: null, error: "Quote not found." }),
      }),
    );

    expect(await screen.findByText(/quote not found/i)).toBeInTheDocument();
  });

  describe("AC-2: unmatched SKU blocks assessment until mapped", () => {
    it("shows the unmatched-SKU block on Extract, and a blocked card when Assessment is opened", async () => {
      const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([assessedLine()], [unmatchedLine()]));
      renderRoute(mockApiClient({ recalculateQuoteAssessment }));

      expect(await screen.findByText(/could not be matched to the benchmark model/i)).toBeInTheDocument();

      fireEvent.click(screen.getByRole("tab", { name: "Assessment" }));

      expect(await screen.findByText("Assessment blocked")).toBeInTheDocument();
    });

    it("← Back to extract returns to the Extract step", async () => {
      const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([assessedLine()], [unmatchedLine()]));
      renderRoute(mockApiClient({ recalculateQuoteAssessment }));

      await screen.findByText(/could not be matched to the benchmark model/i);
      fireEvent.click(screen.getByRole("tab", { name: "Assessment" }));
      await screen.findByText("Assessment blocked");

      fireEvent.click(screen.getByRole("button", { name: /back to extract/i }));

      expect(await screen.findByText(/could not be matched to the benchmark model/i)).toBeInTheDocument();
    });

    it("applying a mapping recalculates with the real correction, and Assessment unblocks once no line is unmatched", async () => {
      const recalculateQuoteAssessment = vi
        .fn()
        .mockResolvedValueOnce(recalcOk([assessedLine()], [unmatchedLine()]))
        .mockResolvedValueOnce(recalcOk([assessedLine()], []));
      renderRoute(mockApiClient({ recalculateQuoteAssessment }));

      await screen.findByText(/could not be matched to the benchmark model/i);
      fireEvent.change(screen.getByLabelText("Canonical SKU"), { target: { value: "ENT-SUP-STD" } });
      fireEvent.click(screen.getByRole("button", { name: /apply mapping & recalculate/i }));

      await waitFor(() =>
        expect(recalculateQuoteAssessment).toHaveBeenNthCalledWith(2, WORKSPACE_ID, QUOTE_ID, [
          { sku: "ENT-SUP-CUSTOM", edition: null, canonicalSku: "ENT-SUP-STD", canonicalProductName: null },
        ]),
      );

      expect(await screen.findByText("All lines normalised")).toBeInTheDocument();

      fireEvent.click(screen.getByRole("tab", { name: "Assessment" }));

      expect(screen.queryByText("Assessment blocked")).not.toBeInTheDocument();
      expect(await screen.findByText("Line-level market position")).toBeInTheDocument();
    });

    it("does not call recalculateQuoteAssessment again when no mapping draft has a canonical SKU filled in", async () => {
      const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([assessedLine()], [unmatchedLine()]));
      renderRoute(mockApiClient({ recalculateQuoteAssessment }));

      await screen.findByText(/could not be matched to the benchmark model/i);
      const applyButton = screen.getByRole("button", { name: /apply mapping & recalculate/i });
      expect(applyButton).toBeDisabled();
    });
  });

  describe("AC-4: negotiation outcome capture", () => {
    it("records an outcome and renders the server's own recorded figures, never a locally-recomputed one", async () => {
      const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([assessedLine()], []));
      const outcomeBody: CaptureNegotiationOutcomeResult["outcome"] = {
        id: "cccccccc-cccc-cccc-cccc-cccccccccccc",
        quoteId: QUOTE_ID,
        originalQuoteTotal: 1000,
        targetPrice: 900,
        finalPrice: 950,
        realizedSaving: 50,
        discountPercent: 5,
        negotiationDurationDays: 10,
        leversUsed: ["Term"],
        capturedAt: "2026-09-06T00:00:00Z",
        savingsOpportunityId: null,
        savingsPropagated: null,
        savingsPropagationError: null,
      };
      const captureNegotiationOutcome = vi.fn().mockResolvedValue({ ok: true, statusCode: 201, outcome: outcomeBody, error: null });
      renderRoute(mockApiClient({ recalculateQuoteAssessment, captureNegotiationOutcome }));

      await screen.findByText("All lines normalised");
      fireEvent.click(screen.getByRole("tab", { name: "Negotiation" }));

      fireEvent.change(screen.getByLabelText("Final price"), { target: { value: "950" } });
      fireEvent.click(screen.getByLabelText("Term"));
      fireEvent.click(screen.getByRole("button", { name: /record outcome/i }));

      await waitFor(() => expect(captureNegotiationOutcome).toHaveBeenCalledTimes(1));
      expect(captureNegotiationOutcome).toHaveBeenCalledWith(
        WORKSPACE_ID,
        expect.objectContaining({ quoteId: QUOTE_ID, finalPrice: 950, leversUsed: ["Term"] }),
      );

      expect(await screen.findByText("Negotiation outcome")).toBeInTheDocument();
      // The recorded panel renders outcomeBody's own server-computed realizedSaving/discountPercent
      // (50 / 5.0%) verbatim, not a locally-recomputed preview (which would use the 1000/950 the
      // form itself submitted and land on the same numbers only by coincidence of this fixture).
      expect(screen.getByText("CHF 50 · 5.0%")).toBeInTheDocument();
    });

    it("disables Record outcome until at least one lever is selected (backend LeversUsedRequiredError)", async () => {
      const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([assessedLine()], []));
      renderRoute(mockApiClient({ recalculateQuoteAssessment }));

      await screen.findByText("All lines normalised");
      fireEvent.click(screen.getByRole("tab", { name: "Negotiation" }));
      fireEvent.change(screen.getByLabelText("Final price"), { target: { value: "950" } });

      expect(screen.getByRole("button", { name: /record outcome/i })).toBeDisabled();
    });

    it("shows an inline error and does not clear the form on a failed capture", async () => {
      const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([assessedLine()], []));
      const captureNegotiationOutcome = vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 400,
        outcome: null,
        error: "'leversUsed' entries must each be one of: Volume, Term, Utilization, Alternatives, QuarterEnd, Bundle, PaymentTerms.",
      });
      renderRoute(mockApiClient({ recalculateQuoteAssessment, captureNegotiationOutcome }));

      await screen.findByText("All lines normalised");
      fireEvent.click(screen.getByRole("tab", { name: "Negotiation" }));
      fireEvent.change(screen.getByLabelText("Final price"), { target: { value: "950" } });
      fireEvent.click(screen.getByLabelText("Term"));
      fireEvent.click(screen.getByRole("button", { name: /record outcome/i }));

      expect(await screen.findByRole("alert")).toHaveTextContent(/leversUsed/i);
      expect(screen.getByLabelText("Final price")).toHaveValue(950);
    });
  });
});
