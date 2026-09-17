import type { QuoteLineAssessmentBody } from "../../../api/client";
import QuoteLinesTable from "../QuoteLinesTable";
import { buildAssessmentBand, type QuoteAggregate, type QuoteLineRow } from "../quoteCheckViewModel";

export interface AssessmentResultProps {
  aggregate: QuoteAggregate;
  /** The same `recalculateQuoteAssessment`/history-entry lines `aggregate` was itself derived from
   * -- passed again here (never re-fetched) so this component can name the one status
   * `buildAssessmentBand`'s tally already folds into "Not yet assessed" without a reason. */
  lines: readonly QuoteLineAssessmentBody[];
  lineRows: readonly QuoteLineRow[];
}

/**
 * Task E25/F04/US02/T01 (quote-benchmark-web; parent story us-02-quote-benchmark-web AC-1; closes
 * NW-57). The market-benchmark result, benchmark-first: the three-cell band
 * (`../quoteCheckViewModel.ts#buildAssessmentBand`) plus the per-line table -- exactly what
 * `../index.tsx` rendered inline before this task, moved here rather than re-derived, so a market
 * position is still computed in exactly one place (`aggregateQuote`/`buildAssessmentBand`).
 *
 * **Cold start (AC-1).** `next-waves-todo.md` NW-57 must #3: "if there is no market row yet, keep
 * this quote as the first document of that type ... still give an honest result ('first of this
 * type; market band pending / seeded from this file')".
 * `Raffa.Quotes.Domain.MarketAssessmentStatus.InsufficientBenchmarkData` is the backend's own honest
 * name for exactly this outcome (its own doc comment: "a market position cannot be determined
 * without fabricating one") -- `isQuoteBenchmarkColdStart` below is true only when *every* line
 * reports that status, a genuine first-of-type quote, never a partially-assessed one, and never
 * `QuoteDataUnresolved` (a different, quote-side gap -- missing supplier/currency/geography/date --
 * this copy does not speak to). The message below explains *why*, without inventing a figure the
 * band's own cells do not already show: `buildAssessmentBand`'s "Not yet available" / "Not yet
 * assessed" cells are untouched by this component.
 *
 * Design oracle: `inputs/design/prototypes/raffa-v2/screens-v2.md` **#9** "Quote check" ("Supplier
 * quote lines (Quoted · Market band · Position)"). The task's own citation names screens-v2.md
 * **§10**, which is "Workspace & members" and has nothing to do with Quote check; corrected here
 * after reading the file (screens-v2.md's own numbered sections run 1-10, and Quote check is #9) --
 * the same "raw-file correction" convention this codebase's ADRs already use rather than silently
 * building against the wrong section or halting over a citation typo.
 */
export default function AssessmentResult({ aggregate, lines, lineRows }: AssessmentResultProps) {
  const band = buildAssessmentBand(aggregate, lines);
  const coldStart = isQuoteBenchmarkColdStart(lines);

  return (
    <>
      <div className="quote-band">
        {band.map((cell) => (
          <div key={cell.key} className="quote-band-cell">
            <span className="quote-band-label">{cell.label}</span>
            <span className={`quote-band-value${cell.emphasize ? " quote-emphasize" : ""}`}>{cell.value}</span>
          </div>
        ))}
      </div>

      {coldStart && (
        <p className="micro-meta quote-cold-start" role="status">
          First of its kind in this workspace — Raffa.ai has no market comparable yet for this quote, so it
          cannot show a position. This quote now seeds the shared benchmark; once similar quotes are on
          file the comparison updates automatically. Nothing above is invented in the meantime.
        </p>
      )}

      <QuoteLinesTable rows={lineRows} />
    </>
  );
}

/**
 * AC-1: true only when every line's own `status` is the backend's honest cold-start name -- never
 * inferred from a null `position` alone (a line can also be `null`-positioned while
 * `QuoteDataUnresolved`, a different, non-market gap). Exported for `../history/QuoteHistoryList.tsx`,
 * which renders the identical honest label for a cold-start entry rather than re-deriving the rule.
 */
export function isQuoteBenchmarkColdStart(lines: readonly QuoteLineAssessmentBody[]): boolean {
  return lines.length > 0 && lines.every((line) => line.status === "InsufficientBenchmarkData");
}
