import { beforeEach, describe, expect, it } from "vitest";
import type { NegotiationOutcomeBody } from "../../../src/api/client";
import { loadNegotiationOutcomes, rememberNegotiationOutcome, sumRealizedSavings } from "../../../src/routes/quotes/quoteOutcomeStore";

function outcome(overrides: Partial<NegotiationOutcomeBody> = {}): NegotiationOutcomeBody {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    quoteId: "22222222-2222-2222-2222-222222222222",
    originalQuoteTotal: 520_000,
    targetPrice: 420_000,
    finalPrice: 435_000,
    realizedSaving: 85_000,
    discountPercent: 16.35,
    negotiationDurationDays: 24,
    leversUsed: ["Term", "QuarterEnd"],
    capturedAt: "2026-09-06T08:00:00Z",
    savingsOpportunityId: null,
    savingsPropagated: null,
    savingsPropagationError: null,
    ...overrides,
  };
}

describe("quoteOutcomeStore", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("returns an empty list when nothing has been recorded yet", () => {
    expect(loadNegotiationOutcomes()).toEqual([]);
  });

  it("remembers a captured outcome and returns it most-recent-first", () => {
    rememberNegotiationOutcome(outcome({ id: "a" }));
    rememberNegotiationOutcome(outcome({ id: "b" }));

    const loaded = loadNegotiationOutcomes();
    expect(loaded.map((o) => o.id)).toEqual(["b", "a"]);
  });

  it("updates (not duplicates) an outcome recorded again for the same id", () => {
    rememberNegotiationOutcome(outcome({ id: "a", finalPrice: 450_000 }));
    rememberNegotiationOutcome(outcome({ id: "a", finalPrice: 430_000 }));

    const loaded = loadNegotiationOutcomes();
    expect(loaded).toHaveLength(1);
    expect(loaded[0].finalPrice).toBe(430_000);
  });

  it("persists across a fresh read of the same storage (real sessionStorage, not an in-memory mock)", () => {
    rememberNegotiationOutcome(outcome());
    expect(loadNegotiationOutcomes(window.sessionStorage)).toHaveLength(1);
  });

  it("treats malformed sessionStorage content as 'nothing recorded yet', not a throw", () => {
    window.sessionStorage.setItem("contigo.quotes.negotiationOutcomes", "{not json");
    expect(loadNegotiationOutcomes()).toEqual([]);
  });
});

describe("sumRealizedSavings", () => {
  it("sums every outcome's own server-computed realizedSaving", () => {
    expect(sumRealizedSavings([outcome({ realizedSaving: 85_000 }), outcome({ id: "b", realizedSaving: 9_000 })])).toBe(94_000);
  });

  it("returns 0 for an empty list", () => {
    expect(sumRealizedSavings([])).toBe(0);
  });
});
