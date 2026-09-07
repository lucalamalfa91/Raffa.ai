import {
  isQuoteMarketPosition,
  type NegotiationLeverTypeName,
  type QuoteLineAssessmentBody,
  type QuoteMarketPosition,
  type UnmatchedQuoteLineBody,
} from "../../api/client";
import type { SemanticTag } from "../../styles/semantics";

/**
 * Pure view-model helpers for the Quote check stepper (route `/quotes/:quoteId`, ADR-018; ADR-020
 * screen 10; task E08/F03/US01/T01, us-01-quote-check AC-1/AC-2/AC-3/AC-4). Same one-concern-per-file
 * split `../contracts/contract360/contract360ViewModel.ts`/`../contracts/review/reviewViewModel.ts`
 * already established for this codebase: no React here, so every rule below is unit-testable
 * without rendering anything.
 *
 * **This screen is real, not a fixture.** `inputs/design/prototypes/day1-demo.html`'s own Quote
 * check screen is a single hard-coded demo scenario (Databricks Proposal Q-88213 -- `qlines`/
 * `assess`/`qbench`/`levers` are all fixed JS array literals, never derived from real state except
 * `qstep`/`mapChoice`/`mapped`/`outcome`). This module instead derives every number from the real
 * backend surface `backend/src/Contigo.Api/QuotesEndpointExtensions.cs` /
 * `NegotiationsEndpointExtensions.cs` already expose (epic E05, wired into `Program.cs` before this
 * task started) -- see `../../api/client.ts`'s own header comments on `uploadQuote`/
 * `getQuoteAssessment`/`recalculateQuoteAssessment`/`captureNegotiationOutcome` for the exact
 * provenance of every field this module reads.
 *
 * **No quote-level rollup exists server-side, on purpose.** `Contigo.Quotes.Application.Assessment
 * .QuoteMarketAssessment`'s own doc comment: "No quote-level rollup (e.g. 'overall position'): ...
 * no ADR/spec names a deterministic way to collapse several lines' positions into one -- inventing
 * one here would be exactly the fabricated-precision Appendix C rule 10 warns against." This module
 * honours that restraint instead of re-introducing the rollup on the client: `summarizePositions`
 * below returns a real *tally* (how many lines are in each bucket), never a single synthesized
 * verdict.
 */

export const QUOTE_STEP_LABELS = ["Extract", "Assessment", "Target", "Negotiation"] as const;
export type QuoteStepIndex = 0 | 1 | 2 | 3;

export interface QuoteStepView {
  index: QuoteStepIndex;
  /** "01".."04" -- day1-demo.html's own `qsteps` numbering (`String(i+1).padStart(2,'0')`). */
  number: string;
  label: string;
}

/** The 4 steps, in order -- `QuoteStepper.tsx` renders exactly this list (AC-1). Every step is
 * directly reachable by clicking its own header (day1-demo.html's own `qsteps[i].go`); only the
 * *content* of the Assessment step gates on `isAssessmentBlocked` below (AC-2), never the stepper
 * navigation itself. */
export const QUOTE_STEPS: readonly QuoteStepView[] = QUOTE_STEP_LABELS.map((label, index) => ({
  index: index as QuoteStepIndex,
  number: String(index + 1).padStart(2, "0"),
  label,
}));

/**
 * AC-2's own gate, verbatim: "assessment blocked until resolved". `unmatchedLines` is
 * `recalculateQuoteAssessment`'s own real, server-computed list -- every `QuoteLine` still
 * `SkuMatchStatus.Unmatched` after whatever corrections were last applied (`SkuMappingService
 * .GetUnmatchedLinesAsync`, tenant/quote-scoped). This function does not re-derive that judgement;
 * it only names the boolean AC-2 needs (day1-demo.html's own `unmapped: !s.mapped` plays the
 * identical role against its own fixed fixture).
 */
