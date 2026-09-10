import type { QuoteLineRow } from "./quoteCheckViewModel";

export interface QuoteLinesTableProps {
  rows: readonly QuoteLineRow[];
}

/**
 * The V2 lines table (screens-v2.md #9 "Supplier quote lines (Quoted · Market band · Position)";
 * `contigo-v2/markup.html` "QUOTE CHECK" block): Line (28%, bold) · Quoted (right) · P50 (right) ·
 * Position · Benchmark (18%, small tag), `font-size:13px`. Every cell is a real per-line figure
 * from `recalculateQuoteAssessment`; a line still waiting for a SKU mapping says so in its Position
 * cell instead of showing a position it does not have.
 */
export default function QuoteLinesTable({ rows }: QuoteLinesTableProps) {
  if (rows.length === 0) {
    return (
      <p className="quote-empty-lines micro-meta" role="status">
        No line items were extracted from this quote yet. Contigo only assesses lines it could read — nothing here is estimated.
      </p>
    );
  }

  return (
    <table className="table quote-lines-table">
      <thead>
        <tr>
          <th scope="col" className="quote-col-line">
            Line
          </th>
          <th scope="col" className="quote-table-numeric">
            Quoted
          </th>
          <th scope="col" className="quote-table-numeric">
            P50
          </th>
          <th scope="col">Position</th>
          <th scope="col" className="quote-col-benchmark">
            Benchmark
          </th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.quoteLineId} className={row.needsMapping ? "row-critical" : undefined}>
            <td className="quote-cell-line">{row.label}</td>
            <td className="quote-table-numeric">{row.quoted}</td>
            <td className="quote-table-numeric">{row.p50}</td>
            <td>
              <span className={`tag tag-${row.position.variant}`}>{row.position.label}</span>
              {row.vsP50 !== null && <span className="quote-vs-p50">{row.vsP50}</span>}
            </td>
            <td>
              {row.benchmark !== null ? (
                <span className={`tag tag-${row.benchmark.variant} quote-benchmark-tag`}>{row.benchmark.label}</span>
              ) : (
                <span className="micro-meta">—</span>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
