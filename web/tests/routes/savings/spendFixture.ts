import type { SpendContractRow, SpendReport } from "../../../src/api/spend";

/** Eight fiscal quarters, FY 2026 and FY 2027 (calendar), "today" in Q4 2026. */
const periods = [2026, 2027].flatMap((fy) =>
  [1, 2, 3, 4].map((q) => ({
    key: `${fy}-Q${q}`,
    fiscalYear: fy,
    quarter: q,
    label: `Q${q} ${fy}`,
    from: `${fy}-${String((q - 1) * 3 + 1).padStart(2, "0")}-01`,
    to: `${fy}-${String(q * 3).padStart(2, "0")}-${q === 1 || q === 4 ? "31" : "30"}`,
  })),
);

const zero = () => [0, 0, 0, 0, 0, 0, 0, 0];

function row(over: Partial<SpendContractRow> & Pick<SpendContractRow, "contractId" | "supplier" | "category">): SpendContractRow {
  return {
    name: `${over.supplier} MSA`,
    currency: "CHF",
    rate: 1,
    billingPeriod: "annual",
    actual: zero(),
    committed: zero(),
    autoRenewal: zero(),
    toDecide: zero(),
    segments: [],
    decision: { decideBy: null, endsOn: null, autoRenew: false, renewalTermMonths: null, uplift: "" },
    notes: [],
    savingsContractual: zero(),
    savingsRealized: zero(),
    quantityReduction: zero(),
    objective: zero(),
    ...over,
  };
}

/** Coupa (Procurement, CHF): 100k a year, renews by itself in 2027, a case with a 10% objective.
 * Zscaler (Sicurezza, EUR at 0.9): 90k a year in CHF, ends 31 Mar 2027, to decide; a signed result. */
export function spendReport(): SpendReport {
  return {
    currency: "CHF",
    basis: "accrual",
    fiscalYearStartMonth: 1,
    currentFiscalYear: 2026,
    today: "2026-11-04",
    periods,
    fiscalYears: [
      { fiscalYear: 2026, label: "FY 2026", from: "2026-01-01", to: "2026-12-31", documented: 2, contracts: 2 },
      { fiscalYear: 2027, label: "FY 2027", from: "2027-01-01", to: "2027-12-31", documented: 1, contracts: 2 },
    ],
    contracts: [
      row({
        contractId: "coupa",
        supplier: "Coupa",
        category: "Procurement",
        actual: [25_000, 25_000, 25_000, 10_000, 0, 0, 0, 0],
        committed: [0, 0, 0, 15_000, 25_000, 0, 0, 0],
        autoRenewal: [0, 0, 0, 0, 0, 25_000, 25_000, 25_000],
        segments: [
          { from: "2026-04-16", to: "2027-04-15", kind: "documented", source: "contract", annualValue: 100_000, previousTermPrice: false },
          { from: "2027-04-16", to: "2028-04-15", kind: "autoRenewal", source: "renewalClause", annualValue: 100_000, previousTermPrice: true },
        ],
        decision: { decideBy: "2027-01-15", endsOn: "2027-04-15", autoRenew: true, renewalTermMonths: 12, uplift: "5%" },
        objective: [0, 0, 0, 0, 0, 2_000, 2_500, 2_500],
      }),
      row({
        contractId: "zscaler",
        supplier: "Zscaler",
        category: "Sicurezza",
        currency: "EUR",
        rate: 0.9,
        actual: [22_500, 22_500, 22_500, 7_500, 0, 0, 0, 0],
        committed: [0, 0, 0, 15_000, 22_500, 0, 0, 0],
        toDecide: [0, 0, 0, 0, 0, 22_500, 22_500, 22_500],
        segments: [{ from: "2025-04-01", to: "2027-03-31", kind: "documented", source: "contract", annualValue: 90_000, previousTermPrice: false }],
        decision: { decideBy: "2027-03-17", endsOn: "2027-03-31", autoRenew: false, renewalTermMonths: null, uplift: "" },
        savingsContractual: [3_000, 3_000, 3_000, 3_000, 3_000, 0, 0, 0],
        savingsRealized: [0, 3_000, 0, 0, 0, 0, 0, 0],
      }),
    ],
    excluded: [
      { contractId: "gb", supplier: "Sage", name: "Sage order", reason: "noRate", currency: "GBP" },
      { contractId: "nd", supplier: "Nodate", name: "Nodate order", reason: "noDates", currency: "CHF" },
    ],
    savings: [
      {
        id: "s1",
        contractId: "zscaler",
        supplier: "Zscaler",
        contract: "Zscaler MSA",
        kind: "term",
        countsAsNegotiation: true,
        amountAnnual: 13_333,
        currency: "EUR",
        amountAnnualReporting: 12_000,
        from: "2025-04-01",
        to: "2027-03-31",
        signedOn: "2025-03-20",
        note: "24-month term",
        baseline: "2024 contract",
        period: "24 months",
        method: "signed vs previous",
        evidence: "zscaler.pdf",
        realizedReporting: 3_000,
        provenTo: "2026-04-30",
        invoiceCount: 1,
        excluded: null,
      },
    ],
    objectives: [
      { contractId: "coupa", caseId: "case-coupa", supplier: "Coupa", kind: "p10", pct: 10, annualTarget: 10_000, from: "2027-04-16", to: "2028-04-15", owner: "Marta", assumedTermMonths: false, excluded: null },
    ],
    otherSavings: { savingsContractual: zero(), savingsRealized: zero(), quantityReduction: zero() },
  };
}