export function isAssessmentBlocked(unmatchedLines: readonly UnmatchedQuoteLineBody[]): boolean {
  return unmatchedLines.length > 0;
}

/** `null` renders as "Not yet available" (Appendix C rule 10) -- never a fabricated placeholder. */
export function formatMoney(amount: number | null, currency: string | null): string {
  if (amount === null) return "Not yet available";
  const rounded = Math.round(amount);
  const formatted = new Intl.NumberFormat("en-GB").format(rounded);
  return currency ? `${currency} ${formatted}` : formatted;
}

/** A single-value estimate, used only for the two user-editable Target step inputs' own computed
 * *starting point* -- never for a line/table fact, which always uses `formatMoney`'s honest
 * "Not yet available" instead. */
export function formatMoneyInputValue(amount: number | null): string {
  return amount === null ? "" : String(Math.round(amount));
}

function formatRange(low: number | null, high: number | null, currency: string | null): string {
  if (low === null && high === null) return "Not yet available";
  if (low === null) return formatMoney(high, currency);
  if (high === null) return formatMoney(low, currency);
  if (Math.round(low) === Math.round(high)) return formatMoney(low, currency);
  const formatted = new Intl.NumberFormat("en-GB");
  const low0 = formatted.format(Math.round(low));
  const high0 = formatted.format(Math.round(high));
  return currency ? `${currency} ${low0}–${high0}` : `${low0}–${high0}`;
}

export interface LineDetail {
  sku: string;
  edition: string | null;
  description: string;
}

/** Merges a fresh `unmatchedLines` read into this screen's own running memory of every line's
 * sku/edition/description it has ever actually seen (see `buildExtractRows`'s own doc comment for
 * why this memory exists at all: the assessment projection never carries these fields, matched or
 * not, so once a line resolves it would otherwise lose the very name a person just read). Never
 * removes an entry -- a line that is resolved this round keeps the description this round's own
 * response, or an earlier round's, already gave it. */
export function mergeKnownLineDetails(
  existing: ReadonlyMap<string, LineDetail>,
  unmatchedLines: readonly UnmatchedQuoteLineBody[],
): Map<string, LineDetail> {
  const next = new Map(existing);
  for (const line of unmatchedLines) {
    next.set(line.quoteLineId, { sku: line.sku, edition: line.edition, description: line.description });
  }
  return next;
}

export interface ExtractLineRow {
  quoteLineId: string;
  /** The line's real description when known (this round's `unmatchedLines`, or a cached earlier
   * one) -- `Line {n}` otherwise (AC-2's own table still needs one row per line even though the
   * assessment projection carries no name for an already-matched line; see this module's own header
   * comment). */
  label: string;
  sku: string | null;
  edition: string | null;
  quantity: number | null;
  unitPrice: number | null;
  /** `unitPrice * quantity` when both are known -- deterministic, never a value either endpoint
   * returns directly (Appendix C rule 6). */
  annual: number | null;
  matchTag: SemanticTag;
  isUnmatched: boolean;
}

/**
 * Extract step's own line table (screens.md #10 AC-2: "line table with benchmark match"). Every row
 * comes from `assessment.lines` (quantity/unitPrice, present for every line); `unmatchedLines`
 * layers the real sku/edition/description on top for whichever lines are currently unresolved.
 * `.tag-outline`/`.tag-neutral` reuse ADR-019's own locked variants (outline = needs attention,
 * neutral = resolved) rather than inventing a third "matched" visual language for this screen alone.
 */
