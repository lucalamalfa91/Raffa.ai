import type { SavingsKpiSummaryBody, SavingsOpportunityBody } from "../../api/client";
import type { TrackedRenewalAction } from "../renewals/renewalActionStore";
import type { SemanticTag, TagVariant } from "../../styles/semantics";
import { formatAnnualSpend, formatSupplier } from "../contracts/portfolioTableFormatters";
import { formatContractRef } from "../renewals/renewalPipelineViewModel";

/**
 * Pure view-model helpers for the Home screen (route `/`, ADR-018 "/ (home)"; screens.md #9 "Home --
 * Savings"; ADR-020 screen 9; task E08/F02/US01/T01, us-01-savings-home AC-1 six KPI cells / AC-2
 * opportunities table / AC-3 rows + stale-labelled error state). Same one-concern-per-file split
 * `../renewals/renewalPipelineViewModel.ts`/`../contracts/portfolioTableFormatters.ts` already
 * established for this repo: no React here, so every rule below is unit-testable without rendering
 * anything.
 */

const INTEGER_FORMAT = new Intl.NumberFormat("en-GB");

/** Same "no currency-conversion service, so group by currency, never sum across them" discipline
 * `SavingsRangeByCurrency`'s own backend doc comment states -- a cell with more than one currency
 * bucket renders one line per currency (KpiRow.tsx), never a silently-summed cross-currency figure. */
function formatCurrencyAmount(currency: string, amount: number): string {
  return `${currency} ${INTEGER_FORMAT.format(Math.round(amount))}`;
}

/** Low === high (e.g. a single realized value) collapses to one figure instead of a zero-width range. */
function formatCurrencyRange(currency: string, low: number, high: number): string {
  const lowText = INTEGER_FORMAT.format(Math.round(low));
  const highText = INTEGER_FORMAT.format(Math.round(high));
  return low === high ? `${currency} ${lowText}` : `${currency} ${lowText}–${highText}`;
}

function formatContractCountMeta(count: number): string {
  return `${count} contract${count === 1 ? "" : "s"}`;
}

/** AC-3's "Not yet available" honest-gap convention, quoted from `../renewals/renewalPipelineViewModel.ts`'s own identically-named constant (kept as a private local copy, not imported, the same "small literal constants stay per-file" convention that file's own `NOT_YET_AVAILABLE` already follows). */
const NOT_YET_AVAILABLE = "Not yet available";

const UNASSIGNED_OWNER_LABEL = "Unassigned";

// ---------------------------------------------------------------------------------------------
// KPI row (AC-1) + AC-3's "benchmark-provider-unreachable -> KPIs stale-labelled" state
// ---------------------------------------------------------------------------------------------

/**
 * The KPI row's own fetch state -- deliberately `loading | ready` only, never a blocking `error`
 * state like `../renewals/index.tsx`'s `FetchState`: screens.md #9's own state list names this
 * screen's "error" as "benchmark provider unreachable; KPIs stale-labelled", not a full-screen
 * block -- the last successfully-fetched summary (or `null`, before the first successful fetch)
 * stays on screen, tagged `stale`, rather than being replaced by an error block. Quoted from the
 * compiled prototype's own copy for this exact state (`inputs/design/prototypes/day1-demo.html`,
 * the Home screen's `ds.error` block): "Spend and renewal KPIs are current; savings figures are from
 * the last successful refresh ... Contigo does not show percentiles without provenance."
 */
export type KpiFetchState =
  | { phase: "loading" }
  | { phase: "ready"; kpis: SavingsKpiSummaryBody | null; stale: boolean };

export type KpiFetchOutcome = { ok: true; kpis: SavingsKpiSummaryBody } | { ok: false };

/**
 * The one pure state-transition this screen's own required test proves ("benchmark-unreachable ->
 * KPIs stale-labelled"). A successful fetch always replaces the summary and clears `stale`; a failed
 * fetch keeps whichever summary this reducer already had (or `null`, if none has ever resolved) and
 * sets `stale: true` -- so a transient failure degrades the KPI row to "last known, marked stale",
 * never a blank one, matching the prototype's own "KPIs are ... from the last successful refresh"
 * copy quoted on `KpiFetchState`'s own doc comment above.
 */
