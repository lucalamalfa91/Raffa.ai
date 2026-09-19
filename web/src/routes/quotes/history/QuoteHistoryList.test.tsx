import { describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { QuoteBenchmarkHistoryEntryBody, QuoteLineAssessmentBody } from "../../../api/client";
import QuoteHistoryList, { formatHistoryMeta, getQuoteHistoryPositionTag } from "./QuoteHistoryList";

/**
 * Task E25/F04/US02/T01 (quote-benchmark-web; parent story us-02-quote-benchmark-web AC-2; closes
 * NW-57). Proves "Quote check history" reads back every request from the server (never a
 * `sessionStorage` list), keeps the server's own newest-first order, re-opens `/quotes/:id`, and
 * never fabricates a position for a first-of-type entry. Also proves the list uses the same locked
 * `.table` catalogue as Documents / Portfolio / Renewals (filename + sibling meta, status tag,
 * row click), not a one-off history chrome.
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
      <Routes>
        <Route path="/" element={<QuoteHistoryList entries={entries} />} />
        <Route path="/quotes/:quoteId" element={<div>QUOTE_OPEN</div>} />
      </Routes>
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

  it("Not yet assessed uses the outline tag the lines table already uses, never a fabricated tally", () => {
    expect(getQuoteHistoryPositionTag(entry({ lines: [] }))).toEqual({
      variant: "outline",
      label: "Not yet assessed",
    });
  });
});

describe("QuoteHistoryList", () => {
  it("AC-2: an honest empty state when the workspace has never checked a quote", () => {
    renderList([]);
    expect(screen.getByRole("status")).toHaveTextContent(/no quote checks yet/i);
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("uses the shared .table catalogue: Quote · Assessment headers, not a custom list", () => {
    renderList([entry()]);
    const table = screen.getByRole("table");
    expect(table).toHaveClass("table");
    expect(screen.getByRole("columnheader", { name: "Quote" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Assessment" })).toBeInTheDocument();
  });

  it("filename is the row link; meta sits under it the way Documents does", () => {
    renderList([entry()]);
    const link = screen.getByRole("link");
    expect(link).toHaveTextContent("Databricks_Proposal_Q-88213.pdf");
    expect(link).not.toHaveTextContent("Databricks · CHF · CH");
    expect(link).toHaveClass("quote-history-link");
    expect(screen.getByText("Databricks · CHF · CH · 01/09/2026")).toBeInTheDocument();
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

  it("clicking the row (not only the filename) re-opens the quote, same cg-row convenience as Portfolio", () => {
    renderList([entry({ id: "q-42" })]);
    fireEvent.click(screen.getAllByRole("row")[1]);
    expect(screen.getByText("QUOTE_OPEN")).toBeInTheDocument();
  });

  it("a first-of-type entry reads the same honest label as AssessmentResult, never a fabricated position", () => {
    renderList([entry({ lines: [coldStartLine()] })]);
    const tag = screen.getByText("First of its kind");
    expect(tag).toHaveClass("tag", "tag-outline");
  });

  it("a quote still processing (no lines extracted yet) is neither a cold start nor a fabricated tally", () => {
    renderList([entry({ lines: [] })]);
    const tag = screen.getByText("Not yet assessed");
    expect(tag).toHaveClass("tag", "tag-outline");
  });

  it("defaults to 10 rows and pages the rest", async () => {
    const entries = Array.from({ length: 12 }, (_, index) =>
      entry({ id: `q-${index}`, fileName: `Quote_${index}.pdf` }),
    );
    renderList(entries);

    expect(screen.getAllByRole("row")).toHaveLength(11);
    expect(screen.getByRole("link", { name: "Quote_0.pdf" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Quote_10.pdf" })).not.toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Quote history pages" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByRole("link", { name: "Quote_10.pdf" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Quote_0.pdf" })).not.toBeInTheDocument();
  });

  it("an assessed entry's tally is a status tag, the same treatment Portfolio / Documents use", () => {
    renderList([entry()]);
    const tag = screen.getByText("1 above market");
    expect(tag).toHaveClass("tag", "tag-neutral");
  });
});
