import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import type { QuoteLineAssessmentBody } from "../../../api/client";
import { aggregateQuote, buildExtractRows, buildQuoteLineRows } from "../quoteCheckViewModel";
import AssessmentResult, { isQuoteBenchmarkColdStart } from "./AssessmentResult";

/**
 * Task E25/F04/US02/T01 (quote-benchmark-web; parent story us-02-quote-benchmark-web AC-1; closes
 * NW-57). Proves the market-benchmark result renders benchmark-first from a real
 * `recalculateQuoteAssessment`-shaped line, and that a genuine cold start (every line
 * `InsufficientBenchmarkData`) shows the honest copy NW-57 must #3 asks for -- never a fabricated
 * number.
 */

function line(overrides: Partial<QuoteLineAssessmentBody> = {}): QuoteLineAssessmentBody {
  return {
    quoteLineId: "line-1",
    status: "Assessed",
    position: "AboveMarket",
    unitPrice: 100,
    quantity: 10,
    benchmark: { hasSufficientData: true, distribution: { p25: 80, p50: 90, p75: 100 }, metric: "unit", currency: "USD" },
    confidence: {
      level: "High",
      score: 0.95,
      source: "fixture",
      sampleSize: 42,
      comparisonDimensions: [],
      updatedAt: "2026-09-01T00:00:00Z",
      summary: "",
    },
    targetSaving: {
      recommendedTargetLow: 85,
      recommendedTargetHigh: 92,
      savingsRangeLow: 8,
      savingsRangeHigh: 15,
      totalSavingsRangeLow: 80,
      totalSavingsRangeHigh: 150,
      explanation: "",
    },
    explanation: "",
    ...overrides,
  };
}

/** The honest cold-start shape `MarketAssessmentService` itself returns for a first-of-type line
 * (`QuoteBenchmarkHistoryEndpointTests.First_of_type_quote_reports_an_honest_insufficient_benchmark_data_cold_start`):
 * `status: InsufficientBenchmarkData`, a null position, and a `benchmark` that still reports
 * `hasSufficientData: false` with no distribution -- confidence/targetSaving are never populated
 * without a position to hang them off. */
function coldStartLine(overrides: Partial<QuoteLineAssessmentBody> = {}): QuoteLineAssessmentBody {
  return line({
    status: "InsufficientBenchmarkData",
    position: null,
    benchmark: { hasSufficientData: false, distribution: null, metric: "unit", currency: "USD" },
    confidence: null,
    targetSaving: null,
    ...overrides,
  });
}

/** Renders with the exact same derivation pipeline `../index.tsx` uses (`aggregateQuote` ->
 * `buildExtractRows` -> `buildQuoteLineRows`), never a hand-built `QuoteLineRow`, so this test only
 * proves what a real render would show. */
function renderResult(lines: readonly QuoteLineAssessmentBody[]) {
  const aggregate = aggregateQuote(lines);
  const extractRows = buildExtractRows(lines, [], new Map());
  const lineRows = buildQuoteLineRows(extractRows, lines);
  return render(<AssessmentResult aggregate={aggregate} lines={lines} lineRows={lineRows} />);
}

describe("isQuoteBenchmarkColdStart", () => {
  it("false for a quote with no lines yet -- nothing extracted is not a cold start", () => {
    expect(isQuoteBenchmarkColdStart([])).toBe(false);
  });

  it("true only when every line reports the honest InsufficientBenchmarkData status", () => {
    expect(isQuoteBenchmarkColdStart([coldStartLine()])).toBe(true);
  });

  it("false when even one line was actually assessed -- not a full cold start", () => {
    expect(isQuoteBenchmarkColdStart([coldStartLine({ quoteLineId: "a" }), line({ quoteLineId: "b" })])).toBe(false);
  });

  it("false for QuoteDataUnresolved -- a different, quote-side gap, never mislabelled a cold start", () => {
    expect(
      isQuoteBenchmarkColdStart([
        line({ status: "QuoteDataUnresolved", position: null, benchmark: null, confidence: null, targetSaving: null }),
      ]),
    ).toBe(false);
  });
});

describe("AssessmentResult", () => {
  it("renders the real benchmark-first band and line for an assessed quote, no cold-start copy", () => {
    renderResult([line()]);

    expect(screen.getByText("Supplier quote")).toBeInTheDocument();
    expect(screen.getByText("Market range")).toBeInTheDocument();
    // The band's own tally (`summarizePositions`), never a synthesized single word.
    expect(screen.getByText("1 above market")).toBeInTheDocument();
    // The line's own position tag.
    expect(screen.getByText("Above market")).toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("AC-1: a genuine cold start shows the honest copy, and nothing on screen fabricates a number", () => {
    renderResult([coldStartLine()]);

    const coldStart = screen.getByRole("status");
    expect(coldStart).toHaveTextContent(/first of its kind/i);
    // Honest, never a fabricated figure: the cold-start copy itself carries no digit.
    expect(coldStart.textContent).not.toMatch(/\d/);

    // The band's own cells stay honest too -- no invented market range or position, band and line
    // table both agree (each contributes one occurrence of each honest label).
    expect(screen.getAllByText("Not yet available").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Not yet assessed").length).toBeGreaterThan(0);
  });

  it("a partially-assessed quote (one real line, one insufficient) is not treated as a cold start", () => {
    renderResult([coldStartLine({ quoteLineId: "a" }), line({ quoteLineId: "b", position: "BelowMarket" })]);

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.getByText("1 below market")).toBeInTheDocument();
  });
});
