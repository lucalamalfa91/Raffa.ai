import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import type { QuoteBenchmarkHistoryEntryBody, QuoteLineAssessmentBody } from "../../../api/client";
import QuoteHistoryList, { formatHistoryMeta, getQuoteHistoryPositionTag } from "./QuoteHistoryList";

/**
 * Task E25/F04/US02/T01 (quote-benchmark-web; parent story us-02-quote-benchmark-web AC-2; closes
 * NW-57). Proves "Quote check history" reads back every request from the server (never a
 * `sessionStorage` list), keeps the server's own newest-first order, re-opens `/quotes/:id`, and
 * never fabricates a position for a first-of-type entry.
 */

function assessedLine(overrides: Partial<QuoteLineAssessmentBody> = {}): QuoteLineAssessmentBody {
  return {
    quoteLineId: "line-1",
    status: "Assessed",
    position: "AboveMarket",
    unitPrice: 10,
    quantity: 1,
    benchmark: { hasSufficientData: true, distribution: { p25: 8, p50: 9, p75: 10 }, metric: "unit", currency: "USD" },
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
    ...overrides,
  };
}

function coldStartLine(): QuoteLineAssessmentBody {
  return assessedLine({
    status: "InsufficientBenchmarkData",
    position: null,
    benchmark: { hasSufficientData: false, distribution: null, metric: "unit", currency: "USD" },
    confidence: null,
  });
}

function entry(overrides: Partial<QuoteBenchmarkHistoryEntryBody> = {}): QuoteBenchmarkHistoryEntryBody {
  return {
    id: "q-1",
    fileName: "Databricks_Proposal_Q-88213.pdf",
    mimeType: "application/pdf",
    processingStatus: "Completed",
    supplier: "Databricks",
    currency: "CHF",
    geography: "CH",
    purchaseDate: "2026-08-01",
    createdAt: "2026-09-01T12:00:00Z",
    lines: [assessedLine()],
    ...overrides,
  };
}

function renderList(entries: readonly QuoteBenchmarkHistoryEntryBody[]) {
  return render(
    <MemoryRouter>
      <QuoteHistoryList entries={entries} />
    </MemoryRouter>,
  );
}

describe("formatHistoryMeta", () => {
  it("joins supplier, currency, geography and the checked-in date", () => {
    expect(formatHistoryMeta(entry())).toBe("Databricks · CHF · CH · 01/09/2026");
  });

  it("drops a null segment without leaving a gap, a dash, or a placeholder", () => {
    expect(formatHistoryMeta(entry({ supplier: null }))).toBe("CHF · CH · 01/09/2026");
    expect(formatHistoryMeta(entry({ supplier: null, currency: null, geography: null }))).toBe("01/09/2026");
  });
});

describe("getQuoteHistoryPositionTag", () => {
  it("a real tally for an assessed entry, never a placeholder", () => {
    expect(getQuoteHistoryPositionTag(entry({ lines: [assessedLine()] }))).toEqual({
      variant: "neutral",
      label: "1 above market",
    });
  });

  it("the same honest cold-start label AssessmentResult uses -- never a fabricated position", () => {
    expect(getQuoteHistoryPositionTag(entry({ lines: [coldStartLine()] }))).toEqual({
      variant: "outline",
      label: "First of its kind",
    });
  });
});

describe("QuoteHistoryList", () => {
  it("AC-2: an honest empty state when the workspace has never checked a quote", () => {
    renderList([]);
    expect(screen.getByRole("status")).toHaveTextContent(/no quote checks yet/i);
  });

  it("AC-2: renders every entry the server returned, in the given (newest-first) order, never re-sorted", () => {
    const older = entry({ id: "q-old", fileName: "older.pdf", createdAt: "2026-08-01T00:00:00Z" });
    const newer = entry({ id: "q-new", fileName: "newer.pdf", createdAt: "2026-09-01T00:00:00Z" });
    renderList([newer, older]);

    const rows = screen.getAllByRole("row").slice(1); // row 0 is the header row
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent("newer.pdf");
    expect(rows[1]).toHaveTextContent("older.pdf");
  });

  it("each row re-opens the same quote's own benchmark, never a copy", () => {
    renderList([entry({ id: "q-42" })]);
    expect(screen.getByRole("link")).toHaveAttribute("href", "/quotes/q-42");
  });

  it("a first-of-type entry reads the same honest label as AssessmentResult, never a fabricated position", () => {
    renderList([entry({ lines: [coldStartLine()] })]);
    expect(screen.getByText("First of its kind")).toBeInTheDocument();
  });

  it("a quote still processing (no lines extracted yet) is neither a cold start nor a fabricated tally", () => {
    renderList([entry({ lines: [] })]);
    expect(screen.getByText("Not yet assessed")).toBeInTheDocument();
  });
});