export function buildExtractRows(
  lines: readonly QuoteLineAssessmentBody[],
  unmatchedLines: readonly UnmatchedQuoteLineBody[],
  knownLineDetails: ReadonlyMap<string, LineDetail>,
): ExtractLineRow[] {
  const unmatchedById = new Map(unmatchedLines.map((line) => [line.quoteLineId, line]));

  return lines.map((line, index) => {
    const unmatched = unmatchedById.get(line.quoteLineId);
    const known = unmatched ?? knownLineDetails.get(line.quoteLineId) ?? null;
    const annual = line.unitPrice !== null && line.quantity !== null ? line.unitPrice * line.quantity : null;

    return {
      quoteLineId: line.quoteLineId,
      label: known?.description ?? `Line ${index + 1}`,
      sku: known?.sku ?? null,
      edition: known?.edition ?? null,
      quantity: line.quantity,
      unitPrice: line.unitPrice,
      annual,
      matchTag: unmatched
        ? { variant: "outline", label: "Needs mapping" }
        : { variant: "neutral", label: "Resolved" },
      isUnmatched: unmatched !== undefined,
    };
  });
}

export interface QuoteAggregate {
  /** `sum(unitPrice * quantity)` over every line where both are known -- the only real source for
   * "the quote total" anywhere in this screen (there is no `GET /api/quotes/{id}` that echoes the
   * upload-time total back; see `../../api/client.ts`'s own header comment on why `uploadQuote`'s
   * response is the only place a quote-level total is ever directly returned, and it is not
   * re-fetchable after this session ends). `null` only when no line has both fields. */
  originalTotal: number | null;
  /** `sum(P25 * quantity)` .. `sum(P75 * quantity)` over lines with both a distribution and a
   * quantity -- a deterministic "expected market spend" range, the same per-line-then-summed shape
   * `totalSavingsLow/High` below already uses (never a value either endpoint returns as a total
   * directly). `null` when no line contributed. */
  expectedMarketLow: number | null;
  expectedMarketHigh: number | null;
  /** `sum(recommendedTargetLow/High * quantity)` -- same summed-per-line shape as
   * `expectedMarketLow/High`. */
  recommendedTargetLow: number | null;
  recommendedTargetHigh: number | null;
  /** Direct sums of `targetSaving.totalSavingsRangeLow/High` -- already totals on the wire, per
   * line; this is the only field here that is a plain sum, not a per-unit-times-quantity one. */
  totalSavingsLow: number | null;
  totalSavingsHigh: number | null;
  /** First non-null `benchmark.currency` found across the lines -- display-only; the capture
   * request itself carries no currency field (see `../../api/client.ts`'s own
   * `CaptureNegotiationOutcomeRequest` doc comment). */
  currency: string | null;
  assessedLineCount: number;
  totalLineCount: number;
}

/** Deterministic aggregation over every line's own assessment (Appendix C rule 6) -- the single
 * place every other view in this module reads a quote-level number from, so the 4-number grid, the
 * Target ladder, and the outcome form's "Original quote total" default can never drift apart from
 * one another. */
