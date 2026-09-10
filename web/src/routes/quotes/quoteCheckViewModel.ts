import {
  isQuoteMarketPosition,
  type NegotiationLeverTypeName,
  type QuoteLineAssessmentBody,
  type QuoteMarketPosition,
  type UnmatchedQuoteLineBody,
  type UploadedQuote,
} from "../../api/client";
import type { SemanticTag, TagVariant } from "../../styles/semantics";

/**
 * Pure view-model helpers for the V2 Quote check screen (route `/quotes[/:quoteId]`; ADR-024 V2
 * IA; screens-v2.md #9; `raffa-v2/markup.html` "QUOTE CHECK (optional)" block). No React here, so
 * every rule below is unit-testable without rendering anything (`quoteCheckViewModel.test.ts`).
 *
 * **This screen is real, not a fixture.** The prototype's Quote check is one hard-coded Databricks
 * scenario. This module derives every number from the real backend surface
 * `backend/src/Raffa.Api/QuotesEndpointExtensions.cs` / `NegotiationsEndpointExtensions.cs`
 * expose -- see `../../api/client.ts`'s own header comments on `uploadQuote` /
 * `recalculateQuoteAssessment` / `captureNegotiationOutcome` for the provenance of every field read
 * here.
 *
 * **No quote-level rollup exists server-side, on purpose.** `Raffa.Quotes.Application.Assessment
 * .QuoteMarketAssessment`'s own doc comment: "No quote-level rollup (e.g. 'overall position'): ...
 * inventing one here would be exactly the fabricated-precision Appendix C rule 10 warns against."
 * The prototype's single "Above market" verdict therefore becomes `summarizePositions`' real
 * *tally* ("2 above market · 1 in line"), never a synthesized single word.
 *
 * The Day-1 four-step stepper (Extract → Assessment → Target → Negotiation) is not part of the V2
 * design: the lines and their market position are the screen; target and levers sit one step
 * further, behind `QUOTE_LEVERS_FOOTER`.
 */

/** Header description, quoted verbatim from the prototype block. */
export const QUOTE_INTRO =
  "Drop a supplier proposal; Raffa normalises the lines and compares them with the market and with what you already pay.";

/** Footer under the lines table, quoted verbatim from the prototype block. */
export const QUOTE_LEVERS_FOOTER = "Target and negotiation levers are one step further — shown only if you want them.";

/**
 * "assessment blocked until resolved": `unmatchedLines` is `recalculateQuoteAssessment`'s own real,
 * server-computed list -- every `QuoteLine` still `SkuMatchStatus.Unmatched` after whatever
 * corrections were last applied. This function does not re-derive that judgement; it only names the
 * boolean the screen needs to swap the levers footer for the mapping block.
 */
export function isAssessmentBlocked(unmatchedLines: readonly UnmatchedQuoteLineBody[]): boolean {
  return unmatchedLines.length > 0;
}

/** `null` renders as "Not yet available" (Appendix C rule 10) -- never a fabricated placeholder. Whole-currency totals, rounded. */
export function formatMoney(amount: number | null, currency: string | null): string {
  if (amount === null) return "Not yet available";
  const rounded = Math.round(amount);
  const formatted = new Intl.NumberFormat("en-GB").format(rounded);
  return currency ? `${currency} ${formatted}` : formatted;
}

/** Per-unit prices (a DBU at 0.55) must keep their decimals -- `formatMoney`'s rounding is for totals only. */
export function formatUnitPrice(amount: number | null, currency: string | null): string {
  if (amount === null) return "Not yet available";
  const formatted = new Intl.NumberFormat("en-GB", { maximumFractionDigits: 2 }).format(amount);
  return currency ? `${currency} ${formatted}` : formatted;
}

/** A single-value estimate, used only for the two user-editable Target inputs' own computed *starting point*. */
export function formatMoneyInputValue(amount: number | null): string {
  return amount === null ? "" : String(Math.round(amount));
}

export function formatRange(low: number | null, high: number | null, currency: string | null): string {
  if (low === null && high === null) return "Not yet available";
  if (low === null) return formatMoney(high, currency);
  if (high === null) return formatMoney(low, currency);
  if (Math.round(low) === Math.round(high)) return formatMoney(low, currency);
  const formatted = new Intl.NumberFormat("en-GB");
  const low0 = formatted.format(Math.round(low));
  const high0 = formatted.format(Math.round(high));
  return currency ? `${currency} ${low0}–${high0}` : `${low0}–${high0}`;
}

/** Header meta line once a quote is loaded: file · supplier · currency · geography from the upload response this session, else the quote id. */
export function formatQuoteMeta(quote: UploadedQuote | null, quoteId: string): string {
  if (quote === null) return `Quote ${quoteId.slice(0, 8)}`;
  return [quote.fileName, quote.supplier, quote.currency, quote.geography].filter((part) => part !== null && part !== "").join(" · ");
}

export interface LineDetail {
  sku: string;
  edition: string | null;
  description: string;
}

/** Merges a fresh `unmatchedLines` read into this screen's own running memory of every line's
 * sku/edition/description it has ever actually seen (the assessment projection never carries these
 * fields, matched or not, so once a line resolves it would otherwise lose the very name a person just
 * read). Never removes an entry. */
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
  /** The line's real description when known (this round's `unmatchedLines`, or a cached earlier one) -- `Line {n}` otherwise. */
  label: string;
  sku: string | null;
  edition: string | null;
  quantity: number | null;
  unitPrice: number | null;
  /** `unitPrice * quantity` when both are known -- deterministic, never a value either endpoint returns directly. */
  annual: number | null;
  matchTag: SemanticTag;
  isUnmatched: boolean;
}

