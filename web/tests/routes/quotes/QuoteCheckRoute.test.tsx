import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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
    inviteWorkspaceMember: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn(),
    validateDocument: vi.fn(),
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
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
        <Route path="/savings" element={<div>SAVINGS_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

const LEVERS_FOOTER = "Target and negotiation levers are one step further — shown only if you want them.";

describe("QuoteCheckRoute (V2, ADR-024 / screens-v2.md #9)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }));
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const recalculateQuoteAssessment = vi.fn();
    renderRoute(mockApiClient({ recalculateQuoteAssessment }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(recalculateQuoteAssessment).not.toHaveBeenCalled();
  });

  it("landing: the V2 header, the dashed drop card with 'Upload a quote' and the sample, and no stepper", () => {
    const recalculateQuoteAssessment = vi.fn();
    renderRoute(mockApiClient({ recalculateQuoteAssessment }), "/quotes");

    expect(screen.getByText("Optional · new purchase")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Quote check" })).toBeInTheDocument();
    expect(
      screen.getByText("Drop a supplier proposal; Raffa normalises the lines and compares them with the market and with what you already pay."),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Upload a quote" })).toHaveClass("btn-primary");
    expect(screen.getByText(/or use the sample:/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Databricks proposal Q-88213" })).toBeInTheDocument();
    expect(screen.queryByRole("tab")).not.toBeInTheDocument();
    expect(recalculateQuoteAssessment).not.toHaveBeenCalled();
  });

  it("uploads the sample proposal as a real file with its own metadata, then loads the returned quote id", async () => {
    const uploadedQuote: UploadQuoteResult["quote"] = {
      id: QUOTE_ID,
      fileName: "Databricks_Proposal_Q-88213.pdf",
      mimeType: "application/pdf",
      processingStatus: "NeedsReview",
      lineItemCount: 3,
      normalizedLineItemCount: 3,
      unresolvedNormalizationCount: 0,
      unmatchedSkuCount: 0,
      supplier: "Databricks",
      currency: "CHF",
      geography: "CH",
      purchaseDate: null,
      createdAt: "2026-09-06T00:00:00Z",
    };
    const uploadQuote = vi.fn().mockResolvedValue({ ok: true, statusCode: 201, quote: uploadedQuote, error: null });
    const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([], []));
    renderRoute(mockApiClient({ uploadQuote, recalculateQuoteAssessment }), "/quotes");

    fireEvent.click(screen.getByRole("button", { name: "Databricks proposal Q-88213" }));

    await waitFor(() => expect(uploadQuote).toHaveBeenCalledTimes(1));
    const [tenantId, file, fields] = uploadQuote.mock.calls[0];
    expect(tenantId).toBe(WORKSPACE_ID);
    expect((file as File).name).toBe("Databricks_Proposal_Q-88213.pdf");
    expect((file as File).size).toBeGreaterThan(200);
    expect(fields).toMatchObject({ supplier: "Databricks", currency: "CHF", geography: "CH" });

    await waitFor(() => expect(recalculateQuoteAssessment).toHaveBeenCalledWith(WORKSPACE_ID, QUOTE_ID, []));
    expect(await screen.findByText("Databricks_Proposal_Q-88213.pdf · Databricks · CHF · CH")).toBeInTheDocument();
    // Zero extracted lines is an honest answer, never a scripted table.
    expect(screen.getByText(/no line items were extracted from this quote yet/i)).toBeInTheDocument();
  });

  it("uploads a picked file straight away with the optional metadata typed under the disclosure", async () => {
    const uploadQuote = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, quote: null, error: "Unsupported file type." });
    renderRoute(mockApiClient({ uploadQuote }), "/quotes");

    fireEvent.change(screen.getByLabelText("Supplier"), { target: { value: "Acme" } });
    const file = new File(["%PDF-1.4"], "proposal.pdf", { type: "application/pdf" });
    fireEvent.change(screen.getByLabelText("Quote file"), { target: { files: [file] } });

    await waitFor(() => expect(uploadQuote).toHaveBeenCalledTimes(1));
    expect(uploadQuote.mock.calls[0][2]).toEqual({ supplier: "Acme", currency: undefined, geography: undefined, purchaseDate: undefined });
    expect(await screen.findByRole("alert")).toHaveTextContent("Unsupported file type.");
  });

  it("calls recalculateQuoteAssessment with an empty mappings array on load (the endpoint's own documented 'pure refresh' read)", async () => {
    const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([assessedLine()], []));
    renderRoute(mockApiClient({ recalculateQuoteAssessment }));

    await waitFor(() => expect(recalculateQuoteAssessment).toHaveBeenCalledWith(WORKSPACE_ID, QUOTE_ID, []));
  });

  it("renders a named not-found state on a 404, under the same header", async () => {
    renderRoute(
      mockApiClient({
        recalculateQuoteAssessment: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, recalculation: null, error: "Quote not found." }),
      }),
    );

    expect(await screen.findByText("Quote not found")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Quote check" })).toBeInTheDocument();
  });

  describe("once loaded", () => {
    it("renders the three-cell band, the five-column lines table and the levers footer", async () => {
      renderRoute(mockApiClient({ recalculateQuoteAssessment: vi.fn().mockResolvedValue(recalcOk([assessedLine()], [])) }));

      const table = await screen.findByRole("table");
      expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
        "Line",
        "Quoted",
        "P50",
        "Position",
        "Benchmark",
      ]);
      const row = within(table).getAllByRole("row")[1];
      expect(within(row).getByText("Line 1")).toBeInTheDocument();
      expect(within(row).getByText("CHF 100")).toBeInTheDocument();
      expect(within(row).getByText("CHF 90")).toBeInTheDocument();
      expect(within(row).getByText("Above market")).toHaveClass("tag-accent");
      expect(within(row).getByText("+11% vs P50")).toBeInTheDocument();
      expect(within(row).getByText("High · n=42")).toHaveClass("tag-neutral");

      const band = screen.getByRole("region", { name: "Quote assessment" });
      expect(within(band).getByText("Supplier quote").nextSibling).toHaveTextContent("CHF 1,000");
      expect(within(band).getByText("Market range").nextSibling).toHaveTextContent("CHF 800–1,000");
      expect(within(band).getByText("Assessment").nextSibling).toHaveTextContent("1 above market");
      expect(within(band).getByText("1 above market")).toHaveClass("quote-emphasize");

      expect(screen.getByText(LEVERS_FOOTER)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Show target and levers →" })).toHaveAttribute("aria-expanded", "false");
      expect(screen.queryByText("Adjust target")).not.toBeInTheDocument();
    });

    it("keeps unit prices' decimals (a 0.55 DBU is not 'CHF 1')", async () => {
      renderRoute(
        mockApiClient({
          recalculateQuoteAssessment: vi.fn().mockResolvedValue(
            recalcOk(
              [assessedLine({ unitPrice: 0.55, quantity: 600_000, benchmark: { hasSufficientData: true, distribution: { p25: 0.4, p50: 0.46, p75: 0.5 }, metric: "DBU", currency: "CHF" } })],
              [],
            ),
          ),
        }),
      );

      const table = await screen.findByRole("table");
      expect(within(table).getByText("CHF 0.55")).toBeInTheDocument();
      expect(within(table).getByText("CHF 0.46")).toBeInTheDocument();
      expect(within(table).getByText("+20% vs P50")).toBeInTheDocument();
    });

    it("the footer reveals the Target step, whose own continue reveals Negotiation; the toggle hides both again", async () => {
      renderRoute(mockApiClient({ recalculateQuoteAssessment: vi.fn().mockResolvedValue(recalcOk([assessedLine()], [])) }));
      await screen.findByText(LEVERS_FOOTER);

      fireEvent.click(screen.getByRole("button", { name: "Show target and levers →" }));
      expect(await screen.findByText("Adjust target")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Hide target and levers" })).toHaveAttribute("aria-expanded", "true");
      expect(screen.queryByText("Record the outcome")).not.toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: /build negotiation strategy/i }));
      expect(await screen.findByText("Record the outcome")).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "Hide target and levers" }));
      expect(screen.queryByText("Adjust target")).not.toBeInTheDocument();
      expect(screen.queryByText("Record the outcome")).not.toBeInTheDocument();
    });
  });

  describe("unmatched SKU blocks assessment until mapped", () => {
    it("shows the mapping block instead of the levers footer, and marks the unmatched line 'Needs mapping'", async () => {
      const recalculateQuoteAssessment = vi
        .fn()
        .mockResolvedValue(recalcOk([assessedLine(), assessedLine({ quoteLineId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", benchmark: null, position: null, status: "QuoteDataUnresolved", confidence: null })], [unmatchedLine()]));
      renderRoute(mockApiClient({ recalculateQuoteAssessment }));

      expect(await screen.findByText(/could not be matched to the benchmark model/i)).toBeInTheDocument();
      expect(screen.queryByText(LEVERS_FOOTER)).not.toBeInTheDocument();

      const table = screen.getByRole("table");
      expect(within(table).getByText("Enterprise Support Tier — Custom Bundle")).toBeInTheDocument();
      expect(within(table).getByText("Needs mapping")).toHaveClass("tag-outline");
      expect(within(table).getByText("Enterprise Support Tier — Custom Bundle").closest("tr")).toHaveClass("row-critical");
    });

    it("applying a mapping recalculates with the real correction; once no line is unmatched the levers footer appears", async () => {
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

      expect(await screen.findByText(LEVERS_FOOTER)).toBeInTheDocument();
      expect(screen.queryByText(/could not be matched to the benchmark model/i)).not.toBeInTheDocument();
    });

    it("does not call recalculateQuoteAssessment again when no mapping draft has a canonical SKU filled in", async () => {
      const recalculateQuoteAssessment = vi.fn().mockResolvedValue(recalcOk([assessedLine()], [unmatchedLine()]));
      renderRoute(mockApiClient({ recalculateQuoteAssessment }));

      await screen.findByText(/could not be matched to the benchmark model/i);
      expect(screen.getByRole("button", { name: /apply mapping & recalculate/i })).toBeDisabled();
      expect(recalculateQuoteAssessment).toHaveBeenCalledTimes(1);
    });
  });

  describe("negotiation outcome capture (one step further)", () => {
    async function openNegotiation() {
      await screen.findByText(LEVERS_FOOTER);
      fireEvent.click(screen.getByRole("button", { name: "Show target and levers →" }));
      fireEvent.click(await screen.findByRole("button", { name: /build negotiation strategy/i }));
      await screen.findByText("Record the outcome");
    }

    it("records an outcome and renders the server's own recorded figures, then links to Savings", async () => {
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
      await openNegotiation();

      fireEvent.change(screen.getByLabelText("Final price"), { target: { value: "950" } });
      fireEvent.click(screen.getByLabelText("Term"));
      fireEvent.click(screen.getByRole("button", { name: /record outcome/i }));

      await waitFor(() => expect(captureNegotiationOutcome).toHaveBeenCalledTimes(1));
      expect(captureNegotiationOutcome).toHaveBeenCalledWith(
        WORKSPACE_ID,
        expect.objectContaining({ quoteId: QUOTE_ID, finalPrice: 950, leversUsed: ["Term"] }),
      );

      expect(await screen.findByText("Negotiation outcome")).toBeInTheDocument();
      expect(screen.getByText("CHF 50 · 5.0%")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "See it in Savings →" })).toHaveAttribute("href", "/savings");
    });

    it("disables Record outcome until at least one lever is selected (backend LeversUsedRequiredError)", async () => {
      renderRoute(mockApiClient({ recalculateQuoteAssessment: vi.fn().mockResolvedValue(recalcOk([assessedLine()], [])) }));
      await openNegotiation();

      fireEvent.change(screen.getByLabelText("Final price"), { target: { value: "950" } });
      expect(screen.getByRole("button", { name: /record outcome/i })).toBeDisabled();
    });

    it("shows an inline error and does not clear the form on a failed capture", async () => {
      const captureNegotiationOutcome = vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 400,
        outcome: null,
        error: "'leversUsed' entries must each be one of: Volume, Term, Utilization, Alternatives, QuarterEnd, Bundle, PaymentTerms.",
      });
      renderRoute(mockApiClient({ recalculateQuoteAssessment: vi.fn().mockResolvedValue(recalcOk([assessedLine()], [])), captureNegotiationOutcome }));
      await openNegotiation();

      fireEvent.change(screen.getByLabelText("Final price"), { target: { value: "950" } });
      fireEvent.click(screen.getByLabelText("Term"));
      fireEvent.click(screen.getByRole("button", { name: /record outcome/i }));

      expect(await screen.findByRole("alert")).toHaveTextContent(/leversUsed/i);
      expect(screen.getByLabelText("Final price")).toHaveValue(950);
    });
  });
});