export function aggregateQuote(lines: readonly QuoteLineAssessmentBody[]): QuoteAggregate {
  let originalTotal: number | null = null;
  let expectedMarketLow: number | null = null;
  let expectedMarketHigh: number | null = null;
  let recommendedTargetLow: number | null = null;
  let recommendedTargetHigh: number | null = null;
  let totalSavingsLow: number | null = null;
  let totalSavingsHigh: number | null = null;
  let currency: string | null = null;
  let assessedLineCount = 0;

  for (const line of lines) {
    if (line.status === "Assessed") assessedLineCount++;

    if (line.unitPrice !== null && line.quantity !== null) {
      originalTotal = (originalTotal ?? 0) + line.unitPrice * line.quantity;
    }

    if (currency === null && line.benchmark?.currency) currency = line.benchmark.currency;

    const distribution = line.benchmark?.distribution ?? null;
    if (distribution !== null && line.quantity !== null) {
      expectedMarketLow = (expectedMarketLow ?? 0) + distribution.p25 * line.quantity;
      expectedMarketHigh = (expectedMarketHigh ?? 0) + distribution.p75 * line.quantity;
    }

    const targetSaving = line.targetSaving;
    if (targetSaving?.recommendedTargetLow !== null && targetSaving?.recommendedTargetLow !== undefined && line.quantity !== null) {
      recommendedTargetLow = (recommendedTargetLow ?? 0) + targetSaving.recommendedTargetLow * line.quantity;
    }
    if (targetSaving?.recommendedTargetHigh !== null && targetSaving?.recommendedTargetHigh !== undefined && line.quantity !== null) {
      recommendedTargetHigh = (recommendedTargetHigh ?? 0) + targetSaving.recommendedTargetHigh * line.quantity;
    }
    if (targetSaving?.totalSavingsRangeLow !== null && targetSaving?.totalSavingsRangeLow !== undefined) {
      totalSavingsLow = (totalSavingsLow ?? 0) + targetSaving.totalSavingsRangeLow;
    }
    if (targetSaving?.totalSavingsRangeHigh !== null && targetSaving?.totalSavingsRangeHigh !== undefined) {
      totalSavingsHigh = (totalSavingsHigh ?? 0) + targetSaving.totalSavingsRangeHigh;
    }
  }

  return {
    originalTotal,
    expectedMarketLow,
    expectedMarketHigh,
    recommendedTargetLow,
    recommendedTargetHigh,
    totalSavingsLow,
    totalSavingsHigh,
    currency,
    assessedLineCount,
    totalLineCount: lines.length,
  };
}

/** A real *tally*, never a synthesized single verdict -- see this module's own header comment for
 * why (`QuoteMarketAssessment`'s own doc comment explicitly declines to collapse several lines'
 * positions into one). */
export function summarizePositions(lines: readonly QuoteLineAssessmentBody[]): string {
  let above = 0;
  let inLine = 0;
  let below = 0;

  for (const line of lines) {
    if (line.position === "AboveMarket") above++;
    else if (line.position === "InLine") inLine++;
    else if (line.position === "BelowMarket") below++;
  }

  if (above === 0 && inLine === 0 && below === 0) return "Not yet assessed";
  const parts: string[] = [];
  if (above > 0) parts.push(`${above} above market`);
  if (inLine > 0) parts.push(`${inLine} in line`);
  if (below > 0) parts.push(`${below} below market`);
  return parts.join(" · ");
}

export interface AssessmentNumber {
  key: string;
  label: string;
  value: string;
  /** `true` renders in `--color-accent-700` (ADR-019 "emphasise a noteworthy figure"), mirroring
   * day1-demo.html's own `assess` array (`Assessment`/`Potential saving` are the two emphasised
   * entries there). */
  emphasize: boolean;
}

/** The "4 numbers" (screens.md #10 AC-3: "quote, market range, assessment, potential saving"),
 * derived from `aggregateQuote`'s own output -- never a second, independent calculation. */
export function buildAssessmentNumbers(aggregate: QuoteAggregate, lines: readonly QuoteLineAssessmentBody[]): AssessmentNumber[] {
  return [
    { key: "quote", label: "Supplier quote", value: formatMoney(aggregate.originalTotal, aggregate.currency), emphasize: false },
    {
      key: "market",
      label: "Expected market range",
      value: formatRange(aggregate.expectedMarketLow, aggregate.expectedMarketHigh, aggregate.currency),
      emphasize: false,
    },
    { key: "assessment", label: "Assessment", value: summarizePositions(lines), emphasize: true },
    {
      key: "saving",
      label: "Potential saving",
      value: formatRange(aggregate.totalSavingsLow, aggregate.totalSavingsHigh, aggregate.currency),
      emphasize: true,
    },
  ];
}

export interface LineMarketPositionRow {
  quoteLineId: string;
  label: string;
  unitPrice: string;
  p25: string;
  p50: string;
  p75: string;
  positionTag: SemanticTag;
  confidence: string;
}

