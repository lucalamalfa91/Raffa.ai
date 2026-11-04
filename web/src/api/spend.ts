/**
 * Spesa e risparmi -- the response of `GET /api/savings/spend` (backend
 * `Raffa.Savings.Application.Spend.SpendReport`, camelCase). Every amount is in `currency` (the
 * workspace's reporting currency); every per-period list is aligned with `periods`. Nothing in it is
 * an estimate: contracts and savings that cannot be valued exactly are listed apart.
 */

export type SpendBasis = "accrual" | "cash";

/** documented: a term a document states · rolledOver: renewed by itself (binding) · autoRenewal: renews unless notice is given · toDecide: no automatic renewal. */
export type SpendSegmentKind = "documented" | "rolledOver" | "autoRenewal" | "toDecide";

export interface SpendPeriod {
  key: string;
  fiscalYear: number;
  quarter: number;
  label: string;
  from: string;
  to: string;
}

export interface SpendFiscalYear {
  fiscalYear: number;
  label: string;
  from: string;
  to: string;
  /** Contracts with a documented term in this fiscal year. */
  documented: number;
  /** Contracts in the totals. */
  contracts: number;
}

export interface SpendSegment {
  from: string;
  to: string;
  kind: SpendSegmentKind;
  source: string;
  /** In the reporting currency; null when the term states only a total value. */
  annualValue: number | null;
  previousTermPrice: boolean;
}

export interface SpendDecision {
  /** Last day to give notice (or to decide), when still ahead. */
  decideBy: string | null;
  /** End of the term that decision applies to. */
  endsOn: string | null;
  autoRenew: boolean;
  renewalTermMonths: number | null;
  uplift: string;
}

export interface SpendContractRow {
  contractId: string;
  supplier: string;
  name: string;
  category: string;
  currency: string;
  rate: number;
  billingPeriod: string | null;
  actual: number[];
  committed: number[];
  autoRenewal: number[];
  toDecide: number[];
  segments: SpendSegment[];
  decision: SpendDecision;
  notes: string[];
  savingsContractual: number[];
  savingsRealized: number[];
  quantityReduction: number[];
  objective: number[];
}

export interface SpendExclusion {
  contractId: string;
  supplier: string;
  name: string;
  /** noDates | noValue | noRate | noBillingPeriod */
  reason: string;
  currency: string;
}

export interface SpendSavingEntry {
  id: string;
  contractId: string | null;
  supplier: string;
  contract: string;
  kind: string;
  /** False for a quantity reduction (shown apart). */
  countsAsNegotiation: boolean;
  amountAnnual: number;
  currency: string;
  amountAnnualReporting: number | null;
  from: string | null;
  to: string | null;
  signedOn: string;
  note: string;
  baseline: string;
  period: string;
  method: string;
  evidence: string;
  realizedReporting: number;
  provenTo: string | null;
  invoiceCount: number;
  /** noRate | noPeriod | null */
  excluded: string | null;
}

export interface SpendObjectiveEntry {
  contractId: string;
  caseId: string;
  supplier: string;
  kind: string | null;
  pct: number | null;
  annualTarget: number | null;
  from: string | null;
  to: string | null;
  owner: string | null;
  assumedTermMonths: boolean;
  excluded: string | null;
}

export interface SpendOtherSavings {
  savingsContractual: number[];
  savingsRealized: number[];
  quantityReduction: number[];
}

export interface SpendReport {
  currency: string;
  basis: SpendBasis;
  fiscalYearStartMonth: number;
  currentFiscalYear: number;
  today: string;
  periods: SpendPeriod[];
  fiscalYears: SpendFiscalYear[];
  contracts: SpendContractRow[];
  excluded: SpendExclusion[];
  savings: SpendSavingEntry[];
  objectives: SpendObjectiveEntry[];
  otherSavings: SpendOtherSavings | null;
}

export interface SpendReportQuery {
  from?: number;
  to?: number;
  basis?: SpendBasis;
}
