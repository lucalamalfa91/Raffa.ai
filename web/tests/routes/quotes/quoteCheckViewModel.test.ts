import { describe, expect, it } from "vitest";
import type { QuoteLineAssessmentBody, UnmatchedQuoteLineBody } from "../../../src/api/client";
import {
  aggregateQuote,
  buildAssessmentNumbers,
  buildExtractRows,
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

// The task's own named "Tests required" row: "unmatched SKU blocks assessment until mapped"
// (parent story us-01-quote-check AC-2). `isAssessmentBlocked` is the single, pure, testable gate
// every caller (index.tsx, AssessmentStep.tsx) reads instead of re-deriving the rule -- mirrors
// ../contracts/review/reviewViewModel.ts#isFieldBlocking's own precedent for the identical AC shape.
describe("isAssessmentBlocked (AC-2: unmatched SKU blocks assessment until mapped)", () => {
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
    const rows = buildExtractRows(
      [line({ quoteLineId: "22222222-2222-2222-2222-222222222222" })],
      [unmatched()],
      new Map(),
    );
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
    // unitPrice/quantity are independent of benchmark/targetSaving (LineMarketAssessment's own doc
    // comment: "Populated whenever the line had one, independent of Status") -- originalTotal still
    // resolves.
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

describe("buildAssessmentNumbers (screens.md #10 AC-3: quote / market range / assessment / potential saving)", () => {
  it("returns exactly 4 numbers in the AC's own order", () => {
    const aggregate = aggregateQuote([line()]);
    const numbers = buildAssessmentNumbers(aggregate, [line()]);
    expect(numbers.map((n) => n.key)).toEqual(["quote", "market", "assessment", "saving"]);
    expect(numbers[2].value).toBe("1 above market");
    expect(numbers[0].emphasize).toBe(false);
    expect(numbers[2].emphasize).toBe(true);
  });
});
