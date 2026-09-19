import { Link, useNavigate } from "react-router-dom";
import type { QuoteBenchmarkHistoryEntryBody } from "../../../api/client";
import type { SemanticTag } from "../../../styles/semantics";
import { summarizePositions } from "../quoteCheckViewModel";
import { isQuoteBenchmarkColdStart } from "../assessment/AssessmentResult";

export interface QuoteHistoryListProps {
  /** Newest first -- the server's own order (`QuoteBenchmarkHistoryService.GetHistoryAsync`,
   * `ORDER BY CreatedAt DESC`). Never re-sorted here. */
  entries: readonly QuoteBenchmarkHistoryEntryBody[];
}

const CREATED_AT_FORMATTER = new Intl.DateTimeFormat("en-GB", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  timeZone: "UTC",
});

/**
 * Task E25/F04/US02/T01 (quote-benchmark-web; parent story us-02-quote-benchmark-web AC-2; closes
 * NW-57). "Quote check history" (`next-waves-todo.md` NW-57 must #5): "a section listing every
 * request this workspace has made (re-open `/quotes/:id`, see the benchmark again)". Reads
 * `GET /api/quotes/benchmark-history` (`../../../api/client.ts#getQuoteBenchmarkHistory`) -- durable
 * server state per ADR-028 ("a client store never stands in for a missing GET"), never a
 * `sessionStorage` list, so a reload or a second browser sees the identical history. Each row
 * re-opens `/quotes/:id`; the server recomputes that quote's benchmark fresh on that read
 * (`QuoteBenchmarkHistoryService`'s own doc comment: a prior quote's benchmark "reads back because
 * the quote that produced it is still in the database, not because a snapshot ... was frozen at
 * upload time"), so this list never itself caches a position.
 *
 * **Rendered only on the Quote check landing** (`../index.tsx`, no `routeQuoteId`) -- never beside an
 * already-open quote's own live result. The currently-open quote's own row here would otherwise show
 * whatever the *last full history fetch* returned, which goes stale the instant a mapping correction
 * changes that same quote's position in `AssessmentResult` above it -- two answers for one quote on
 * one screen, the same class of defect ADR-024's w17 footer clause 7 names for the analogous
 * duplicate-market-claim case ("one resolution per screen, not two"). Landing-only sidesteps it
 * rather than re-fetching history on every mapping apply.
 *
 * Never fabricates a position: an entry's own tag is either a real tally
 * (`quoteCheckViewModel.ts#summarizePositions`) or, for a genuine first-of-type quote, the same
 * honest "First of its kind" label `AssessmentResult`'s cold-start copy uses -- `isQuoteBenchmarkColdStart`
 * is imported, not re-derived, so the two surfaces can never disagree about what counts as a cold start.
 *
 * **Same table system as Documents / Portfolio / Renewals / Savings**, not a one-off history list.
 * Locked `.table` catalogue (ADR-019): 11px uppercase thead, 2px header rule, 1px row rules, hover
 * tint. Filename is the keyboard-operable `<Link>` (Documents' own `document-status-table-link`
 * pattern); the meta line is a sibling `.micro-meta`, never inside the link. Assessment is a `.tag`
 * (`tag-outline` for "Not yet assessed" / cold start, `tag-neutral` for a real tally -- the same
 * pairing `QuoteLinesTable` already uses). The row's own click is the prototype `cg-row` mouse
 * convenience Portfolio / Renewals / Savings layer on top of the cell link, never the only way in.
 * No column filters: Documents and Renewals do not have them, and a two-column compact list does
 * not earn Portfolio's typed header inputs.
 */
export default function QuoteHistoryList({ entries }: QuoteHistoryListProps) {
  const navigate = useNavigate();

  if (entries.length === 0) {
    return (
      <p className="quote-history-empty micro-meta" role="status">
        No quote checks yet — upload a supplier proposal above to start this workspace's benchmark history.
      </p>
    );
  }

  return (
    <div className="quote-history-table-wrapper">
      <table className="table quote-history-table">
        <thead>
          <tr>
            <th scope="col">Quote</th>
            <th scope="col" className="quote-history-col-assessment">
              Assessment
            </th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => {
            const tag = getQuoteHistoryPositionTag(entry);
            const href = `/quotes/${entry.id}`;
            return (
              <tr
                key={entry.id}
                className="quote-history-row"
                onClick={(event) => {
                  if ((event.target as HTMLElement).closest("a") !== null) return;
                  navigate(href);
                }}
              >
                <td>
                  <Link to={href} className="quote-history-link">
                    {entry.fileName}
                  </Link>
                  <div className="micro-meta">{formatHistoryMeta(entry)}</div>
                </td>
                <td>
                  <span className={`tag tag-${tag.variant}`}>{tag.label}</span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

/** Supplier · currency · geography · the checked-in date, dropping any null segment without leaving
 * a gap, a dash, or a placeholder -- the same convention `../index.tsx#formatQuoteMeta` and
 * `WorkspacePickerScreen.tsx#buildWorkspaceRowMeta` already establish. Not `formatQuoteMeta` itself:
 * that helper's parameter type (`UploadedQuote`) pins `processingStatus` to a closed literal union
 * this wire shape does not carry (`QuoteBenchmarkHistoryEntryBody`'s own `processingStatus` is a bare
 * `string`, the same `GET /api/quotes` list projection uses) -- reusing it would need a cast, not a
 * shared rule. */
export function formatHistoryMeta(entry: QuoteBenchmarkHistoryEntryBody): string {
  const parts = [entry.supplier, entry.currency, entry.geography].filter(
    (part): part is string => part !== null && part !== "",
  );
  parts.push(CREATED_AT_FORMATTER.format(new Date(entry.createdAt)));
  return parts.join(" · ");
}

/** A real tally (`summarizePositions`) for an assessed entry, or the same honest cold-start /
 * not-yet-assessed labels the lines table already uses -- never a fabricated position, and never a
 * second definition of "cold start" from this one. */
export function getQuoteHistoryPositionTag(entry: QuoteBenchmarkHistoryEntryBody): SemanticTag {
  if (isQuoteBenchmarkColdStart(entry.lines)) {
    return { variant: "outline", label: "First of its kind" };
  }
  const label = summarizePositions(entry.lines);
  if (label === "Not yet assessed") {
    return { variant: "outline", label };
  }
  return { variant: "neutral", label };
}