/** One row per assessed line, with the real sku/edition/description layered on for whichever lines are currently unresolved. */
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
  /** `sum(unitPrice * quantity)` over every line where both are known -- the only real source for "the quote total" anywhere in this screen. `null` only when no line has both fields. */
  originalTotal: number | null;
  /** `sum(P25 * quantity)` .. `sum(P75 * quantity)` over lines with both a distribution and a quantity. `null` when no line contributed. */
  expectedMarketLow: number | null;
  expectedMarketHigh: number | null;
  /** `sum(recommendedTargetLow/High * quantity)` -- same summed-per-line shape as `expectedMarketLow/High`. */
  recommendedTargetLow: number | null;
  recommendedTargetHigh: number | null;
  /** Direct sums of `targetSaving.totalSavingsRangeLow/High` -- already totals on the wire, per line. */
  totalSavingsLow: number | null;
  totalSavingsHigh: number | null;
  /** First non-null `benchmark.currency` found across the lines -- display-only. */
  currency: string | null;
  assessedLineCount: number;
  totalLineCount: number;
}

/** Deterministic aggregation over every line's own assessment (Appendix C rule 6) -- the single place every other view reads a quote-level number from. */
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

/** A real *tally*, never a synthesized single verdict -- see this module's own header comment. */
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

export interface AssessmentBandCell {
  key: "quote" | "market" | "assessment";
  label: string;
  value: string;
  /** The prototype renders the Assessment cell in accent-700; text carries the meaning, the colour only adds emphasis. */
  emphasize: boolean;
}

/**
 * The V2 three-cell band above the lines (`markup.html`: "Supplier quote CHF 520k · Market range
 * CHF 390–470k · Assessment Above market"), derived from `aggregateQuote`'s own output -- never a
 * second, independent calculation. Potential saving is not a band cell in V2; it lives one step
 * further, in the Target step.
 */
export function buildAssessmentBand(aggregate: QuoteAggregate, lines: readonly QuoteLineAssessmentBody[]): AssessmentBandCell[] {
  return [
    { key: "quote", label: "Supplier quote", value: formatMoney(aggregate.originalTotal, aggregate.currency), emphasize: false },
    {
      key: "market",
      label: "Market range",
      value: formatRange(aggregate.expectedMarketLow, aggregate.expectedMarketHigh, aggregate.currency),
      emphasize: false,
    },
    { key: "assessment", label: "Assessment", value: summarizePositions(lines), emphasize: true },
  ];
}

export interface QuoteLineRow {
  quoteLineId: string;
  label: string;
  quoted: string;
  p50: string;
  position: SemanticTag;
  /** `"+20% vs P50"` -- deterministic from the line's own unit price and P50; `null` when either is unknown. */
  vsP50: string | null;
  /** Benchmark confidence "High · n=96" as a tag (High neutral, Medium accent, Low outline); `null` when no benchmark matched. */
  benchmark: SemanticTag | null;
  needsMapping: boolean;
}

const BENCHMARK_VARIANT_BY_LEVEL: Readonly<Record<string, TagVariant>> = { High: "neutral", Medium: "accent", Low: "outline" };

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

/** `+20% vs P50` / `-5% vs P50` / `At P50`, from the real unit price and the real median only. */
export function formatVersusP50(unitPrice: number | null, p50: number | null): string | null {
  if (unitPrice === null || p50 === null || p50 <= 0) return null;
  const percent = Math.round((unitPrice / p50 - 1) * 100);
  if (percent === 0) return "At P50";
  return `${percent > 0 ? "+" : ""}${percent}% vs P50`;
}

/** The V2 lines table (Line · Quoted · P50 · Position · Benchmark), one row per assessed line. */
export function buildQuoteLineRows(rows: readonly ExtractLineRow[], lines: readonly QuoteLineAssessmentBody[]): QuoteLineRow[] {
  const linesById = new Map(lines.map((line) => [line.quoteLineId, line]));

  return rows.map((row) => {
    const line = linesById.get(row.quoteLineId);
    const distribution = line?.benchmark?.distribution ?? null;
    const currency = line?.benchmark?.currency ?? null;
    // `line.position` is generated as a bare `string | null`; narrow it through the runtime guard --
    // an unrecognised value renders as "Not yet assessed" rather than mis-tagging it.
    const position = line?.position !== null && line?.position !== undefined && isQuoteMarketPosition(line.position) ? line.position : null;
    const confidence = line?.confidence ?? null;

    return {
      quoteLineId: row.quoteLineId,
      label: row.label,
      quoted: formatUnitPrice(line?.unitPrice ?? null, currency),
      p50: distribution ? formatUnitPrice(distribution.p50, currency) : "Not yet available",
      position: row.isUnmatched ? { variant: "outline", label: "Needs mapping" } : positionTag(position),
      vsP50: formatVersusP50(line?.unitPrice ?? null, distribution?.p50 ?? null),
      benchmark:
        confidence !== null
          ? { variant: BENCHMARK_VARIANT_BY_LEVEL[confidence.level] ?? "neutral", label: `${confidence.level} · n=${confidence.sampleSize ?? "—"}` }
          : null,
      needsMapping: row.isUnmatched,
    };
  });
}

/** The 7-member closed vocabulary `POST /api/negotiations/outcomes` validates `leversUsed` against -- labels only; the wire value is the bare enum name. */
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
 * (`NegotiationOutcomeCalculator.Compute`, mirrored verbatim: `realizedSaving = originalTotal -
 * finalPrice`, `discountPercent = realizedSaving / originalTotal * 100`). The *recorded* outcome
 * always renders the server's own response instead (`NegotiationStep.tsx`).
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
