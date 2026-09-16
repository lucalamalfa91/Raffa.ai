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
  DETAILS_LABEL_CLOSED,
  DETAILS_LABEL_OPEN,
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
  buildObligationsRows,
  buildPriorityComponentRows,
  buildProductsRows,
  buildRecommendation,
  buildRisksRows,
  clauseViewerHref,
  computeNeedsAttention,
  formatHeaderMeta,
  formatPriorityFact,
  formatReviewCountLine,
  formatShortReference,
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
    renewalDate: "2026-01-01",
    daysUntilRenewal: 30,
    annualSpend: 500_000,
    cancellationDeadline: "2025-11-17",
    daysUntilCancellationDeadline: 14,
    autoRenewal: true,
    action: "Start renewal negotiation now",
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
    expect(save).toEqual({ estimate: "1,500", lever: "This line orders 120,000 — cite the order size." });
  });

  it("state (ii): a representative band renders with adapter and sample size on the detail line, never bare", () => {
    const save = buildAnswers(header(), renewalTab, [], strategyCalled(strategyPack())).save;
    expect(save.estimate).toBe("1,500–1,800");
    expect(save.lever).toBe("representative · adapter A, n = 214 · as of 2026-01-01");
    expect(save.lever).toMatch(/adapter .+ n = /);
    expect(save.lever.toLowerCase()).toContain("representative");
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
    expect(neverCalled.save).toEqual({ estimate: "", lever: "" });

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
    expect(calledEmpty.save).toEqual({ estimate: SAVINGS_NOT_YET_AVAILABLE, lever: LEVER_NOT_YET_AVAILABLE });

    const calledFailed = buildAnswers(header(), renewalTab, [renewalItem()], strategyCalled(null));
    expect(calledFailed.save).toEqual({ estimate: SAVINGS_NOT_YET_AVAILABLE, lever: LEVER_NOT_YET_AVAILABLE });
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
    expect(formatTrackerMeta(answers.save, answers.move)).toBe("target 1,500–1,800 · close by 17/11/2025");
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

  it("buildClauseRows carries type · accepted value · leverage · why · viewer href; unofficialized values stay as a dashed row", () => {
    const documents: Contract360DocumentBody[] = [
      { documentId: "doc-1", fileName: "MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Completed", createdAt: "x" },
    ];
    const [row] = buildClauseRows([clause()], documents, AUTO_ACCEPT_THRESHOLD);
    expect(row).toEqual({
      clauseId: "cl-1",
      type: "Liability cap",
      normalized: UNOFFICIALIZED_PLACEHOLDER,
      risk: { variant: "neutral", label: LEVERAGE_WORTH_RAISING },
      why: "Worth raising in negotiation.",
      viewerHref: "/documents/doc-1/viewer?page=27&clause=cl-1",
    });

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

describe("details ▾", () => {
  it("labels", () => {
    expect(DETAILS_LABEL_CLOSED).toBe("All terms, documents and open facts ▾");
    expect(DETAILS_LABEL_OPEN).toBe("Hide details");
    expect(formatReviewCountLine(2)).toBe("2 facts still need you — Review all →");
    expect(formatReviewCountLine(1)).toBe("1 facts still need you — Review all →");
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

  it("extracted rows keep unofficialized values as an em-dash and never drop a row", () => {
    const [productRow] = buildProductsRows([product()]);
    expect(productRow).toMatchObject({ term: "Premium DBU — committed", value: expect.stringContaining("120,000"), source: "p.9 · §6.2" });
    expect(buildProductsRows([product({ sourceSpan: null, sourcePage: null })])[0].source).toBe("Linked document");
    expect(buildProductsRows([product({ sourceDocumentId: null })])[0].source).toBeNull();

    const unofficial = buildProductsRows([product({ lineItemId: "p-low", confidence: 0.71 })]);
    expect(unofficial).toHaveLength(1);
    expect(unofficial[0].value).toBe(UNOFFICIALIZED_PLACEHOLDER);
    expect(unofficial[0].term).toBe("Premium DBU — committed");

    expect(buildObligationsRows([obligation({ confidence: 0.97 })])[0].value).toBe("Annual true-up of committed DBU · due 15/01/2026 · high");
    expect(buildObligationsRows([obligation()])[0].value).toBe(UNOFFICIALIZED_PLACEHOLDER);
    expect(buildRisksRows([risk({ severity: "Critical" })])[0].value).toContain("Critical risk");
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
