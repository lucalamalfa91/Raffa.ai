import { describe, expect, it } from "vitest";
import type {
  Contract360Body,
  Contract360ClauseBody,
  Contract360DocumentBody,
  Contract360HeaderBody,
  Contract360ObligationBody,
  Contract360ProductBody,
  Contract360RiskBody,
  ContractFieldEvidenceBody,
  ContractStrategyBody,
  RenewalPipelineItemBody,
  RenewalPriorityBody,
} from "../../../../src/api/client";
import {
  ADD_THE_END_DATE,
  AUTO_ACCEPT_THRESHOLD,
  LEVERAGE_LEGEND,
  LEVERAGE_PUSH_TO_CHANGE,
  LEVERAGE_STANDARD_TERMS,
  LEVERAGE_WORTH_RAISING,
  LEVER_NOT_YET_AVAILABLE,
  SAVINGS_NOT_YET_AVAILABLE,
  UNOFFICIALIZED_PLACEHOLDER,
  buildAnswers,
  buildClauseEvidence,
  buildClauseRows,
  buildDocumentRows,
  buildKeyTerms,
  buildNegotiationSteps,
  buildObligationColumns,
  buildPriorityComponentRows,
  buildProductLines,
  buildRecommendation,
  buildRiskItems,
  buildScoreParts,
  buildLeverCards,
  buildProductNote,
  buildClauseGroups,
  buildClosedOutcome,
  standardClausesLabel,
  terminatedActionText,
  PRODUCT_NOTE_NO_MATCH,
  MARKET_MEDIAN_EXPLAINED,
  SAVE_EXPLAINED,
  SAVING_COLUMN_EXPLAINED,
  formatSampleSize,
  PRODUCT_NOTE_UNCHECKED,
  AT_MARKET_PRICE,
  TARGET_PRICE_ONLY,
  buildProductSavingTotal,
  computeLineSaving,
  formatSavingRange,
  SECTION_COPY,
  clauseViewerHref,
  computeNeedsAttention,
  formatHeaderMeta,
  formatPriorityFact,
  formatReviewCountLine,
  formatShortReference,
  formatVersusMarket,
  isExtractedRowShown,
  formatTrackerMeta,
  getClauseRiskTag,
  leverageWhy,
  resolveBackLink,
  resolveHighlightedClauseId,
  resolveSupplierLabel,
  ticksFromServer,
  toConfidencePercent,
} from "../../../../src/routes/contracts/contract360/contract360ViewModel";

const CONTRACT_ID = "11111111-1111-1111-1111-111111111111";

function header(overrides: Partial<Contract360HeaderBody> = {}): Contract360HeaderBody {
  return {
    contractId: CONTRACT_ID,
    supplierId: null,
    supplierName: null,
    type: "Msa",
    status: "active",
    annualSpend: 500_000,
    totalContractValue: 1_500_000,
    startDate: "2025-01-01",
    endDate: "2026-01-01",
    renewalDate: "2026-01-01",
    cancellationDeadline: "2025-11-17",
    autoRenewal: true,
    risk: "High",
    ...overrides,
  };
}

function product(overrides: Partial<Contract360ProductBody> = {}): Contract360ProductBody {
  return {
    lineItemId: "li-1",
    productId: null,
    sku: "SKU-1",
    description: "Premium DBU — committed",
    quantity: 120_000,
    unit: "DBU/yr",
    unitPrice: 0.55,
    listPrice: 0.6,
    discount: 12,
    billingPeriod: "annual",
    annualCost: 66_000,
    totalCost: 198_000,
    sourceDocumentId: "doc-1",
    sourceSpan: "§6.2",
    sourcePage: 9,
    confidence: 0.97,
    market: null,
    ...overrides,
  };
}

function marketBand(overrides: Partial<NonNullable<Contract360ProductBody["market"]>> = {}): NonNullable<Contract360ProductBody["market"]> {
  return {
    matched: true,
    recordId: "MKT-DBU-CH",
    product: "Premium DBU",
    geography: "CH",
    currency: "CHF",
    termMonths: 12,
    unitPriceP25: 0.4,
    unitPriceP50: 0.5,
    unitPriceP75: 0.6,
    sampleSize: 40,
    provenance: "representative market data · mock feed · updated 2026-07-01",
    marketUpdatedAt: "2026-07-01T00:00:00Z",
    checkedAt: "2026-09-22T08:00:00Z",
    matchKind: "Exact",
    ...overrides,
  };
}

function clause(overrides: Partial<Contract360ClauseBody> = {}): Contract360ClauseBody {
  return {
    clauseId: "cl-1",
    clauseType: "Liability cap",
    rawText: "Liability is capped at 12 months fees, save for confidentiality.",
    normalizedValue: "12 months fees",
    riskLevel: "Medium",
    sourceDocumentId: "doc-1",
    sourceSpan: "§17.2",
    sourcePage: 27,
    confidence: 0.78,
    ...overrides,
  };
}

function obligation(overrides: Partial<Contract360ObligationBody> = {}): Contract360ObligationBody {
  return {
    obligationId: "ob-1",
    party: "Customer",
    obligationType: "True-up",
    description: "Annual true-up of committed DBU",
    dueDate: "2026-01-15",
    recurrenceRule: "annual",
    criticality: "high",
    status: "pending",
    sourceDocumentId: "doc-1",
    sourceSpan: "§6.3",
    sourcePage: 10,
    confidence: 0.88,
    ...overrides,
  };
}

function risk(overrides: Partial<Contract360RiskBody> = {}): Contract360RiskBody {
  return {
    riskId: "risk-1",
    riskType: "Auto-renewal",
    severity: "High",
    description: "Auto-renews without an explicit re-negotiation checkpoint",
    status: "open",
    clauseId: null,
    sourceDocumentId: "doc-1",
    sourceSpan: "§8.4",
    sourcePage: 12,
    confidence: 0.97,
    ...overrides,
  };
}

function fieldEvidence(overrides: Partial<ContractFieldEvidenceBody> = {}): ContractFieldEvidenceBody {
  return {
    fieldName: "annualSpend",
    value: "500000",
    confidence: 0.96,
    decision: "auto_accepted",
    sourcePage: 2,
    sourceSpan: "CHF 500,000 per year",
    sourceDocumentId: "doc-1",
    sourceFileName: "MSA.pdf",
    passage: null,
    highlightStart: null,
    highlightLength: null,
    box: null,
    modelId: "fixture-extract-model",
    extractedAt: "2026-09-09T10:00:00Z",
    ...overrides,
  };
}

function acceptedEvidence(): ContractFieldEvidenceBody[] {
  return [
    fieldEvidence({ fieldName: "annualSpend" }),
    fieldEvidence({ fieldName: "totalContractValue", value: "1500000" }),
    fieldEvidence({ fieldName: "startDate", value: "2025-01-01" }),
    fieldEvidence({ fieldName: "endDate", value: "2026-01-01" }),
    fieldEvidence({ fieldName: "cancellationDeadline", value: "2025-11-17" }),
    fieldEvidence({ fieldName: "autoRenewal", value: "true" }),
    fieldEvidence({ fieldName: "renewalTermMonths", value: "12" }),
    fieldEvidence({ fieldName: "paymentTerms", value: "Net 45" }),
    fieldEvidence({ fieldName: "governingLaw", value: "Switzerland, Zürich" }),
    fieldEvidence({ fieldName: "effectiveDate", value: "2025-01-01" }),
  ];
}

