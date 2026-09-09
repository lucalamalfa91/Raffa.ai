import { describe, expect, it } from "vitest";
import type { QuoteLineAssessmentBody, UnmatchedQuoteLineBody } from "../../../src/api/client";
import {
  QUOTE_LEVERS_FOOTER,
  aggregateQuote,
  buildAssessmentBand,
  buildExtractRows,
  buildQuoteLineRows,
  formatQuoteMeta,
  formatUnitPrice,
  formatVersusP50,
  isAssessmentBlocked,
  mergeKnownLineDetails,
  previewOutcome,
  summarizePositions,
} from "../../../src/routes/quotes/quoteCheckViewModel";

function line(overrides: Partial<QuoteLineAssessmentBody> = {}): QuoteLineAssessmentBody {
  return {
    quoteLineId: "11111111-1111-1111-1111-111111111111",
    status: "Assessed",
    position: "AboveMarket",
    unitPrice: 100,
    quantity: 10,
    benchmark: {
      hasSufficientData: true,
      distribution: { p25: 80, p50: 90, p75: 100 },
      metric: "unit / yr",
      currency: "CHF",
    },
    confidence: {
      level: "High",
      score: 0.9,
      source: "fixture",
      sampleSize: 42,
      comparisonDimensions: ["Supplier", "Geography"],
      updatedAt: "2026-08-28T00:00:00Z",
      summary: "High confidence, n=42",
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

function unmatched(overrides: Partial<UnmatchedQuoteLineBody> = {}): UnmatchedQuoteLineBody {
  return {
    quoteLineId: "22222222-2222-2222-2222-222222222222",
    sku: "ENT-SUP-CUSTOM",
    normalizedSku: "ENT-SUP-CUSTOM",
    edition: null,
    description: "Enterprise Support Tier — Custom Bundle",
    ...overrides,
  };
}

describe("isAssessmentBlocked (unmatched SKU blocks assessment until mapped)", () => {
  it("blocks when at least one line is still unmatched", () => {
    expect(isAssessmentBlocked([unmatched()])).toBe(true);
  });

  it("blocks on more than one unmatched line too", () => {
    expect(isAssessmentBlocked([unmatched(), unmatched({ quoteLineId: "33333333-3333-3333-3333-333333333333" })])).toBe(true);
  });

  it("does not block once every line is resolved (empty unmatchedLines)", () => {
    expect(isAssessmentBlocked([])).toBe(false);
  });
});

describe("mergeKnownLineDetails", () => {
  it("adds a fresh line's sku/edition/description to an empty map", () => {
    const merged = mergeKnownLineDetails(new Map(), [unmatched()]);
    expect(merged.get("22222222-2222-2222-2222-222222222222")).toEqual({
      sku: "ENT-SUP-CUSTOM",
      edition: null,
      description: "Enterprise Support Tier — Custom Bundle",
    });
  });

  it("keeps an earlier-seen line's details once that line is no longer in the fresh unmatchedLines list (it resolved)", () => {
    const afterFirstLoad = mergeKnownLineDetails(new Map(), [unmatched()]);
    const afterMapping = mergeKnownLineDetails(afterFirstLoad, []);
    expect(afterMapping.get("22222222-2222-2222-2222-222222222222")).toEqual({
      sku: "ENT-SUP-CUSTOM",
      edition: null,
      description: "Enterprise Support Tier — Custom Bundle",
    });
  });
});

describe("buildExtractRows", () => {
  it("labels an unmatched line with its real description and a 'Needs mapping' outline tag", () => {
    const rows = buildExtractRows([line({ quoteLineId: "22222222-2222-2222-2222-222222222222" })], [unmatched()], new Map());
    expect(rows[0].label).toBe("Enterprise Support Tier — Custom Bundle");
    expect(rows[0].sku).toBe("ENT-SUP-CUSTOM");
    expect(rows[0].isUnmatched).toBe(true);
    expect(rows[0].matchTag).toEqual({ variant: "outline", label: "Needs mapping" });
  });

  it("falls back to 'Line N' and a 'Resolved' neutral tag for a line with no known description", () => {
    const rows = buildExtractRows([line()], [], new Map());
    expect(rows[0].label).toBe("Line 1");
    expect(rows[0].sku).toBeNull();
    expect(rows[0].isUnmatched).toBe(false);
    expect(rows[0].matchTag).toEqual({ variant: "neutral", label: "Resolved" });
  });

  it("computes annual as unitPrice * quantity, and null when either is missing", () => {
    const rows = buildExtractRows([line({ unitPrice: 100, quantity: 10 }), line({ quoteLineId: "x", unitPrice: null, quantity: 5 })], [], new Map());
    expect(rows[0].annual).toBe(1000);
    expect(rows[1].annual).toBeNull();
  });
});

describe("aggregateQuote", () => {
  it("sums unitPrice*quantity across lines into originalTotal", () => {
    const aggregate = aggregateQuote([line({ unitPrice: 100, quantity: 10 }), line({ quoteLineId: "x", unitPrice: 50, quantity: 4 })]);
    expect(aggregate.originalTotal).toBe(1000 + 200);
  });

  it("returns null for originalTotal when no line has both unitPrice and quantity", () => {
    const aggregate = aggregateQuote([line({ unitPrice: null, quantity: null })]);
    expect(aggregate.originalTotal).toBeNull();
  });

  it("sums P25/P75 * quantity into an expected market range", () => {
    const aggregate = aggregateQuote([line({ quantity: 10 })]); // p25=80, p75=100
    expect(aggregate.expectedMarketLow).toBe(800);
    expect(aggregate.expectedMarketHigh).toBe(1000);
  });

  it("sums the wire's own totalSavingsRangeLow/High directly (not re-derived)", () => {
    const aggregate = aggregateQuote([line(), line({ quoteLineId: "x" })]);
    expect(aggregate.totalSavingsLow).toBe(200);
    expect(aggregate.totalSavingsHigh).toBe(400);
  });

  it("excludes a line with no benchmark/targetSaving from every sum, rather than treating it as zero", () => {
    const aggregate = aggregateQuote([line({ benchmark: null, targetSaving: null, status: "QuoteDataUnresolved", position: null })]);
    expect(aggregate.expectedMarketLow).toBeNull();
    expect(aggregate.totalSavingsLow).toBeNull();
    expect(aggregate.originalTotal).toBe(1000);
  });

  it("counts only Assessed lines in assessedLineCount, out of the real totalLineCount", () => {
    const aggregate = aggregateQuote([line({ status: "Assessed" }), line({ quoteLineId: "x", status: "QuoteDataUnresolved" })]);
    expect(aggregate.assessedLineCount).toBe(1);
    expect(aggregate.totalLineCount).toBe(2);
  });
});

describe("summarizePositions (a real tally, never a fabricated single verdict)", () => {
  it("tallies each bucket by its real count", () => {
    expect(
      summarizePositions([
        line({ position: "AboveMarket" }),
        line({ quoteLineId: "b", position: "AboveMarket" }),
        line({ quoteLineId: "c", position: "InLine" }),
      ]),
    ).toBe("2 above market · 1 in line");
  });

  it("reports 'Not yet assessed' when no line has a position yet", () => {
    expect(summarizePositions([line({ position: null, status: "QuoteDataUnresolved" })])).toBe("Not yet assessed");
  });
});

describe("buildAssessmentBand (markup.html: Supplier quote · Market range · Assessment)", () => {
  it("returns exactly the three V2 cells, in the prototype's order, with only Assessment emphasised", () => {
    const aggregate = aggregateQuote([line()]);
    const band = buildAssessmentBand(aggregate, [line()]);
    expect(band.map((cell) => [cell.key, cell.label, cell.value])).toEqual([
      ["quote", "Supplier quote", "CHF 1,000"],
      ["market", "Market range", "CHF 800–1,000"],
      ["assessment", "Assessment", "1 above market"],
    ]);
    expect(band.map((cell) => cell.emphasize)).toEqual([false, false, true]);
  });

  it("is honest about missing data", () => {
    const band = buildAssessmentBand(aggregateQuote([]), []);
    expect(band.map((cell) => cell.value)).toEqual(["Not yet available", "Not yet available", "Not yet assessed"]);
  });
});

describe("unit prices and P50 comparison", () => {
  it("keeps decimals on per-unit prices while totals stay whole", () => {
    expect(formatUnitPrice(0.55, "CHF")).toBe("CHF 0.55");
    expect(formatUnitPrice(50_000, "CHF")).toBe("CHF 50,000");
    expect(formatUnitPrice(null, "CHF")).toBe("Not yet available");
  });

  it("formats the deterministic distance to the median, or nothing when a figure is missing", () => {
    expect(formatVersusP50(0.55, 0.46)).toBe("+20% vs P50");
    expect(formatVersusP50(90, 100)).toBe("-10% vs P50");
    expect(formatVersusP50(90, 90)).toBe("At P50");
    expect(formatVersusP50(null, 90)).toBeNull();
    expect(formatVersusP50(90, null)).toBeNull();
    expect(formatVersusP50(90, 0)).toBeNull();
  });
});

describe("buildQuoteLineRows (Line · Quoted · P50 · Position · Benchmark)", () => {
  it("carries the real per-line figures, the position tag, the P50 distance and the benchmark confidence tag", () => {
    const rows = buildExtractRows([line()], [], new Map());
    const [row] = buildQuoteLineRows(rows, [line()]);
    expect(row.label).toBe("Line 1");
    expect(row.quoted).toBe("CHF 100");
    expect(row.p50).toBe("CHF 90");
    expect(row.position).toEqual({ variant: "accent", label: "Above market" });
    expect(row.vsP50).toBe("+11% vs P50");
    expect(row.benchmark).toEqual({ variant: "neutral", label: "High · n=42" });
    expect(row.needsMapping).toBe(false);
  });

  it("maps benchmark confidence levels to High neutral / Medium accent / Low outline", () => {
    const medium = line({ confidence: { ...line().confidence!, level: "Medium", sampleSize: 22 } });
    const low = line({ confidence: { ...line().confidence!, level: "Low", sampleSize: null } });
    const [mediumRow] = buildQuoteLineRows(buildExtractRows([medium], [], new Map()), [medium]);
    const [lowRow] = buildQuoteLineRows(buildExtractRows([low], [], new Map()), [low]);
    expect(mediumRow.benchmark).toEqual({ variant: "accent", label: "Medium · n=22" });
    expect(lowRow.benchmark).toEqual({ variant: "outline", label: "Low · n=—" });
  });

  it("says 'Needs mapping' for an unmatched line instead of a position it does not have, and has no benchmark tag", () => {
    const unresolved = line({ quoteLineId: "22222222-2222-2222-2222-222222222222", benchmark: null, position: null, status: "QuoteDataUnresolved", confidence: null });
    const rows = buildExtractRows([unresolved], [unmatched()], new Map());
    const [row] = buildQuoteLineRows(rows, [unresolved]);
    expect(row.label).toBe("Enterprise Support Tier — Custom Bundle");
    expect(row.position).toEqual({ variant: "outline", label: "Needs mapping" });
    expect(row.p50).toBe("Not yet available");
    expect(row.vsP50).toBeNull();
    expect(row.benchmark).toBeNull();
    expect(row.needsMapping).toBe(true);
  });
});

describe("header copy", () => {
  it("quotes the levers footer verbatim", () => {
    expect(QUOTE_LEVERS_FOOTER).toBe("Target and negotiation levers are one step further — shown only if you want them.");
  });

  it("formats the meta line from the upload response, or falls back to the quote id", () => {
    expect(
      formatQuoteMeta(
        {
          id: "q",
          fileName: "Databricks_Proposal_Q-88213.pdf",
          mimeType: "application/pdf",
          processingStatus: "NeedsReview",
          lineItemCount: 3,
          normalizedLineItemCount: 3,
          unresolvedNormalizationCount: 0,
          unmatchedSkuCount: 0,
          supplier: "Databricks",
          currency: "CHF",
          geography: null,
          purchaseDate: null,
          createdAt: "2026-09-06T00:00:00Z",
        },
        "q",
      ),
    ).toBe("Databricks_Proposal_Q-88213.pdf · Databricks · CHF");
    expect(formatQuoteMeta(null, "22222222-2222-2222-2222-222222222222")).toBe("Quote 22222222");
  });
});

describe("previewOutcome (mirrors NegotiationOutcomeCalculator.Compute verbatim)", () => {
  it("reproduces spec §12.2's own worked example: 520,000 -> 435,000 = 85,000 saving, ~16.3%", () => {
    const preview = previewOutcome(520_000, 435_000);
    expect(preview.realizedSaving).toBe(85_000);
    expect(preview.discountPercent).toBeCloseTo(16.346, 2);
  });

  it("never clamps a negative saving at zero (a final price above the original is an honest, unusual outcome)", () => {
    const preview = previewOutcome(100, 150);
    expect(preview.realizedSaving).toBe(-50);
  });

  it("returns nulls (not a fabricated 0%) when either figure is not known yet", () => {
    expect(previewOutcome(null, 100)).toEqual({ realizedSaving: null, discountPercent: null });
    expect(previewOutcome(100, null)).toEqual({ realizedSaving: null, discountPercent: null });
  });
});
