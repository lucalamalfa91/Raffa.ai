import type { FactRow } from "./contract360ViewModel";

export interface FactTableProps {
  title: string;
  rows: readonly FactRow[];
  /** Shown instead of the table when `rows` is empty -- never a blank box. */
  emptyMessage: string;
}

/**
 * Term / Value / Source list used inside the "Details ▾" drawer for the extracted Products /
 * Obligations / Risks. Every row is a deterministic extracted fact -- never the recommendation
 * (ADR-019 facts vs AI) and never a confidence tag (ADR-019 w17 clause 11). Unofficialized values
 * arrive already as the em-dash; rows are never dropped.
 */
export default function FactTable({ title, rows, emptyMessage }: FactTableProps) {
  return (
    <section className="contract360-facts">
      <h6>{title}</h6>

      {rows.length === 0 ? (
        <p className="contract360-detail-empty">{emptyMessage}</p>
      ) : (
        <table className="table contract360-facts-table">
          <thead>
            <tr>
              <th scope="col">Term</th>
              <th scope="col">Value</th>
              <th scope="col">Source</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.key}>
                <td>{row.term}</td>
                <td>{row.value}</td>
                <td>{row.source ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
