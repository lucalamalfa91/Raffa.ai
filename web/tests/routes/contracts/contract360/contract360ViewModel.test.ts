import { describe, expect, it } from "vitest";
import type {
  Contract360Body,
  Contract360ClauseBody,
  Contract360DocumentBody,
  Contract360HeaderBody,
  Contract360ObligationBody,
  Contract360ProductBody,
  Contract360RiskBody,
  RenewalPipelineItemBody,
  RenewalPriorityBody,
} from "../../../../src/api/client";
import {
  DETAILS_LABEL_CLOSED,
  DETAILS_LABEL_OPEN,
  LEVER_NOT_YET_AVAILABLE,
  NO_ATTENTION_MESSAGE,
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
  computeNeedsAttention,
  formatHeaderMeta,
  formatPriorityFact,
  formatTrackerMeta,
  getClauseRiskTag,
  resolveBackLink,
  resolveHighlightedClauseId,
  resolveSupplierLabel,
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

function contractBody(overrides: Partial<Contract360Body["tabs"]> = {}): Contract360Body {
  return {
    contractId: CONTRACT_ID,
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

  it("Where you can save renders the pipeline's own figures, or honest 'not yet' copy", () => {
    const unknown = buildAnswers(header(), renewalTab, [renewalItem()], new Date("2025-11-10T00:00:00Z"));
    expect(unknown.save).toEqual({ estimate: "Not yet available", lever: LEVER_NOT_YET_AVAILABLE });

    const uplift = renewalItem();
    uplift.insightCard.recommendations.annualUpliftPercent = 7;
    expect(buildAnswers(header(), renewalTab, [uplift]).save.lever).toBe("A 7% uplift clause applies at renewal.");

    const known = renewalItem();
    known.insightCard.recommendations.potentialSavingsRange = "CHF 80–120k / yr";
    known.insightCard.recommendations.marketPosition = "9% above market";
    expect(buildAnswers(header(), renewalTab, [known]).save).toEqual({ estimate: "CHF 80–120k / yr", lever: "9% above market" });
  });

  it("When you must move: deadline, days left, urgency, term end and auto-renewal", () => {
    const soon = buildAnswers(header(), renewalTab, [], new Date("2025-11-10T00:00:00Z")).move;
    expect(soon.deadline).toBe("17/11/2025");
    expect(soon.cancelDays).toBe(7);
    expect(soon.isUrgent).toBe(true);
    expect(soon.detail).toBe("in 7 days — last day to give notice. Term ends 01/01/2026 and auto-renews for 12 months.");

    const far = buildAnswers(header({ cancellationDeadline: "2026-06-01", endDate: "2026-09-01" }), { ...renewalTab, renewalTermMonths: null }, [], new Date("2025-11-10T00:00:00Z")).move;
    expect(far.isUrgent).toBe(false);
    expect(far.detail).toBe("in 203 days — last day to give notice. Term ends 01/09/2026 and auto-renews.");

    const past = buildAnswers(header({ autoRenewal: false }), { ...renewalTab, autoRenewal: false }, [], new Date("2025-12-01T00:00:00Z")).move;
    expect(past.detail).toBe("14 days ago — the notice window has closed. Term ends 01/01/2026.");

    const none = buildAnswers(header({ cancellationDeadline: null, endDate: null }), renewalTab, []).move;
    expect(none.deadline).toBe("Not determined");
    expect(none.cancelDays).toBeNull();
    expect(none.isUrgent).toBe(false);
    expect(none.detail).toBe("No notice deadline determined. Term end not recorded and auto-renews for 12 months.");
  });

  it("the tracker steps and meta follow the real supplier and deadline", () => {
    expect(buildNegotiationSteps("Salesforce", "17/11/2025")).toEqual([
      { label: "Notify Salesforce of intent to renegotiate", due: "this week" },
      { label: "Request revised pricing and licence mix", due: "+10 days" },
      { label: "Counter with the market benchmark", due: "+20 days" },
      { label: "Sign, or send non-renewal notice", due: "by 17/11/2025" },
    ]);
    const answers = buildAnswers(header(), renewalTab, [], new Date("2025-11-10T00:00:00Z"));
    expect(formatTrackerMeta(answers.save, answers.move)).toBe("target Not yet available · close by 17/11/2025");
  });
});

describe("why — the clauses behind it", () => {
  it("getClauseRiskTag emphasises High/Critical only, text first; null without a level", () => {
    expect(getClauseRiskTag("High")).toEqual({ variant: "accent", label: "High" });
    expect(getClauseRiskTag("Critical")).toEqual({ variant: "accent", label: "Critical" });
    expect(getClauseRiskTag("Medium")).toEqual({ variant: "neutral", label: "Medium" });
    expect(getClauseRiskTag(null)).toBeNull();
    expect(getClauseRiskTag("  ")).toBeNull();
  });

  it("buildClauseRows carries type · normalised · source · risk · confidence", () => {
    const [row] = buildClauseRows([clause()]);
    expect(row).toEqual({
      clauseId: "cl-1",
      type: "Liability cap",
      normalized: "12 months fees",
      source: "p.27 · §17.2",
      risk: { variant: "neutral", label: "Medium" },
      confidencePct: 78,
    });
    expect(buildClauseRows([clause({ normalizedValue: null, sourceDocumentId: null })])[0]).toMatchObject({
      normalized: "Liability is capped at 12 months fees, save for confidentiality.",
      source: null,
    });
  });

  it("buildClauseEvidence marks the normalised value inside the raw text, else the whole wording, and cites file · page · §", () => {
    const documents: Contract360DocumentBody[] = [{ documentId: "doc-1", fileName: "MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Completed", createdAt: "x" }];
    expect(buildClauseEvidence(clause(), documents)).toEqual({
      citation: "MSA.pdf · page 27 · §17.2",
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
    expect(NO_ATTENTION_MESSAGE).toBe("None — every fact is above 95% or signed off by you.");
  });

  it("buildKeyTerms reads the real contract-level fields, with no borrowed source or confidence", () => {
    const rows = buildKeyTerms(contractBody());
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
    expect(rows.every((row) => row.source === null && row.confidencePct === null)).toBe(true);
    expect(rows.some((row) => row.key === "parentContractId")).toBe(false);
  });

  it("extracted rows carry real confidence (as a percentage) and a formatted source", () => {
    const [productRow] = buildProductsRows([product()]);
    expect(productRow).toMatchObject({ term: "Premium DBU — committed", confidencePct: 97, source: "p.9 · §6.2" });
    expect(buildProductsRows([product({ sourceSpan: null, sourcePage: null })])[0].source).toBe("Linked document");
    expect(buildProductsRows([product({ sourceDocumentId: null })])[0].source).toBeNull();

    expect(buildObligationsRows([obligation()])[0].value).toBe("Annual true-up of committed DBU · due 15/01/2026 · high");
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

  it("computeNeedsAttention lists every sub-95% fact with its value, lowest confidence first, skipping accepted and unscored ones", () => {
    const tabs = contractBody({
      products: [product({ lineItemId: "p-accepted", confidence: 0.99 }), product({ lineItemId: "p-flagged", confidence: 0.88, description: "SQL Serverless DBU" })],
      clauses: [clause({ clauseId: "c-review", confidence: 0.71, clauseType: "Termination for convenience", normalizedValue: "Not permitted during the term." })],
      obligations: [obligation({ obligationId: "o-no-confidence", confidence: null })],
    }).tabs;

    const attention = computeNeedsAttention(tabs);
    expect(attention.map((a) => a.term)).toEqual(["Termination for convenience", "SQL Serverless DBU"]);
    expect(attention[0].value).toBe("Not permitted during the term.");
    expect(attention[0].confidencePct).toBe(71);
    expect(attention[0].tag.variant).toBe("outline");
    expect(attention[1].tag.variant).toBe("accent");
    expect(attention.some((a) => a.key === "obligation-o-no-confidence")).toBe(false);
  });
});