export function reduceKpiFetch(previous: KpiFetchState, outcome: KpiFetchOutcome): KpiFetchState {
  if (outcome.ok) {
    return { phase: "ready", kpis: outcome.kpis, stale: false };
  }
  const previousKpis = previous.phase === "ready" ? previous.kpis : null;
  return { phase: "ready", kpis: previousKpis, stale: true };
}

export interface KpiCellView {
  /** Stable id for React keys/tests -- not shown in the UI. */
  key: string;
  /** Exact label text, quoted verbatim from AC-1 / screens.md #9's own six-cell list. */
  label: string;
  /** One formatted line per currency bucket (or a single plain-count line for the two non-currency
   * cells) -- empty only when `kpis` itself is `null` (never fetched, or never successfully fetched). */
  lines: readonly string[];
  meta: string | null;
  /** Only "Savings realized" gets the accent-700 highlight, quoted from day1-demo.html's own
   * `kpis[2].fg:'var(--color-accent-700)'` -- the one KPI cell the prototype itself calls out. */
  emphasize?: boolean;
}

/**
 * AC-1's six cells, in the exact order the parent story's own AC-1 list and screens.md #9 both name
 * them: Annual spend analyzed, Savings identified, Savings realized, Savings in progress, Contracts
 * analyzed, Upcoming renewals. `kpis === null` (never fetched, or a first-load failure with nothing
 * to fall back on) renders every cell with an honestly empty `lines` array -- `KpiRow.tsx` renders
 * that as a plain "-" rather than this function fabricating a placeholder number (Appendix C rule 10).
 */
export function buildKpiCells(kpis: SavingsKpiSummaryBody | null): readonly KpiCellView[] {
  return [
    {
      key: "annual-spend-analyzed",
      label: "Annual spend analyzed",
      lines: kpis === null ? [] : kpis.annualSpendAnalyzed.map((bucket) => formatCurrencyAmount(bucket.currency, bucket.amount)),
      meta:
        kpis === null
          ? null
          : formatContractCountMeta(kpis.annualSpendAnalyzed.reduce((total, bucket) => total + bucket.contractCount, 0)),
    },
    {
      key: "savings-identified",
      label: "Savings identified",
      lines: kpis === null ? [] : kpis.savingsIdentified.map((bucket) => formatCurrencyRange(bucket.currency, bucket.low, bucket.high)),
      meta:
        kpis === null
          ? null
          : `${kpis.savingsIdentified.reduce((total, bucket) => total + bucket.count, 0)} identified`,
    },
    {
      key: "savings-realized",
      label: "Savings realized",
      lines: kpis === null ? [] : kpis.savingsRealized.map((bucket) => formatCurrencyRange(bucket.currency, bucket.low, bucket.high)),
      meta:
        kpis === null ? null : `${kpis.savingsRealized.reduce((total, bucket) => total + bucket.count, 0)} realized`,
      emphasize: true,
    },
    {
      key: "savings-in-progress",
      label: "Savings in progress",
      lines: kpis === null ? [] : kpis.savingsInProgress.map((bucket) => formatCurrencyRange(bucket.currency, bucket.low, bucket.high)),
      meta:
        kpis === null
          ? null
          : `${kpis.savingsInProgress.reduce((total, bucket) => total + bucket.count, 0)} in progress`,
    },
    {
      key: "contracts-analyzed",
      label: "Contracts analyzed",
      lines: kpis === null ? [] : [INTEGER_FORMAT.format(kpis.contractsAnalyzedCount)],
      meta: null,
    },
    {
      key: "upcoming-renewals",
      label: "Upcoming renewals",
      lines: kpis === null ? [] : [INTEGER_FORMAT.format(kpis.upcomingRenewalsCount)],
      meta: null,
    },
  ];
}

// ---------------------------------------------------------------------------------------------
// Opportunities table (AC-2) + row navigation (AC-3)
// ---------------------------------------------------------------------------------------------