/** Assessment step's own "Line-level market position" table (screens.md #10 AC-3). */
export function buildLineMarketPositionRows(rows: readonly ExtractLineRow[], lines: readonly QuoteLineAssessmentBody[]): LineMarketPositionRow[] {
  const linesById = new Map(lines.map((line) => [line.quoteLineId, line]));

  return rows.map((row) => {
    const line = linesById.get(row.quoteLineId);
    const distribution = line?.benchmark?.distribution ?? null;
    const currency = line?.benchmark?.currency ?? null;
    // `line.position` is generated as a bare `string | null` (see ../../api/client.ts's own
    // QuoteMarketPosition doc comment for why); narrow it through the same runtime guard
    // isPortfolioRiskSeverity's own callers use for the identical generator limitation -- an
    // unrecognized value renders as "Not yet assessed" (positionTag's own `default` branch) rather
    // than mis-tagging it.
    const position = line?.position !== null && line?.position !== undefined && isQuoteMarketPosition(line.position) ? line.position : null;

    return {
      quoteLineId: row.quoteLineId,
      label: row.label,
      unitPrice: line?.unitPrice !== null && line?.unitPrice !== undefined ? formatMoney(line.unitPrice, currency) : "Not yet available",
      p25: distribution ? formatMoney(distribution.p25, currency) : "Not yet available",
      p50: distribution ? formatMoney(distribution.p50, currency) : "Not yet available",
      p75: distribution ? formatMoney(distribution.p75, currency) : "Not yet available",
      positionTag: positionTag(position),
      confidence: line?.confidence ? `${line.confidence.level} · n=${line.confidence.sampleSize ?? "—"}` : "Not yet available",
    };
  });
}

function positionTag(position: QuoteMarketPosition | null): SemanticTag {
  switch (position) {
    case "AboveMarket":
      return { variant: "accent", label: "Above market" };
    case "InLine":
      return { variant: "neutral", label: "In line" };
    case "BelowMarket":
      return { variant: "neutral", label: "Below market" };
    default:
      return { variant: "outline", label: "Not yet assessed" };
  }
}

/** The 7-member closed vocabulary `POST /api/negotiations/outcomes` validates `leversUsed` against
 * (`NegotiationOutcomeService.LeversUsedInvalidError`) -- labels only; the wire value is the bare
 * enum name (`NEGOTIATION_LEVER_TYPES` in `../../api/client.ts`). */
export const NEGOTIATION_LEVER_LABELS: ReadonlyArray<{ value: NegotiationLeverTypeName; label: string }> = [
  { value: "Volume", label: "Volume" },
  { value: "Term", label: "Term" },
  { value: "Utilization", label: "Utilization" },
  { value: "Alternatives", label: "Alternatives" },
  { value: "QuarterEnd", label: "Quarter-end" },
  { value: "Bundle", label: "Bundle" },
  { value: "PaymentTerms", label: "Payment terms" },
];

export interface OutcomePreview {
  realizedSaving: number | null;
  discountPercent: number | null;
}

/**
 * Client-side preview of what `POST /api/negotiations/outcomes` will compute
 * (`NegotiationOutcomeCalculator.Compute`, mirrored here verbatim: `realizedSaving = originalTotal -
 * finalPrice`, `discountPercent = realizedSaving / originalTotal * 100`) -- shown before submit so a
 * person sees the consequence of the final price they are about to record. The *recorded* outcome
 * table never uses this function's output; it always renders the server's own response (see
 * `NegotiationStep.tsx`'s own header comment).
 */
export function previewOutcome(originalTotal: number | null, finalPrice: number | null): OutcomePreview {
  if (originalTotal === null || finalPrice === null || originalTotal <= 0) {
    return { realizedSaving: null, discountPercent: null };
  }
  const realizedSaving = originalTotal - finalPrice;
  return { realizedSaving, discountPercent: (realizedSaving / originalTotal) * 100 };
}

export function formatPercent(value: number | null): string {
  if (value === null) return "Not yet available";
  return `${value.toFixed(1)}%`;
}
