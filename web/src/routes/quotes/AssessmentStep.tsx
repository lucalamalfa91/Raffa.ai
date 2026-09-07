import { formatMoney, type AssessmentNumber, type LineMarketPositionRow, type QuoteAggregate } from "./quoteCheckViewModel";

export interface AssessmentStepProps {
  blocked: boolean;
  onBackToExtract: () => void;
  numbers: readonly AssessmentNumber[];
  positionRows: readonly LineMarketPositionRow[];
  aggregate: QuoteAggregate;
  onContinue: () => void;
}

/**
 * Assessment step (screens.md #10 AC-3: "4 numbers ..., line-level P25/P50/P75 table with
 * confidence, provenance card"). AC-2's own gate ("assessment blocked until resolved") is rendered
 * here, not navigation-level -- see `QuoteStepper.tsx`'s own header comment.
 */
export default function AssessmentStep({ blocked, onBackToExtract, numbers, positionRows, aggregate, onContinue }: AssessmentStepProps) {
  if (blocked) {
    return (
      <div className="card quote-blocked-card">
        <h4>Assessment blocked</h4>
        <p className="micro-meta">Line-item normalisation is unresolved. Return to Extract and map the unmatched line(s).</p>
        <button type="button" className="btn btn-secondary" onClick={onBackToExtract}>
          ← Back to extract
        </button>
      </div>
    );
  }

  return (
    <div className="quote-assessment-step">
      <div className="quote-assessment-numbers">
        {numbers.map((number) => (
          <div key={number.key} className="quote-assessment-number">
            <span className="micro-meta">{number.label}</span>
            <span className={`key-fact-number${number.emphasize ? " quote-emphasize" : ""}`}>{number.value}</span>
          </div>
        ))}
      </div>

      <h6>Line-level market position</h6>
      {positionRows.length === 0 ? (
        <p className="micro-meta">No lines to assess yet.</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>Line</th>
              <th style={{ textAlign: "right" }}>Quoted</th>
              <th style={{ textAlign: "right" }}>P25</th>
              <th style={{ textAlign: "right" }}>P50</th>
              <th style={{ textAlign: "right" }}>P75</th>
              <th>Position</th>
              <th>Confidence</th>
            </tr>
          </thead>
          <tbody>
            {positionRows.map((row) => (
              <tr key={row.quoteLineId}>
                <td>{row.label}</td>
                <td style={{ textAlign: "right" }}>{row.unitPrice}</td>
                <td style={{ textAlign: "right" }}>{row.p25}</td>
                <td style={{ textAlign: "right" }}>{row.p50}</td>
                <td style={{ textAlign: "right" }}>{row.p75}</td>
                <td>
                  <span className={`tag tag-${row.positionTag.variant}`}>{row.positionTag.label}</span>
                </td>
                <td className="micro-meta">{row.confidence}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <button type="button" className="btn btn-primary quote-step-continue" onClick={onContinue}>
        Set target →
      </button>

      <div className="card">
        <span className="card-kicker">Assessment</span>
        <div className="card-title">
          {aggregate.assessedLineCount} of {aggregate.totalLineCount} line{aggregate.totalLineCount === 1 ? "" : "s"} assessed
        </div>
        <p className="card-body">
          Supplier quote {formatMoney(aggregate.originalTotal, aggregate.currency)} vs expected market range{" "}
          {formatMoney(aggregate.expectedMarketLow, aggregate.currency)}–{formatMoney(aggregate.expectedMarketHigh, aggregate.currency)}.
          Arithmetic is deterministic — every figure is summed from each line's own real benchmark match, never a fabricated
          total. Lines without a resolved benchmark match are excluded from these totals rather than assumed at zero.
        </p>
      </div>
    </div>
  );
}