/**
 * AC-3 "Rows open Contract 360 > Benchmark / Quote check" -- quoted verbatim from day1-demo.html's
 * own row handler for this exact screen: `open:()=>o.cid?this.go('c360',{cid:o.cid,tab:'Benchmark'})
 * :this.go('quote')`. A contract-linked opportunity always lands on Contract 360's Benchmark tab
 * (the same `location.state.tab` deep-link seam `../ask/index.tsx` already uses to open Clauses);
 * one with no `contractId` (e.g. a future quote-sourced opportunity -- `SavingsOpportunityResult`
 * carries no `quoteId` at all yet, only `contractId`, so there is no specific quote to deep-link to)
 * falls back to the `/quotes` landing route, the same "no id yet -> render the upload/landing state"
 * seam `../quotes/index.tsx` already establishes.
 */
export type OpportunityNavigation = { kind: "contract"; contractId: string } | { kind: "quote" };

export function getOpportunityNavigation(contractId: string | null): OpportunityNavigation {
  return contractId !== null ? { kind: "contract", contractId } : { kind: "quote" };
}

export interface OpportunityRowView {
  /** Stable id for React keys -- `SavingsOpportunityBody.id` for a real row, a synthesized
   * `renewal-action-<contractId>` for a tracked one (never overlaps a real GUID). */
  key: string;
  primaryLabel: string;
  primaryTitle: string | undefined;
  type: string;
  currentSpend: string;
  estimatedSavings: string;
  /** `null` -- rendered with no tag at all -- only for a tracked renewal action, which has no
   * confidence score of its own (see `buildTrackedOpportunityRow`'s own comment). */
  confidence: SemanticTag | null;
  owner: string;
  status: SemanticTag;
  realized: string;
  navigation: OpportunityNavigation;
}

/**
 * "Opportunity" column's primary identity (AC-2). `SavingsOpportunityResult` has no name/title field
 * at all (`Domain.SavingsOpportunity`'s own doc comment) -- the same gap `formatSupplier`/
 * `formatContractRef` already solve elsewhere in this codebase for their own entities. Prefers the
 * supplier (most opportunities are supplier-linked), falls back to the contract, and only falls back
 * to the opportunity's own id when neither is known -- never a blank cell.
 */
function primaryIdentity(item: SavingsOpportunityBody): { label: string; title: string | undefined } {
  if (item.supplierId !== null) return formatSupplier(item.supplierId);
  if (item.contractId !== null) return formatContractRef(item.contractId);
  return { label: `Opportunity ${item.id.slice(0, 8)}`, title: item.id };
}

/**
 * "Status" column tag (AC-2). `SavingsOpportunityStatus` is a real, closed three-value backend enum
 * (`Domain.SavingsOpportunityStatus`'s own doc comment) -- no ADR-019-locked mapping exists for it
 * (that table covers document/extraction confidence, not this module's own workflow status), so this
 * is this task's own extension of the same visual language, documented here rather than silently
 * invented: `Identified` (nothing actioned yet) stays the calm default; `InProgress`/`Realized` (a
 * human is actively working it, or it is done) both get the accent emphasis ADR-019 already uses
 * elsewhere for "worth noticing", distinguished from each other by their own text label, never colour
 * alone.
 */
export function getSavingsStatusTag(status: SavingsOpportunityBody["status"]): SemanticTag {
  switch (status) {
    case "Identified":
      return { variant: "neutral", label: "Identified" };
    case "InProgress":
      return { variant: "accent", label: "In progress" };
    case "Realized":
      return { variant: "accent", label: "Realized" };
  }
}

/**
 * "Confidence" column tag (AC-2, AC-3 "returns provenance + confidence, never fabricated precision").
 * `confidenceLevel` (`SavingsConfidenceLevel.Low/Medium/High`) is Contigo's own already-computed
 * qualitative tier (`SavingsProvenanceClassifier`'s own doc comment) -- shown as the label text itself
 * (never re-derived from the raw score), paired with the raw `confidence` score as a percentage so a
 * reader is never asked to interpret a bare tier unaided. Variant mapping mirrors the same
 * neutral/accent/outline severity ladder ADR-019's own confidence-tag table uses for its different
 * (extraction-confidence) vocabulary: the highest-trust tier stays calm, the lowest gets the outline
 * treatment that table reserves for "needs a closer look".
 */