function renewalItem(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalPipelineItemBody {
  return {
    contractId: CONTRACT_ID,
    supplierId: null,
    supplierName: null,
    status: "Determined",
    contractStatus: "active",
    documentProcessingStatus: "Completed",
    renewalDate: "2026-01-01",
    daysUntilRenewal: 30,
    annualSpend: 500_000,
    cancellationDeadline: "2025-11-17",
    daysUntilCancellationDeadline: 14,
    autoRenewal: true,
    action: "Start renewal negotiation now",
    priority: null,
    insightCard: {
      facts: {
        supplierId: null,
        supplierName: null,
        renewalDate: "2026-01-01",
        daysUntilRenewal: 30,
        annualSpend: 500_000,
        cancellationDeadline: "2025-11-17",
        daysUntilCancellationDeadline: 14,
      },
      recommendations: {
        recommendedAction: "Start renewal negotiation now",
        explanation: "Renews in 30 days with a cancellation notice due in 14 days.",
        annualUpliftPercent: null,
        marketPosition: null,
        potentialSavingsRange: null,
      },
    },
    ...overrides,
  };
}

function priority(overrides: Partial<RenewalPriorityBody> = {}): RenewalPriorityBody {
  return {
    contractId: CONTRACT_ID,
    totalScore: 72,
    components: {
      spendWeight: { score: 20, explanation: "AnnualSpend (500000) is 500,000 or more: maximum spend weight (20)." },
      timeUrgency: { score: 20, explanation: "30 day(s) until renewal, within the 30-day window: maximum time urgency (20)." },
      benchmarkOpportunity: { score: 10, explanation: "R3 benchmark data is not available: neutral (10)." },
      priceIncreaseRisk: { score: 7, explanation: "AnnualUpliftPercent is unknown: minimum (0)." },
      contractRisk: { score: 15, explanation: "ContractRisk is High: contract risk (15)." },
    },
    ...overrides,
  };
}

const renewalTab: Contract360Body["tabs"]["renewal"] = {
  endDate: "2026-01-01",
  renewalDate: "2026-01-01",
  cancellationDeadline: "2025-11-17",
  autoRenewal: true,
  renewalTermMonths: 12,
};

function strategyPack(overrides: Partial<ContractStrategyBody> = {}): ContractStrategyBody {
  return {
    contractId: CONTRACT_ID,
    whenYouMustMove: {
      renewalDate: "2026-01-01",
      cancellationDeadline: "2025-11-17",
      daysLeft: 7,
      passedDeadline: false,
      explanation: "7 day(s) until the cancellation deadline.",
    },
    whereYouCanPush: [
      { leverType: "Volume", rationale: "This line orders 120,000 — cite the order size.", citationKeys: ["fact:volume"] },
    ],
    targets: [
      {
        description: "Premium DBU",
        openingTarget: 1500,
        acceptableRangeLow: 1500,
        acceptableRangeHigh: 1800,
        walkAwayThreshold: 2100,
        explanation: "Recommended target range [1500, 1800]. representative (source: A; n=214; as of 2026-01-01)",
      },
    ],
    nextSteps: [{ label: "Notify Salesforce of intent to renegotiate", dueHint: "this week" }],
    openWeakFacts: [],
    ...overrides,
  };
}

const strategyCalled = (pack: ContractStrategyBody | null) => ({ called: true as const, pack });
const strategyNotCalled = { called: false as const };

function contractBody(overrides: Partial<Contract360Body["tabs"]> = {}): Contract360Body {
  return {
    contractId: CONTRACT_ID,
    // Task E16/F02/US03/T01 (ADR-027 §D9): a fully extracted fixture is a `ready` one.
    readiness: { state: "ready", stage: null, documentCount: 1, completedDocumentCount: 1 },
    header: header(),
    tabs: {
      overview: {
        currency: "CHF",
        effectiveDate: "2025-01-01",
        renewalTermMonths: 12,
        paymentTerms: "Net 45",
        governingLaw: "Switzerland, Zürich",
        parentContractId: null,
        version: 1,
        createdAt: "2025-01-01T00:00:00Z",
      },
      commercials: {
        annualSpend: 500_000,
        totalContractValue: 1_500_000,
        currency: "CHF",
        paymentTerms: "Net 45",
        autoRenewal: true,
        renewalTermMonths: 12,
        lineItemCount: 1,
        lineItemAnnualCostTotal: 66_000,
        lineItemTotalCostTotal: 198_000,
      },
      products: [product()],
      clauses: [clause()],
      obligations: [obligation()],
      risks: [risk()],
      documents: [
        { documentId: "doc-1", fileName: "MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Completed", createdAt: "2026-01-01T00:00:00Z" },
      ],
      benchmark: [],
      renewal: renewalTab,
      activity: [],
      ...overrides,
    },
  };
}

describe("toConfidencePercent", () => {
  it("converts a 0-1 fraction to a 0-100 percentage and passes null through", () => {
    expect(toConfidencePercent(0.92)).toBe(92);
    expect(toConfidencePercent(0)).toBe(0);
    expect(toConfidencePercent(null)).toBeNull();
  });
});

describe("header", () => {
  it("resolveBackLink follows the origin, including Savings, and is null for anything else", () => {
    expect(resolveBackLink("ask")).toEqual({ label: "Ask Raffa", href: "/ask" });
    expect(resolveBackLink("documents")).toEqual({ label: "Documents", href: "/documents" });
    expect(resolveBackLink("portfolio")).toEqual({ label: "Portfolio", href: "/contracts" });
    expect(resolveBackLink("renewals")).toEqual({ label: "Renewals", href: "/renewals" });
    expect(resolveBackLink("savings")).toEqual({ label: "Savings", href: "/savings" });
    expect(resolveBackLink(undefined)).toBeNull();
    expect(resolveBackLink(null)).toBeNull();
    expect(resolveBackLink("something-else")).toBeNull();
  });

  it("resolveSupplierLabel prefers the resolved name, falls back to the id fragment, ignores blanks", () => {
    expect(resolveSupplierLabel(header({ supplierId: "33333333-3333-3333-3333-333333333333", supplierName: "Salesforce" }))).toEqual({
      label: "Salesforce",
      title: "33333333-3333-3333-3333-333333333333",
    });
    expect(resolveSupplierLabel(header({ supplierId: "33333333-3333-3333-3333-333333333333" }))).toEqual({
      label: "Supplier 33333333",
      title: "33333333-3333-3333-3333-333333333333",
    });
    expect(resolveSupplierLabel(header({ supplierName: "   " })).label).not.toBe("   ");
  });

  it("formatHeaderMeta reads '{type} · {spend} / year · {docs} documents · {status}' and is honest about missing spend", () => {
    expect(formatHeaderMeta(header(), "CHF", 2)).toMatch(/^MSA · CHF 500,000 \/ year · 2 documents · /);
    expect(formatHeaderMeta(header({ annualSpend: null }), "CHF", 1)).toMatch(/^MSA · spend not recorded · 1 document · /);
  });
});

describe("answers band", () => {
  it("buildRecommendation sources the real recommendedAction/explanation from the matching pipeline item", () => {
    const recommendation = buildRecommendation(header(), [renewalItem()]);
    expect(recommendation).toEqual({
      hasRecommendation: true,
      statement: "Start renewal negotiation now",
      rationale: "Renews in 30 days with a cancellation notice due in 14 days.",
    });
  });

  it("buildRecommendation names an honest gap when there is no pipeline entry, specific to auto-renewal", () => {
    expect(buildRecommendation(header(), [])).toMatchObject({ hasRecommendation: false, statement: "No renewal recommendation for this contract" });
    expect(buildRecommendation(header({ autoRenewal: false }), []).rationale).toMatch(/only for auto-renewing contracts/i);
    expect(buildRecommendation(header(), []).rationale).toMatch(/did not appear in the current renewal pipeline yet/i);
  });

  it("state (i): a figure and its lever map from the strategy pack, never from the pipeline uplift chain", () => {
    const pack = strategyPack({
      targets: [
        {
          description: "Premium DBU",
          openingTarget: 1500,
          acceptableRangeLow: null,
          acceptableRangeHigh: null,
          walkAwayThreshold: null,
          explanation: "Opening target 1500 from this line's own unit price.",
        },
      ],
    });
    const save = buildAnswers(header(), renewalTab, [renewalItem()], strategyCalled(pack)).save;
    expect(save).toEqual({ estimate: "1,500", lever: "This line orders 120,000 — cite the order size.", source: "" });
  });

  it("state (ii): a representative band without the line's annual cost reads as a target unit price, its provenance never bare", () => {
    const save = buildAnswers(header(), renewalTab, [], strategyCalled(strategyPack())).save;
    expect(save.estimate).toBe("1,500–1,800 / unit");
    expect(save.lever).toBe(TARGET_PRICE_ONLY);
    expect(save.source).toBe("Representative market data from 214 comparable contracts · source A · as of 01/01/2026");
    expect(save.source).toMatch(/source A/);
    expect(save.source).not.toMatch(/n = |n=/);

    const withCurrency = buildAnswers(header(), renewalTab, [], strategyCalled(strategyPack()), {
      products: [product({ description: "Premium DBU", annualCost: null })],
      currency: "CHF",
    }).save;
    expect(withCurrency.estimate).toBe("CHF 1,500–1,800 / unit");
  });

  it("where you can save: the yearly saving, what you pay against the market median, then the provenance", () => {
    const jira = product({
      description: "Jira Software Premium + Confluence Premium",
      quantity: 780,
      unit: "named users / licenses",
      unitPrice: 191,
      annualCost: 149_000,
      market: marketBand({ product: "Jira Software Premium", geography: "UK", currency: "GBP", unitPriceP25: 109, unitPriceP50: 120, unitPriceP75: 131, sampleSize: 6 }),
    });
    const pack = strategyPack({
      targets: [
        {
          description: "Jira Software Premium + Confluence Premium",
          openingTarget: 98,
          acceptableRangeLow: 109,
          acceptableRangeHigh: 120,
          walkAwayThreshold: 131,
          explanation: "Recommended target range [109, 120]. representative (source: market-feed (representative, mock); n=6; as of 2026-09-10)",
        },
      ],
    });

    const save = buildAnswers(header(), renewalTab, [], strategyCalled(pack), { products: [jira], currency: "GBP" }).save;
    expect(save).toEqual({
      estimate: "GBP 55–64k / yr",
      lever: "You pay GBP 191 per unit against a market median of GBP 120 (+59%).",
      source:
        "Representative market data from only 6 comparable contracts (a small sample: treat it as indicative) · source market-feed (representative, mock) · as of 10/09/2026",
    });
    expect(formatTrackerMeta(save, buildAnswers(header(), renewalTab, [], strategyCalled(pack)).move)).toBe("target GBP 55–64k / yr · close by 17/11/2025");
  });

  it("where you can save: a bundle's summed medians and a similar product's median say what they are", () => {
    const bundleLine = product({
      description: "Jira Software Premium + Confluence Premium",
      unitPrice: 191,
      annualCost: 149_000,
      market: marketBand({ product: "Jira Software Premium + Confluence Premium", currency: "GBP", unitPriceP25: 185.3, unitPriceP50: 205, matchKind: "Bundle" }),
    });
    const bundlePack = strategyPack({
      targets: [{ ...strategyPack().targets[0], description: bundleLine.description, acceptableRangeLow: 185.3, acceptableRangeHigh: 191 }],
    });
    const bundle = buildAnswers(header(), renewalTab, [], strategyCalled(bundlePack), { products: [bundleLine], currency: "GBP" }).save;
    expect(bundle.estimate).toBe("Up to GBP 4.4k / yr");
    expect(bundle.lever).toBe("You pay GBP 191 per unit against GBP 205, each product's market median added up (-7%).");
    expect(formatTrackerMeta(bundle, buildAnswers(header(), renewalTab, [], strategyCalled(bundlePack)).move)).toBe("target up to GBP 4.4k / yr · close by 17/11/2025");

    const similarLine = product({
      description: "Confluence Enterprise",
      unitPrice: 100,
      annualCost: 10_000,
      market: marketBand({ product: "Confluence Premium", currency: "GBP", unitPriceP25: 76.3, unitPriceP50: 85, matchKind: "Similar" }),
    });
    const similarPack = strategyPack({
      targets: [{ ...strategyPack().targets[0], description: "Confluence Enterprise", acceptableRangeLow: 76.3, acceptableRangeHigh: 85 }],
    });
    const similar = buildAnswers(header(), renewalTab, [], strategyCalled(similarPack), { products: [similarLine], currency: "GBP" }).save;
    expect(similar.estimate).toBe("≈ GBP 1.5–2.4k / yr");
    expect(similar.lever).toBe("You pay GBP 100 per unit against GBP 85, the median of a similar product (Confluence Premium), not your exact one (+18%).");
    expect(similar.similar).toBe(true);
    expect(formatTrackerMeta({ ...similar, estimate: "≈ Up to GBP 2.4k / yr" }, buildAnswers(header(), renewalTab, [], strategyCalled(similarPack)).move)).toBe(
      "target ≈ up to GBP 2.4k / yr · close by 17/11/2025",
    );
  });

  it("where you can save: at or below the market's cheaper quarter there is nothing to save on price", () => {
    const cheap = product({
      description: "Premium DBU",
      unitPrice: 100,
      annualCost: 10_000,
      market: marketBand({ currency: "GBP", unitPriceP25: 109, unitPriceP50: 120 }),
    });
    const pack = strategyPack({ targets: [{ ...strategyPack().targets[0], acceptableRangeLow: 100, acceptableRangeHigh: 100 }] });
    const save = buildAnswers(header(), renewalTab, [], strategyCalled(pack), { products: [cheap], currency: "GBP" }).save;
    expect(save.estimate).toBe(AT_MARKET_PRICE);
    expect(save.lever).toBe("You pay GBP 100 per unit, at or below a market median of GBP 120 (-17%) — price is not where the saving is.");
  });

  it("where you can save: several lines add up, the lever names the line with the most to save", () => {
    const small = product({ lineItemId: "li-a", description: "Line A", unitPrice: 10, annualCost: 1_000, market: marketBand({ currency: "GBP", unitPriceP25: 8, unitPriceP50: 9 }) });
    const big = product({ lineItemId: "li-b", description: "Line B", unitPrice: 100, annualCost: 50_000, market: marketBand({ currency: "GBP", unitPriceP25: 70, unitPriceP50: 80 }) });
    const target = strategyPack().targets[0];
    const pack = strategyPack({
      targets: [
        { ...target, description: "Line A", acceptableRangeLow: 8, acceptableRangeHigh: 9 },
        { ...target, description: "Line B", acceptableRangeLow: 70, acceptableRangeHigh: 80 },
      ],
    });
    const save = buildAnswers(header(), renewalTab, [], strategyCalled(pack), { products: [small, big], currency: "GBP" }).save;
    // A: 100–200; B: 10,000–15,000.
    expect(save.estimate).toBe("GBP 10–15k / yr");
    expect(save.lever).toBe("Line B: you pay GBP 100 per unit against a market median of GBP 80 (+25%).");

    // The small print is the lead line's own source and sample, not the first line's.
    const sourced = strategyPack({
      targets: [
        { ...target, description: "Line A", acceptableRangeLow: 8, acceptableRangeHigh: 9, explanation: "representative (source: A; n=6; as of 2026-01-01)" },
        { ...target, description: "Line B", acceptableRangeLow: 70, acceptableRangeHigh: 80, explanation: "representative (source: B; n=40; as of 2026-01-01)" },
      ],
    });
    expect(buildAnswers(header(), renewalTab, [], strategyCalled(sourced), { products: [small, big], currency: "GBP" }).save.source).toBe(
      "Representative market data from 40 comparable contracts · source B · as of 01/01/2026",
    );

    // A line whose price is not officialized never sizes a saving.
    const hidden = { ...big, confidence: 0.5, sourcePage: null, sourceSpan: null };
    const withoutHidden = buildAnswers(header(), renewalTab, [], strategyCalled(pack), { products: [small, hidden], currency: "GBP" }).save;
    expect(withoutHidden.estimate).toBe("GBP 100–200 / yr");
  });

  it("formatSampleSize says how many contracts in words, and 'only' below the small-sample bar", () => {
    expect(formatSampleSize(22)).toBe("22 contracts");
    expect(formatSampleSize(10)).toBe("10 contracts");
    expect(formatSampleSize(6)).toBe("only 6 contracts");
    expect(formatSampleSize(1, "comparable contract")).toBe("only 1 comparable contract");
  });

  it("formatSavingRange puts both ends on the higher one's scale, and says 'up to' when the low end is nothing", () => {
    expect(formatSavingRange({ low: 55_387, high: 63_968 }, "GBP")).toBe("GBP 55–64k");
    expect(formatSavingRange({ low: 0, high: 4_446 }, "GBP")).toBe("up to GBP 4.4k");
    expect(formatSavingRange({ low: 1_200_000, high: 2_500_000 }, "CHF")).toBe("CHF 1.2–2.5M");
    expect(formatSavingRange({ low: 800, high: 950 }, "EUR")).toBe("EUR 800–950");
    expect(formatSavingRange({ low: 64_000, high: 64_200 }, "GBP")).toBe("GBP 64k");
    expect(formatSavingRange({ low: 0, high: 0 }, "GBP")).toBeNull();
    expect(computeLineSaving(191, 149_000, 109, 120)).toEqual({ low: expect.closeTo(55_387, 0), high: expect.closeTo(63_969, 0) });
    expect(computeLineSaving(191, null, 109, 120)).toBeNull();
    expect(computeLineSaving(null, 149_000, 109, 120)).toBeNull();
  });

  it("state (iii): a missing end date names the way to get the fact and never says Not determined", () => {
    const pack = strategyPack({
      whenYouMustMove: {
        renewalDate: null,
        cancellationDeadline: null,
        daysLeft: null,
        passedDeadline: false,
        explanation: "No renewal date or cancellation deadline could be determined for this contract.",
      },
    });
    const move = buildAnswers(header({ cancellationDeadline: null, endDate: null }), renewalTab, [], strategyCalled(pack)).move;
    expect(move.deadline).toBe(ADD_THE_END_DATE);
    expect(move.deadlineHref).toBe(`/contracts/${CONTRACT_ID}/review`);
    expect(move.detail).toMatch(/Add it on Review/i);
    expect(JSON.stringify(move)).not.toMatch(/Not determined/);
  });

  it("the two constants appear only when the strategy source was called and returned nothing — not when it was never called", () => {
    const neverCalled = buildAnswers(header(), renewalTab, [renewalItem()], strategyNotCalled);
    expect(neverCalled.save.estimate).not.toBe(SAVINGS_NOT_YET_AVAILABLE);
    expect(neverCalled.save.lever).not.toBe(LEVER_NOT_YET_AVAILABLE);
    expect(neverCalled.save).toEqual({ estimate: "", lever: "", source: "" });

    const calledEmpty = buildAnswers(
      header(),
      renewalTab,
      [renewalItem()],
      strategyCalled(
        strategyPack({
          whereYouCanPush: [],
          targets: [
            {
              description: "Premium DBU",
              openingTarget: null,
              acceptableRangeLow: null,
              acceptableRangeHigh: null,
              walkAwayThreshold: null,
              explanation: "insufficient market data for this line.",
            },
          ],
        }),
      ),
    );
    expect(calledEmpty.save).toEqual({ estimate: SAVINGS_NOT_YET_AVAILABLE, lever: LEVER_NOT_YET_AVAILABLE, source: "" });

    const calledFailed = buildAnswers(header(), renewalTab, [renewalItem()], strategyCalled(null));
    expect(calledFailed.save).toEqual({ estimate: SAVINGS_NOT_YET_AVAILABLE, lever: LEVER_NOT_YET_AVAILABLE, source: "" });
  });

  it("When you must move maps the pack's notice deadline, days left and auto-renewal", () => {
    const soon = buildAnswers(header(), renewalTab, [], strategyCalled(strategyPack())).move;
    expect(soon.deadline).toBe("17/11/2025");
    expect(soon.deadlineHref).toBeNull();
    expect(soon.cancelDays).toBe(7);
    expect(soon.isUrgent).toBe(true);
    expect(soon.detail).toBe("in 7 days — last day to give notice. Term ends 01/01/2026 and auto-renews for 12 months.");

    const farPack = strategyPack({
      whenYouMustMove: {
        renewalDate: "2026-09-01",
        cancellationDeadline: "2026-06-01",
        daysLeft: 203,
        passedDeadline: false,
        explanation: "203 day(s) until the cancellation deadline.",
      },
    });
    const far = buildAnswers(
      header({ cancellationDeadline: "2026-06-01", endDate: "2026-09-01" }),
      { ...renewalTab, renewalTermMonths: null },
      [],
      strategyCalled(farPack),
    ).move;
    expect(far.isUrgent).toBe(false);
    expect(far.detail).toBe("in 203 days — last day to give notice. Term ends 01/09/2026 and auto-renews.");

    const pastPack = strategyPack({
      whenYouMustMove: {
        renewalDate: "2026-01-01",
        cancellationDeadline: "2025-11-17",
        daysLeft: -14,
        passedDeadline: true,
        explanation: "The cancellation deadline passed 14 day(s) ago — stated as passed, not hidden (AC-4).",
      },
    });
    const past = buildAnswers(
      header({ autoRenewal: false }),
      { ...renewalTab, autoRenewal: false },
      [],
      strategyCalled(pastPack),
    ).move;
    expect(past.detail).toBe("14 days ago — the notice window has closed. Term ends 01/01/2026.");
    expect(past.isUrgent).toBe(false);
  });

  it("the tracker steps and meta follow the real supplier and mapped deadline", () => {
    expect(buildNegotiationSteps("Salesforce", "17/11/2025")).toEqual([
      { key: "Notify", label: "Notify Salesforce of intent to renegotiate", due: "this week" },
      { key: "RequestRevisedPricing", label: "Request revised pricing and licence mix", due: "+10 days" },
      { key: "CounterWithMarketBenchmark", label: "Counter with the market benchmark", due: "+20 days" },
      { key: "SignOrSendNonRenewalNotice", label: "Sign, or send non-renewal notice", due: "by 17/11/2025" },
    ]);
    const answers = buildAnswers(header(), renewalTab, [], strategyCalled(strategyPack()));
    expect(formatTrackerMeta(answers.save, answers.move)).toBe("target 1,500–1,800 / unit · close by 17/11/2025");
  });

  it("ticksFromServer keeps named keys, ignores unknown names, and treats a missing key as unticked", () => {
    expect([...ticksFromServer(["Notify", "RequestRevisedPricing"])]).toEqual(["Notify", "RequestRevisedPricing"]);
    expect([...ticksFromServer(["Notify", "InventedFifthStep", "CounterWithMarketBenchmark"])]).toEqual([
      "Notify",
      "CounterWithMarketBenchmark",
    ]);
    expect(ticksFromServer(["Notify"]).has("RequestRevisedPricing")).toBe(false);
    expect(ticksFromServer([]).size).toBe(0);
  });
});

describe("why — the clauses behind it", () => {
  it("getClauseRiskTag returns the three leverage labels; High/Critical stay accent; null stays null", () => {
    expect(getClauseRiskTag("High")).toEqual({ variant: "accent", label: LEVERAGE_PUSH_TO_CHANGE });
    expect(getClauseRiskTag("Critical")).toEqual({ variant: "accent", label: LEVERAGE_PUSH_TO_CHANGE });
    expect(getClauseRiskTag("Medium")).toEqual({ variant: "neutral", label: LEVERAGE_WORTH_RAISING });
    expect(getClauseRiskTag("Low")).toEqual({ variant: "neutral", label: LEVERAGE_STANDARD_TERMS });
    expect(getClauseRiskTag(null)).toBeNull();
    expect(getClauseRiskTag("  ")).toBeNull();
    expect(getClauseRiskTag("Unknown")).toBeNull();
  });

  it("the raw ContractRiskLevel enum never becomes a label", () => {
    expect(getClauseRiskTag("High")?.label).not.toBe("High");
    expect(getClauseRiskTag("Medium")?.label).not.toBe("Medium");
    expect(getClauseRiskTag("Low")?.label).not.toBe("Low");
    expect(getClauseRiskTag("Critical")?.label).not.toBe("Critical");
  });

  it("leverageWhy is null when risk was never determined", () => {
    expect(leverageWhy("High")).toContain("costs money");
    expect(leverageWhy("Medium")).toContain("Worth raising");
    expect(leverageWhy("Low")).toContain("Usual language");
    expect(leverageWhy(null)).toBeNull();
    expect(LEVERAGE_LEGEND).toBe("Push to change · Worth raising · Standard terms");
  });

  it("buildClauseRows carries type · value · leverage · why · viewer href; a sourced value shows, an unsourced unofficialized one stays a dashed row", () => {
    const documents: Contract360DocumentBody[] = [
      { documentId: "doc-1", fileName: "MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Completed", createdAt: "x" },
    ];
    // Below the auto-accept bar but pointing at p.27: sourced, so the value reads (same rule as products).
    const [row] = buildClauseRows([clause()], documents, AUTO_ACCEPT_THRESHOLD);
    expect(row).toEqual({
      clauseId: "cl-1",
      type: "Liability cap",
      normalized: "12 months fees",
      risk: { variant: "neutral", label: LEVERAGE_WORTH_RAISING },
      why: "Worth raising in negotiation.",
      viewerHref: "/documents/doc-1/viewer?page=27&clause=cl-1",
    });

    expect(buildClauseRows([clause({ sourcePage: null, sourceSpan: null })], documents)[0].normalized).toBe(UNOFFICIALIZED_PLACEHOLDER);

    const accepted = buildClauseRows([clause({ confidence: 0.97 })], documents)[0];
    expect(accepted.normalized).toBe("12 months fees");
    expect(accepted.viewerHref).toBe("/documents/doc-1/viewer?page=27&clause=cl-1");

    expect(buildClauseRows([clause({ normalizedValue: null, sourceDocumentId: null, confidence: 0.97 })], documents)[0]).toMatchObject({
      normalized: "Liability is capped at 12 months fees, save for confidentiality.",
      viewerHref: null,
    });
    expect(buildClauseRows([clause({ sourcePage: null, confidence: 0.97 })], documents)[0].viewerHref).toBeNull();
  });

  it("clauseViewerHref needs both a family document and a source page", () => {
    const documents: Contract360DocumentBody[] = [
      { documentId: "doc-1", fileName: "MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Completed", createdAt: "x" },
    ];
    expect(clauseViewerHref(clause(), documents)).toBe("/documents/doc-1/viewer?page=27&clause=cl-1");
    expect(clauseViewerHref(clause({ sourceDocumentId: "missing" }), documents)).toBeNull();
    expect(clauseViewerHref(clause({ sourcePage: null }), documents)).toBeNull();
  });

  it("the short reference is capped at 60 characters and the untruncated quote never reaches the citation", () => {
    const longSpan = `§${"A".repeat(80)}`;
    const documents: Contract360DocumentBody[] = [
      { documentId: "doc-1", fileName: "MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Completed", createdAt: "x" },
    ];
    const short = formatShortReference({ sourcePage: 27, sourceSpan: longSpan });
    expect(short).not.toBeNull();
    expect(short!.length).toBeLessThanOrEqual("p.27 · ".length + 60);
    expect(short).toContain("…");
    expect(short).not.toContain("A".repeat(80));

    const evidence = buildClauseEvidence(clause({ sourceSpan: longSpan }), documents);
    expect(evidence.citation).toContain("p.27");
    expect(evidence.citation).toContain("…");
    expect(evidence.citation).not.toContain("A".repeat(80));
    expect(evidence.quote).toBe("12 months fees");
  });

  it("a quoted span is never printed as a section: only a section label earns the §", () => {
    const quote = "Aggregate liability is capped at 2x fees paid in the preceding 12 months.";
    expect(formatShortReference({ sourcePage: 2, sourceSpan: quote })).toBe("p.2");
    expect(formatShortReference({ sourcePage: null, sourceSpan: quote })).toBeNull();
    expect(formatShortReference({ sourcePage: 12, sourceSpan: "8.4" })).toBe("p.12 · §8.4");
    expect(formatShortReference({ sourcePage: 12, sourceSpan: "Section 8.4" })).toBe("p.12 · §8.4");
    expect(formatShortReference({ sourcePage: 12, sourceSpan: "Art. 5" })).toBe("p.12 · §5");
    expect(formatShortReference({ sourcePage: 12, sourceSpan: "§ 17.2" })).toBe("p.12 · § 17.2");
    // A quote-only row is still sourced, so its value is shown rather than dashed.
    expect(isExtractedRowShown({ confidence: 0.5, sourcePage: null, sourceSpan: quote }, AUTO_ACCEPT_THRESHOLD)).toBe(true);
    expect(isExtractedRowShown({ confidence: 0.5, sourcePage: null, sourceSpan: "  " }, AUTO_ACCEPT_THRESHOLD)).toBe(false);
  });

  it("buildClauseEvidence marks the normalised value inside the raw text, else the whole wording, and cites file · p.N · §", () => {
    const documents: Contract360DocumentBody[] = [{ documentId: "doc-1", fileName: "MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Completed", createdAt: "x" }];
    expect(buildClauseEvidence(clause(), documents)).toEqual({
      citation: "MSA.pdf · p.27 · §17.2",
      before: "Liability is capped at ",
      quote: "12 months fees",
      after: ", save for confidentiality.",
    });
    expect(buildClauseEvidence(clause({ normalizedValue: "Capped at twelve months" }), documents)).toMatchObject({
      before: "",
      quote: "Liability is capped at 12 months fees, save for confidentiality.",
      after: "",
    });
    expect(buildClauseEvidence(clause({ sourceSpan: "17.2", sourcePage: null, sourceDocumentId: null }), documents).citation).toBe("§17.2");
    expect(buildClauseEvidence(clause({ sourceSpan: null, sourcePage: null, sourceDocumentId: null }), documents).citation).toBe("Liability cap");
  });

  it("resolveHighlightedClauseId matches by clause id, else by page only when no clause id was given", () => {
    const clauses = [clause({ clauseId: "cl-1", sourcePage: 27 }), clause({ clauseId: "cl-2", sourcePage: 9 })];
    expect(resolveHighlightedClauseId(clauses, "cl-2", null)).toBe("cl-2");
    expect(resolveHighlightedClauseId(clauses, null, "27")).toBe("cl-1");
    expect(resolveHighlightedClauseId(clauses, "does-not-exist", "9")).toBeNull();
    expect(resolveHighlightedClauseId(clauses, null, "999")).toBeNull();
    expect(resolveHighlightedClauseId(clauses, null, "not-a-number")).toBeNull();
    expect(resolveHighlightedClauseId(clauses, null, null)).toBeNull();
  });
});

describe("the six sections (Raffa.ai V2.dc.html CONTRACT 360)", () => {
  it("labels", () => {
    expect(SECTION_COPY.terms).toEqual({ number: "01", title: "Key terms", description: "The facts in one glance. Validated during review — no sources here." });
    expect(SECTION_COPY.leverage.number).toBe("02");
    expect(SECTION_COPY.risks.number).toBe("06");
    expect(formatReviewCountLine(2)).toBe("2 facts still need you — Review all →");
    expect(formatReviewCountLine(1)).toBe("1 facts still need you — Review all →");
    expect(standardClausesLabel(6, false)).toBe("Show 6 standard clauses ▾");
    expect(standardClausesLabel(1, true)).toBe("Hide 1 standard clause ▴");
  });

  it("buildKeyTerms reads the real contract-level fields; unofficialized values keep the row as an em-dash", () => {
    const rows = buildKeyTerms(contractBody(), acceptedEvidence());
    const byKey = Object.fromEntries(rows.map((row) => [row.key, row.value]));
    expect(byKey).toMatchObject({
      annualSpend: "CHF 500,000",
      totalContractValue: "CHF 1,500,000",
      term: "01/01/2025 → 01/01/2026",
      cancellationDeadline: "17/11/2025",
      autoRenewal: "Yes",
      renewalTermMonths: "12 months",
      paymentTerms: "Net 45",
      governingLaw: "Switzerland, Zürich",
      effectiveDate: "01/01/2025",
      lineItemCount: "1",
    });
    expect(rows.every((row) => row.source === null)).toBe(true);
    expect(rows.some((row) => row.key === "parentContractId")).toBe(false);
    expect(rows).toHaveLength(10);

    const mixed = buildKeyTerms(contractBody(), [
      ...acceptedEvidence().filter((row) => row.fieldName !== "annualSpend"),
      fieldEvidence({ fieldName: "annualSpend", decision: "review_required", confidence: 0.71 }),
    ]);
    expect(mixed.find((row) => row.key === "annualSpend")?.value).toBe(UNOFFICIALIZED_PLACEHOLDER);
    expect(mixed.find((row) => row.key === "totalContractValue")?.value).toBe("CHF 1,500,000");
    expect(mixed).toHaveLength(10);
  });

  it("02 Leverage: one card per lever type, strongest first -- identical wording shown once, differing wording names its line", () => {
    const quarterEnd = "Today (2026-09-22) is within 8 day(s) of a calendar quarter-end.";
    const pack = {
      contractId: "c-1",
      whenYouMustMove: { renewalDate: null, cancellationDeadline: null, daysLeft: null, passedDeadline: false, explanation: "" },
      whereYouCanPush: [
        { leverType: "Volume" as const, rationale: "SAP S/4HANA: This line orders 570 — cite the order size.", citationKeys: ["k-1"] },
        { leverType: "Term" as const, rationale: "SAP S/4HANA: Committed term is 24 months.", citationKeys: [] },
        { leverType: "QuarterEnd" as const, rationale: `SAP S/4HANA: ${quarterEnd}`, citationKeys: [] },
        { leverType: "Volume" as const, rationale: "Onboarding fee: No quantity is recorded on this line.", citationKeys: [] },
        { leverType: "Term" as const, rationale: "Onboarding fee: Committed term is 24 months.", citationKeys: [] },
        { leverType: "QuarterEnd" as const, rationale: `Onboarding fee: ${quarterEnd}`, citationKeys: [] },
      ],
      targets: [],
      nextSteps: [],
      openWeakFacts: [],
    };
    const cards = buildLeverCards(pack, ["SAP S/4HANA", "Onboarding fee"]);
    expect(cards.map((card) => [card.kicker, card.headline, card.entries, card.strong])).toEqual([
      [
        "Order size lever",
        "Order size",
        [
          { lines: ["SAP S/4HANA"], body: "This line orders 570 — cite the order size." },
          { lines: ["Onboarding fee"], body: "No quantity is recorded on this line." },
        ],
        true,
      ],
      ["Term length lever", "Term length", [{ lines: [], body: "Committed term is 24 months." }], false],
      ["Quarter end lever", "Quarter end", [{ lines: [], body: quarterEnd }], false],
    ]);
    expect(JSON.stringify(cards)).not.toContain("k-1");

    // Three lines, two sharing a wording: the shared wording names both lines, the odd one its own.
    const threeLines = buildLeverCards(
      {
        ...pack,
        whereYouCanPush: [
          { leverType: "Term" as const, rationale: "A: Committed term is 12 months.", citationKeys: [] },
          { leverType: "Term" as const, rationale: "B: Committed term is 12 months.", citationKeys: [] },
          { leverType: "Term" as const, rationale: "C: Committed term is 36 months.", citationKeys: [] },
          { leverType: "Bundle" as const, rationale: "A: This contract bundles 3 priced lines.", citationKeys: [] },
        ],
      },
      ["A", "B", "C"],
    );
    expect(threeLines.map((card) => card.entries)).toEqual([
      [
        { lines: ["A", "B"], body: "Committed term is 12 months." },
        { lines: ["C"], body: "Committed term is 36 months." },
      ],
      // A lever only one line carries still names that line.
      [{ lines: ["A"], body: "This contract bundles 3 priced lines." }],
    ]);

    // A single-line contract carries no prefix: every card unlabelled, the rationale untouched.
    const single = buildLeverCards(pack, []);
    expect(single[0].entries).toEqual([
      { lines: [], body: "SAP S/4HANA: This line orders 570 — cite the order size." },
      { lines: [], body: "Onboarding fee: No quantity is recorded on this line." },
    ]);
    expect(buildLeverCards({ ...pack, whereYouCanPush: [pack.whereYouCanPush[0]] }, [])[0].entries).toEqual([
      { lines: [], body: "SAP S/4HANA: This line orders 570 — cite the order size." },
    ]);
    expect(buildLeverCards(null)).toEqual([]);
  });

  it("02 Products & pricing: pay figures from the line; before any market comparison, market and delta an honest dash; unofficialized lines dashed but kept", () => {
    const [line] = buildProductLines([product()], "CHF");
    expect(line).toMatchObject({ name: "Premium DBU — committed", meta: "SKU-1 · DBU/yr", marketBasis: null, qty: "120,000", price: "CHF 0.55", market: "—", marketMeta: "", marketTitle: null, delta: "—", payWidth: "100%", marketWidth: "0%", saving: "—", savingAccent: false, annual: "CHF 66,000" });
    const [sourced] = buildProductLines([product({ confidence: 0.71 })], "CHF");
    expect(sourced.price).toBe("CHF 0.55");
    const [unofficial] = buildProductLines([product({ confidence: 0.71, sourceSpan: null, sourcePage: null })], "CHF");
    expect(unofficial).toMatchObject({ name: "Premium DBU — committed", qty: UNOFFICIALIZED_PLACEHOLDER, price: UNOFFICIALIZED_PLACEHOLDER, annual: UNOFFICIALIZED_PLACEHOLDER, payWidth: "0%" });
    expect(buildProductNote([product()])).toBe(PRODUCT_NOTE_UNCHECKED);
  });

  it("02 Products & pricing: a matched line shows the market P50 with region · term · n, the delta vs P50 and bars scaled to the larger", () => {
    const [above] = buildProductLines([product({ market: marketBand() })], "CHF");
    expect(above).toMatchObject({ marketBasis: null, market: "CHF 0.5", marketMeta: "CH · 12 mo · 40 contracts", delta: "+10%", deltaAccent: true, payWidth: "100%", marketWidth: "91%" });
    // 120,000 units a year: 0.05 over the median, 0.15 over P25.
    expect(above).toMatchObject({ saving: "CHF 6–18k", savingAccent: true });
    expect(above.marketTitle).toBe("Premium DBU · P25 CHF 0.4 – P75 CHF 0.6 · representative market data · mock feed · updated 2026-07-01");

    const [below] = buildProductLines([product({ unitPrice: 0.4, market: marketBand() })], "CHF");
    expect(below).toMatchObject({ delta: "-20%", deltaAccent: false, payWidth: "80%", marketWidth: "100%", saving: "none", savingAccent: false });
    expect(formatVersusMarket(0.5, 0.5)).toBe("0%");

    // Compared, nothing comparable: an honest dash that says so, the pay bar full.
    const noMatch = marketBand({ matched: false, recordId: null, product: null, geography: null, currency: null, termMonths: null, unitPriceP25: null, unitPriceP50: null, unitPriceP75: null, sampleSize: null, provenance: null, marketUpdatedAt: null });
    const [unmatched] = buildProductLines([product({ market: noMatch })], "CHF");
    expect(unmatched).toMatchObject({ market: "—", marketMeta: "no match", marketTitle: null, delta: "—", deltaAccent: false, payWidth: "100%", marketWidth: "0%", saving: "—" });

    // An unofficialized price still shows the market figure, but no delta is drawn against a hidden price.
    const [hidden] = buildProductLines([product({ confidence: 0.71, sourceSpan: null, sourcePage: null, market: marketBand() })], "CHF");
    expect(hidden).toMatchObject({ price: UNOFFICIALIZED_PLACEHOLDER, market: "CHF 0.5", delta: "—", payWidth: "0%", marketWidth: "0%", saving: "—" });

    expect(buildProductNote([product({ market: noMatch })])).toBe(PRODUCT_NOTE_NO_MATCH);
    // Once a line has a market price the page carries no note: the header tooltips explain it, in plain words.
    expect(buildProductNote([product({ market: noMatch }), product({ market: marketBand() })])).toBeNull();
    expect(MARKET_MEDIAN_EXPLAINED[0]).toMatch(/half pay less, half pay more/);
    expect(MARKET_MEDIAN_EXPLAINED[1]).toMatch(/how many contracts.*Under 10.*indicative/);
    expect(JSON.stringify([MARKET_MEDIAN_EXPLAINED, SAVING_COLUMN_EXPLAINED, SAVE_EXPLAINED])).not.toMatch(/P50|P25|n=/);
    expect(buildProductSavingTotal([product({ market: marketBand() }), product({ market: noMatch })], "CHF")).toBe("CHF 6–18k");
    expect(buildProductSavingTotal([product({ market: noMatch })], "CHF")).toBeNull();
  });

  it("02 Products & pricing: a bundle says it is summed and a similar product is marked ≈, never read as the line's own price", () => {
    const [bundle] = buildProductLines(
      [product({ unitPrice: 191, annualCost: 149_000, market: marketBand({ product: "Jira Software Premium + Confluence Premium", geography: "UK", currency: "GBP", unitPriceP25: 185.3, unitPriceP50: 205, sampleSize: 6, matchKind: "Bundle" }) })],
      "GBP",
    );
    expect(bundle).toMatchObject({
      marketBasis: "Market: Jira Software Premium + Confluence Premium, priced one by one and added up",
      market: "GBP 205",
      marketMeta: "sum · UK · 12 mo · only 6 contracts",
      delta: "-7%",
      deltaAccent: false,
      saving: "up to GBP 4.4k",
    });

    const similarBand = marketBand({ product: "Confluence Premium", geography: "UK", currency: "GBP", unitPriceP25: 76.3, unitPriceP50: 85, sampleSize: 48, matchKind: "Similar" });
    const [similar] = buildProductLines([product({ unitPrice: 100, annualCost: 10_000, market: similarBand })], "GBP");
    expect(similar).toMatchObject({
      marketBasis: "Market: a similar product, not yours — Confluence Premium",
      market: "≈ GBP 85",
      marketMeta: "similar · UK · 12 mo · 48 contracts",
      delta: "≈ +18%",
      saving: "≈ GBP 1.5–2.4k",
    });
    expect(buildProductSavingTotal([product({ unitPrice: 100, annualCost: 10_000, market: similarBand })], "GBP")).toBe("≈ GBP 1.5–2.4k");
  });

  it("03 Clauses that matter: High/Critical push, Medium raise, the rest standard; the ask slot is the leverage copy", () => {
    const groups = buildClauseGroups(
      [
        clause({ clauseId: "cl-high", clauseType: "Price increase", riskLevel: "High", confidence: 0.97 }),
        clause({ clauseId: "cl-med", clauseType: "Liability cap", riskLevel: "Medium", confidence: 0.97 }),
        clause({ clauseId: "cl-low", clauseType: "Confidentiality", riskLevel: "Low", confidence: 0.97 }),
        clause({ clauseId: "cl-weak", clauseType: "Audit", riskLevel: "Low", confidence: 0.6 }),
        clause({ clauseId: "cl-none", clauseType: "Notices", riskLevel: null, confidence: 0.6, sourcePage: null, sourceSpan: null }),
      ],
      contractBody().tabs.documents,
    );
    expect(groups.groups.map((group) => [group.label, group.tag, group.items.map((item) => item.clauseId)])).toEqual([
      [LEVERAGE_PUSH_TO_CHANGE, "accent", ["cl-high"]],
      [LEVERAGE_WORTH_RAISING, "neutral", ["cl-med"]],
    ]);
    expect(groups.groups[0].items[0].ask).toBe("This clause costs money if it stays.");
    expect(groups.groups[0].items[0].source).toBe("p.27 · §17.2");
    expect(groups.groups[0].items[0].viewerHref).toBe("/documents/doc-1/viewer?page=27&clause=cl-high");
    expect(groups.standard.map((item) => [item.clauseId, item.ask, item.normalized])).toEqual([
      ["cl-low", null, "12 months fees"],
      // Below the auto-accept bar but sourced: the value reads, as it does for products.
      ["cl-weak", null, "12 months fees"],
      // Neither officialized nor sourced: the em dash, the row stays.
      ["cl-none", null, UNOFFICIALIZED_PLACEHOLDER],
    ]);
    expect(buildClauseGroups([clause({ riskLevel: "Low" })]).groups).toEqual([]);
  });

  it("04 Obligations: the customer side is 'you', dated items first, criticality drives the dot", () => {
    const columns = buildObligationColumns([
      obligation({ obligationId: "o-late", party: "Customer", dueDate: "2026-06-01", criticality: "medium", recurrenceRule: null }),
      obligation({ obligationId: "o-soon", party: "Customer", dueDate: "2026-01-15", criticality: "high", recurrenceRule: null }),
      obligation({ obligationId: "o-ongoing", party: "Customer", dueDate: null, recurrenceRule: "monthly", criticality: "low" }),
      obligation({ obligationId: "o-sup", party: "Supplier", dueDate: null, recurrenceRule: null, criticality: "high", description: "Maintain 99.9% uptime" }),
    ]);
    expect(columns.you.map((item) => [item.key, item.when, item.recurrence, item.dot, item.strong])).toEqual([
      ["o-soon", "by 15/01/2026", "once", "accent", true],
      ["o-late", "by 01/06/2026", "once", "text", false],
      ["o-ongoing", "—", "monthly", "neutral", false],
    ]);
    expect(columns.supplier).toHaveLength(1);
    expect(columns.supplier[0]).toMatchObject({ text: "Maintain 99.9% uptime", when: "—", recurrence: "once", dot: "accent" });
    expect(buildObligationColumns([obligation({ confidence: 0.71 })]).you[0].text).toBe("Annual true-up of committed DBU");
    const [unofficial] = buildObligationColumns([obligation({ confidence: 0.71, sourceSpan: null, sourcePage: null })]).you;
    expect(unofficial.text).toBe(UNOFFICIALIZED_PLACEHOLDER);
  });

  it("05 Risk factors: score parts out of 20 with the accent fill at >= 80%, risks tagged by severity", () => {
    const parts = buildScoreParts(priority());
    expect(parts.map((part) => [part.label, part.value, part.width, part.accent])).toEqual([
      ["Spend weight", "20 / 20", "100%", true],
      ["Time urgency", "20 / 20", "100%", true],
      ["Benchmark opportunity", "10 / 20", "50%", false],
      ["Price-increase risk", "7 / 20", "35%", false],
      ["Contract risk", "15 / 20", "75%", false],
    ]);
    expect(buildScoreParts(null)).toEqual([]);
    expect(buildRiskItems([risk({ severity: "Critical" }), risk({ riskId: "r-2", severity: "Medium" }), risk({ riskId: "r-3", severity: "Low" })]).map((item) => item.tag)).toEqual(["accent", "neutral", "outline"]);
    expect(buildRiskItems([risk({ confidence: 0.5, sourceSpan: null, sourcePage: null })])[0].text).toBe(UNOFFICIALIZED_PLACEHOLDER);
  });

  it("close the cycle: a sent notice is a Completed action naming the date, read back as the terminated outcome", () => {
    expect(terminatedActionText("2026-09-08")).toBe("Terminated — notice sent 08/09/2026");
    const outcome = buildClosedOutcome("Terminated — notice sent 08/09/2026", contractBody().header, "CHF");
    expect(outcome.title).toBe("Terminated");
    expect(outcome.when).toBe("notice sent 08/09/2026");
    expect(outcome.kind).toBe("Notice sent");
    expect(outcome.facts).toEqual([
      { label: "Contract ends", value: "01/01/2026" },
      { label: "Spend avoided", value: "CHF 500,000 / yr" },
      { label: "Auto-renewal", value: "blocked" },
    ]);
    const other = buildClosedOutcome("Signed", contractBody().header, "CHF");
    expect(other.title).toBe("Closed");
    expect(other.when).toBe("Signed");
    expect(other.facts).toEqual([]);
  });

  it("buildDocumentRows gives type · file · status tag", () => {
    const [row] = buildDocumentRows(contractBody().tabs.documents);
    expect(row.documentId).toBe("doc-1");
    expect(row.type).toBe("MSA");
    expect(row.fileName).toBe("MSA.pdf");
    expect(row.status.label.length).toBeGreaterThan(0);
  });

  it("priority breakdown: five named components, or an empty list and an honest fact when missing", () => {
    const rows = buildPriorityComponentRows(priority());
    expect(rows.map((r) => r.label)).toEqual(["Spend weight", "Time urgency", "Benchmark opportunity", "Price-increase risk", "Contract risk"]);
    expect(rows[0].score).toBe(20);
    expect(rows[0].explanation).toContain("500,000 or more");
    expect(buildPriorityComponentRows(null)).toEqual([]);
    expect(formatPriorityFact(priority({ totalScore: 71.6 }))).toBe("priority 72/100");
    expect(formatPriorityFact(null)).toBe("priority not yet available");
  });

  it("computeNeedsAttention counts review_required decisions, not a tag variant or a percentage", () => {
    const evidence = [
      fieldEvidence({ fieldName: "annualSpend", decision: "auto_accepted", confidence: 0.71 }),
      fieldEvidence({ fieldName: "paymentTerms", decision: "human_accepted", confidence: 0.4 }),
      fieldEvidence({ fieldName: "endDate", decision: "review_required", confidence: 0.99 }),
      fieldEvidence({ fieldName: "governingLaw", decision: "review_required", confidence: 0.2 }),
    ];
    expect(computeNeedsAttention(evidence)).toBe(2);
    expect(computeNeedsAttention(evidence.filter((row) => row.decision !== "review_required"))).toBe(0);
    expect(computeNeedsAttention([])).toBe(0);
    expect(formatReviewCountLine(2)).toBe("2 facts still need you — Review all →");
  });
});
