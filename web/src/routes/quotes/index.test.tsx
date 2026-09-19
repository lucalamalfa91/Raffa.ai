import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ApiClient, QuoteLineAssessmentBody } from "../../api/client";
import QuoteCheckRoute from "./index";

/**
 * Task E25/F04/US02/T01 (quote-benchmark-web; parent story us-02-quote-benchmark-web AC-2/AC-3;
 * closes NW-57). Two things a pure-function test cannot prove on their own: that the landing wires
 * `getQuoteBenchmarkHistory` into `QuoteHistoryList` (AC-2), and that the "See it in Savings →" CTA
 * never replaces the benchmark result -- both are on screen together once the negotiation panel
 * renders an already-recorded outcome (AC-3).
 */

const ASSESSED_LINE: QuoteLineAssessmentBody = {
  quoteLineId: "line-1",
  status: "Assessed",
  position: "AboveMarket",
  unitPrice: 100,
  quantity: 10,
  benchmark: { hasSufficientData: true, distribution: { p25: 80, p50: 90, p75: 100 }, metric: "unit", currency: "USD" },
  confidence: {
    level: "High",
    score: 0.9,
    source: "fixture",
    sampleSize: 10,
    comparisonDimensions: [],
    updatedAt: "2026-09-01T00:00:00Z",
    summary: "",
  },
  targetSaving: null,
  explanation: "",
};

function apiClientWith(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    recalculateQuoteAssessment: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      recalculation: {
        quoteId: "q-1",
        mappingsAppliedCount: 0,
        normalization: { lineCount: 1, matchedCount: 1, unmatchedCount: 0, notApplicableCount: 0 },
        unmatchedLines: [],
        assessment: { quoteId: "q-1", lines: [ASSESSED_LINE] },
      },
      error: null,
    }),
    getQuote: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      quote: {
        id: "q-1",
        fileName: "quote.pdf",
        mimeType: "application/pdf",
        processingStatus: "Completed",
        supplier: "Acme",
        currency: "USD",
        geography: "US",
        purchaseDate: null,
        createdAt: "2026-09-01T00:00:00Z",
        outcomes: [
          {
            id: "outcome-1",
            originalQuoteTotal: 1000,
            targetPrice: 900,
            finalPrice: 950,
            realizedSaving: 50,
            discountPercent: 5,
            negotiationDurationDays: 10,
            leversUsed: ["Volume"],
            capturedAt: "2026-09-02T00:00:00Z",
            savingsOpportunityId: null,
          },
        ],
      },
      error: null,
    }),
    getQuoteBenchmarkHistory: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, history: { items: [] }, error: null }),
    uploadQuote: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    ...overrides,
  } as unknown as ApiClient;
}

beforeEach(() => {
  window.sessionStorage.clear();
  window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: "w-1", name: "Acme Co" }));
});

describe("QuoteCheckRoute", () => {
  it("AC-3: the benchmark result stays on screen once the negotiation CTA appears -- it never replaces it", async () => {
    const apiClient = apiClientWith();
    render(
      <MemoryRouter initialEntries={["/quotes/q-1"]}>
        <Routes>
          <Route path="/quotes/:quoteId" element={<QuoteCheckRoute apiClient={apiClient} />} />
        </Routes>
      </MemoryRouter>,
    );

    // The benchmark-first result (AssessmentResult), rendered before any levers/negotiation UI exists.
    await screen.findByText("Supplier quote");
    expect(screen.getByText("1 above market")).toBeInTheDocument();

    await userEvent.click(await screen.findByRole("button", { name: /show target and levers/i }));
    await userEvent.click(await screen.findByRole("button", { name: /build negotiation strategy/i }));

    const savingsCta = await screen.findByRole("link", { name: /see it in savings/i });
    expect(savingsCta).toHaveAttribute("href", "/savings");
    expect(savingsCta).toHaveClass("btn-secondary"); // secondary, never the primary action on this screen

    // Still there, unreplaced, exactly as before the CTA appeared.
    expect(screen.getByText("Supplier quote")).toBeInTheDocument();
    expect(screen.getByText("1 above market")).toBeInTheDocument();
  });

  it("AC-2: the landing renders quote check history read back from the server, not a client store", async () => {
    const apiClient = apiClientWith({
      getQuoteBenchmarkHistory: vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        history: {
          items: [
            {
              id: "q-old",
              fileName: "older-quote.pdf",
              mimeType: "application/pdf",
              processingStatus: "Completed",
              supplier: "Acme",
              currency: "USD",
              geography: "US",
              purchaseDate: null,
              createdAt: "2026-08-01T00:00:00Z",
              lines: [ASSESSED_LINE],
            },
          ],
        },
        error: null,
      }),
    });

    render(
      <MemoryRouter initialEntries={["/quotes"]}>
        <Routes>
          <Route path="/quotes" element={<QuoteCheckRoute apiClient={apiClient} />} />
          <Route path="/quotes/:quoteId" element={<QuoteCheckRoute apiClient={apiClient} />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText("older-quote.pdf")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 6, name: "Quote check history" })).toBeInTheDocument();
    expect(screen.getByRole("table")).toHaveClass("table");
    expect(screen.getByRole("link", { name: /older-quote\.pdf/i })).toHaveAttribute("href", "/quotes/q-old");
  });
});