export function getSavingsConfidenceTag(confidence: number, level: SavingsOpportunityBody["confidenceLevel"]): SemanticTag {
  const variantByLevel: Record<SavingsOpportunityBody["confidenceLevel"], TagVariant> = {
    High: "neutral",
    Medium: "accent",
    Low: "outline",
  };
  const percent = Math.round(confidence * 100);
  return { variant: variantByLevel[level], label: `${level} · ${percent}%` };
}

function buildRealOpportunityRow(item: SavingsOpportunityBody): OpportunityRowView {
  const identity = primaryIdentity(item);
  return {
    key: item.id,
    primaryLabel: identity.label,
    primaryTitle: identity.title,
    type: item.type,
    currentSpend: formatCurrencyAmount(item.currency, item.currentSpend),
    estimatedSavings: formatCurrencyRange(item.currency, item.estimatedSavingsLow, item.estimatedSavingsHigh),
    confidence: getSavingsConfidenceTag(item.confidence, item.confidenceLevel),
    owner: item.owner ?? UNASSIGNED_OWNER_LABEL,
    status: getSavingsStatusTag(item.status),
    realized: item.realizedAmount === null ? "—" : formatCurrencyAmount(item.currency, item.realizedAmount),
    navigation: getOpportunityNavigation(item.contractId),
  };
}

/**
 * A session-tracked renewal action, rendered as its own opportunity row -- the honest stand-in
 * `../renewals/renewalActionStore.ts`'s own header comment names this exact task
 * (E08/F02/US01/T01) as the intended consumer of `loadTrackedRenewalActions()`: "merge these into
 * its own opportunities table alongside whatever the real ... GET /api/savings returns". A tracked
 * action has no id, currency, estimated-savings range, or confidence score of its own (it only ever
 * recorded owner/status/action against a contract, never a `SavingsOpportunity` row --
 * `POST /api/savings` does not exist), so those fields render their own honest gap rather than a
 * borrowed or fabricated figure. `formatAnnualSpend` (no currency prefix) is deliberate, not an
 * oversight -- `TrackedRenewalAction.annualSpend` carries no currency code either, the same gap that
 * formatter's own doc comment already documents for the Portfolio/Renewals screens.
 */
function buildTrackedOpportunityRow(tracked: TrackedRenewalAction): OpportunityRowView {
  const supplier = formatSupplier(tracked.supplierId);
  return {
    key: `renewal-action-${tracked.contractId}`,
    primaryLabel: supplier.label,
    primaryTitle: supplier.title,
    type: "Renewal",
    currentSpend: formatAnnualSpend(tracked.annualSpend),
    estimatedSavings: NOT_YET_AVAILABLE,
    confidence: null,
    owner: tracked.owner,
    status: { variant: "accent", label: tracked.action },
    realized: "—",
    navigation: getOpportunityNavigation(tracked.contractId),
  };
}

/**
 * AC-2's whole opportunities table: this session's own tracked renewal actions first (most-recently
 * acted first, `loadTrackedRenewalActions()`'s own ordering -- immediate, visible confirmation that
 * "Action creates an opportunity visible on Home", the council decision `../renewals/InsightCard.tsx`
 * carries), then every real, persisted `SavingsOpportunity` (already newest-identified-first per
 * `GET /api/savings`'s own ordering). No de-duplication is attempted between the two: a tracked
 * action never actually creates a `SavingsOpportunity` row (see `buildTrackedOpportunityRow`'s own
 * comment), so there is no shared id -- and a real opportunity that happens to reference the same
 * `contractId` as a tracked action is not provably "the same opportunity" (nothing wires one to the
 * other), so merging them would risk silently dropping a row rather than fabricating a false match
 * (Appendix C rule 10).
 */
export function buildOpportunityRows(
  opportunities: readonly SavingsOpportunityBody[],
  trackedRenewalActions: readonly TrackedRenewalAction[],
): readonly OpportunityRowView[] {
  return [...trackedRenewalActions.map(buildTrackedOpportunityRow), ...opportunities.map(buildRealOpportunityRow)];
}
