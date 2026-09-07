import { describe, expect, it } from "vitest";
import type {
  Contract360Body,
  Contract360ClauseBody,
  Contract360HeaderBody,
  Contract360ObligationBody,
  Contract360ProductBody,
  Contract360RiskBody,
  RenewalPipelineItemBody,
  RenewalPriorityBody,
} from "../../../../src/api/client";
import {
  buildClausesRows,
  buildCommercialsRows,
  buildDocumentsRows,
  buildObligationsRows,
  buildOverviewDetailRows,
  buildPriorityComponentRows,
  buildProductsRows,
  buildRecommendation,
  buildRenewalFactRows,
  buildRisksRows,
  computeNeedsAttention,
  computeTopRisks,
  formatPriorityFact,
  toConfidencePercent,
} from "../../../../src/routes/contracts/contract360/contract360ViewModel";

const CONTRACT_ID = "11111111-1111-1111-1111-111111111111";

function header(overrides: Partial<Contract360HeaderBody> = {}): Contract360HeaderBody {
  return {
    contractId: CONTRACT_ID,
    supplierId: null,
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
    rawText: "12 months fees",
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

describe("toConfidencePercent", () => {
  it("converts a 0-1 fraction to a 0-100 percentage", () => {
    expect(toConfidencePercent(0.92)).toBe(92);
    expect(toConfidencePercent(0)).toBe(0);
  });

  it("passes null through unchanged", () => {
    expect(toConfidencePercent(null)).toBeNull();
  });
});

describe("row builders (deterministic facts)", () => {
  it("buildCommercialsRows formats money with currency and honours null aggregates", () => {
    const rows = buildCommercialsRows({
      annualSpend: 500_000,
      totalContractValue: null,
      currency: "CHF",
      paymentTerms: "Net 45",
      autoRenewal: true,
      renewalTermMonths: 12,
      lineItemCount: 0,
      lineItemAnnualCostTotal: null,
      lineItemTotalCostTotal: null,
    });

    expect(rows.find((r) => r.key === "annualSpend")?.value).toBe("CHF 500,000");
    expect(rows.find((r) => r.key === "totalContractValue")?.value).toBe("—");
    expect(rows.every((r) => r.confidencePct === null)).toBe(true);
  });

  it("buildProductsRows carries real confidence (converted to a percentage) and a formatted source", () => {
    const rows = buildProductsRows([product()]);

    expect(rows).toHaveLength(1);
    expect(rows[0].term).toBe("Premium DBU — committed");
    expect(rows[0].confidencePct).toBe(97);
    expect(rows[0].source).toBe("p.9 · §6.2");
  });

  it("buildProductsRows renders 'Linked document' when a source document exists with no page/span, and null source when there is none at all", () => {
    const withDocOnly = buildProductsRows([product({ sourceDocumentId: "doc-1", sourceSpan: null, sourcePage: null })]);
    const withoutDoc = buildProductsRows([product({ sourceDocumentId: null, sourceSpan: null, sourcePage: null })]);

    expect(withDocOnly[0].source).toBe("Linked document");
    expect(withoutDoc[0].source).toBeNull();
  });

  it("buildClausesRows appends the risk level to the value when present", () => {
    const rows = buildClausesRows([clause({ riskLevel: "Medium" })]);
    expect(rows[0].value).toContain("Medium risk");
  });

  it("buildObligationsRows joins description, due date, and criticality", () => {
    const rows = buildObligationsRows([obligation()]);
    expect(rows[0].value).toBe("Annual true-up of committed DBU · due 15/01/2026 · high");
  });

  it("buildRisksRows always includes severity as text, never colour-only", () => {
    const rows = buildRisksRows([risk({ severity: "Critical" })]);
    expect(rows[0].value).toContain("Critical risk");
  });

  it("buildDocumentsRows has no per-document confidence (there is nothing to grade)", () => {
    const rows = buildDocumentsRows([
      { documentId: "doc-1", fileName: "MSA.pdf", mimeType: "application/pdf", documentType: "Msa", processingStatus: "Completed", createdAt: "2026-01-01T00:00:00Z" },
    ]);
    expect(rows[0].confidencePct).toBeNull();
    expect(rows[0].source).toBe("application/pdf");
  });

  it("buildRenewalFactRows and buildOverviewDetailRows never carry a confidence either (contract-level fields, not extractions)", () => {
    const renewalRows = buildRenewalFactRows({
      endDate: "2026-01-01",
      renewalDate: "2026-01-01",
      cancellationDeadline: "2025-11-17",
      autoRenewal: true,
      renewalTermMonths: 12,
    });
    const overviewRows = buildOverviewDetailRows({
      currency: "CHF",
      effectiveDate: "2025-01-01",
      renewalTermMonths: 12,
      paymentTerms: "Net 45",
      governingLaw: "Switzerland, Zürich",
      parentContractId: null,
      version: 2,
      createdAt: "2025-01-01T00:00:00Z",
    });

    expect(renewalRows.every((r) => r.confidencePct === null)).toBe(true);
    expect(overviewRows.every((r) => r.confidencePct === null)).toBe(true);
    expect(overviewRows.find((r) => r.key === "governingLaw")?.value).toBe("Switzerland, Zürich");
  });
});

describe("buildPriorityComponentRows / formatPriorityFact", () => {
  it("returns all five named components with their real score and explanation", () => {
    const rows = buildPriorityComponentRows(priority());
    expect(rows.map((r) => r.label)).toEqual([
      "Spend weight",
      "Time urgency",
      "Benchmark opportunity",
      "Price-increase risk",
      "Contract risk",
    ]);
    expect(rows[0].score).toBe(20);
    expect(rows[0].explanation).toContain("500,000 or more");
  });

  it("returns an empty list, never a fabricated all-zero table, when priority is null", () => {
    expect(buildPriorityComponentRows(null)).toEqual([]);
  });

  it("formatPriorityFact renders the rounded total score out of 100, or an honest gap", () => {
    expect(formatPriorityFact(priority({ totalScore: 71.6 }))).toBe("priority 72/100");
    expect(formatPriorityFact(null)).toBe("priority not yet available");
  });
});

describe("computeNeedsAttention (AC-3 'Needs your attention')", () => {
  const tabs: Contract360Body["tabs"] = {
    overview: {
      currency: "CHF",
      effectiveDate: null,
      renewalTermMonths: null,
      paymentTerms: null,
      governingLaw: null,
      parentContractId: null,
      version: 1,
      createdAt: "2025-01-01T00:00:00Z",
    },
    commercials: {
      annualSpend: null,
      totalContractValue: null,
      currency: "CHF",
      paymentTerms: null,
      autoRenewal: true,
      renewalTermMonths: null,
      lineItemCount: 0,
      lineItemAnnualCostTotal: null,
      lineItemTotalCostTotal: null,
    },
    products: [
      product({ lineItemId: "p-accepted", confidence: 0.99 }),
      product({ lineItemId: "p-flagged", confidence: 0.88, description: "SQL Serverless DBU" }),
    ],
    clauses: [clause({ clauseId: "c-review", confidence: 0.71, clauseType: "Termination for convenience" })],
    obligations: [obligation({ obligationId: "o-no-confidence", confidence: null })],
    risks: [] as Contract360RiskBody[],
    documents: [],
    benchmark: [],
    renewal: { endDate: null, renewalDate: null, cancellationDeadline: null, autoRenewal: true, renewalTermMonths: null },
    activity: [],
  };

  it("excludes fields at or above 95% confidence and fields with no confidence at all", () => {
    const attention = computeNeedsAttention(tabs);
    expect(attention.map((a) => a.term)).not.toContain("Premium DBU — committed"); // 99% accepted
    expect(attention.some((a) => a.key === "obligation-o-no-confidence")).toBe(false); // null confidence
  });

  it("sorts lowest confidence first and caps at 3", () => {
    const attention = computeNeedsAttention(tabs);
    expect(attention.map((a) => a.term)).toEqual(["Termination for convenience", "SQL Serverless DBU"]);
    expect(attention[0].confidencePct).toBe(71);
    expect(attention[0].tag.variant).toBe("outline");
    expect(attention[1].tag.variant).toBe("accent");
  });
});

describe("computeTopRisks (AC-3 'Top risks')", () => {
  it("orders by severity (Critical > High > Medium > Low) and caps at 2", () => {
    const risks = [
      risk({ riskId: "low", severity: "Low" }),
      risk({ riskId: "critical", severity: "Critical" }),
      risk({ riskId: "medium", severity: "Medium" }),
      risk({ riskId: "high", severity: "High" }),
    ];

    const top = computeTopRisks(risks);
    expect(top.map((r) => r.riskId)).toEqual(["critical", "high"]);
  });

  it("breaks a severity tie by ascending confidence (the least-certain risk first)", () => {
    const risks = [
      risk({ riskId: "confident", severity: "High", confidence: 0.9 }),
      risk({ riskId: "uncertain", severity: "High", confidence: 0.6 }),
    ];

    expect(computeTopRisks(risks).map((r) => r.riskId)).toEqual(["uncertain", "confident"]);
  });
});

describe("buildRecommendation — facts vs AI separation (ADR-019 council decision)", () => {
  it("sources the real recommendedAction/explanation text from the matching GET /api/renewals item, never inventing copy", () => {
    const recommendation = buildRecommendation(header(), [renewalItem()]);

    expect(recommendation.hasRecommendation).toBe(true);
    expect(recommendation.statement).toBe("Start renewal negotiation now");
    expect(recommendation.rationale).toBe("Renews in 30 days with a cancellation notice due in 14 days.");
  });

  it("names an honest gap, not a fabricated recommendation, when this contract has no pipeline entry", () => {
    const recommendation = buildRecommendation(header(), []);

    expect(recommendation.hasRecommendation).toBe(false);
    expect(recommendation.statement).toMatch(/no renewal recommendation/i);
  });

  it("names the non-auto-renewal reason specifically when the header itself says autoRenewal is false", () => {
    const recommendation = buildRecommendation(header({ autoRenewal: false }), []);
    expect(recommendation.rationale).toMatch(/only for auto-renewing contracts/i);
  });

  it("always includes a real Cancellation-deadline driver from the header, independent of whether a recommendation exists", () => {
    const withRecommendation = buildRecommendation(header(), [renewalItem()], new Date("2025-11-10T00:00:00Z"));
    const withoutRecommendation = buildRecommendation(header(), [], new Date("2025-11-10T00:00:00Z"));

    expect(withRecommendation.drivers.find((d) => d.key === "cancellationDeadline")?.value).toBe("7 days");
    expect(withoutRecommendation.drivers.find((d) => d.key === "cancellationDeadline")?.value).toBe("7 days");
  });

  it("renders Market position / Potential savings as an honest 'Not yet available', never a guess (both are always null this wave)", () => {
    const recommendation = buildRecommendation(header(), [renewalItem()]);
    expect(recommendation.drivers.find((d) => d.key === "marketPosition")?.value).toBe("Not yet available");
    expect(recommendation.drivers.find((d) => d.key === "potentialSavings")?.value).toBe("Not yet available");
  });

  it("keeps the recommendation's statement/rationale/drivers structurally separate from every deterministic FactRow shape (never mixed, per ADR-019)", () => {
    const recommendation = buildRecommendation(header(), [renewalItem()]);

    // A Recommendation is never a FactRow and is never concatenated into one: it carries no
    // `source`/`confidencePct` keys at all, the two fields that identify a FactRow.
    expect(recommendation).not.toHaveProperty("source");
    expect(recommendation).not.toHaveProperty("confidencePct");

    // The recommendation's own real, Renewals-module-sourced text never leaks into any tab's
    // deterministic fact rows -- each row builder only ever reads its own tab's data, never the
    // renewal pipeline.
    const everyFactValue = [
      ...buildCommercialsRows({
        annualSpend: 1,
        totalContractValue: 1,
        currency: "CHF",
        paymentTerms: null,
        autoRenewal: true,
        renewalTermMonths: null,
        lineItemCount: 0,
        lineItemAnnualCostTotal: null,
        lineItemTotalCostTotal: null,
      }),
      ...buildProductsRows([product()]),
      ...buildClausesRows([clause()]),
      ...buildObligationsRows([obligation()]),
      ...buildRisksRows([risk()]),
    ].map((row) => row.value);

    expect(everyFactValue).not.toContain(recommendation.statement);
    expect(everyFactValue).not.toContain(recommendation.rationale);
  });
});
