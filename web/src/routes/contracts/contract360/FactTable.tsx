import { getConfidenceTag } from "../../../styles/semantics";
import type { FactRow } from "./contract360ViewModel";

export interface FactTableProps {
  /** h6 title, left of the table (screens.md #5: "h6 title + summary line right + .table"). */
  title: string;
  /** Summary line, right-aligned next to the title. */
  summary?: string;
  rows: readonly FactRow[];
  /** Shown instead of the table when `rows` is empty -- never a blank box (ADR-018 empty-state contract). */
  emptyMessage: string;
}

/**
 * The shared Term / Value / Source / Confidence table template (ADR-020 screen 5: "one template —
 * h6 title + summary line right + .table (Term · Value · Source · Confidence pattern)"), reused by
 * Commercials/Products/Clauses/Obligations/Risks/Documents/Overview's own "Contract details" block
 * and the generic half of Renewal. Every row here is a deterministic, extracted, or contract-level
 * fact -- never the AI recommendation (`OverviewTab.tsx`'s `.ai-recommendation` card is the only
 * place that renders, ADR-019 facts/AI separation; see `contract360ViewModel.ts`'s own header
 * comment for why the two can never be accidentally merged).
 */
export default function FactTable({ title, summary, rows, emptyMessage }: FactTableProps) {
  return (
    <section className="contract360-tab-panel">
      <div className="contract360-tab-panel-header">
        <h6>{title}</h6>
        {summary !== undefined && <span className="micro-meta">{summary}</span>}
      </div>

      {rows.length === 0 ? (
        <p className="micro-meta">{emptyMessage}</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>Term</th>
              <th>Value</th>
              <th>Source</th>
              <th>Confidence</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const tag = row.confidencePct === null ? null : getConfidenceTag(row.confidencePct);
              return (
                <tr key={row.key}>
                  <td>{row.term}</td>
                  <td>{row.value}</td>
                  <td>{row.source ?? "—"}</td>
                  <td>{tag === null ? "—" : <span className={`tag tag-${tag.variant}`}>{tag.label}</span>}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}
