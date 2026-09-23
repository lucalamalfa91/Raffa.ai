import { Link } from "react-router-dom";
import type { VerifiedSavingView } from "./savingsDashboard";

export interface RecentVerifiedProps {
  rows: readonly VerifiedSavingView[];
}

/**
 * The outcomes behind "When you saved", newest first: day recorded · supplier (→ Contract 360) ·
 * lever · verified money. Empty, it says how a saving becomes verified rather than showing nothing.
 */
export default function RecentVerified({ rows }: RecentVerifiedProps) {
  return (
    <section className="savings-panel savings-recent" aria-labelledby="savings-recent-title">
      <div className="savings-panel-head">
        <h3 id="savings-recent-title" className="savings-panel-title">
          Latest verified savings
        </h3>
      </div>
      {rows.length === 0 ? (
        <p className="savings-panel-empty">
          No verified savings recorded yet. A saving is verified when the outcome of a negotiation is recorded against it — in
          Quote check, or when a renewal closes.
        </p>
      ) : (
        <ol className="savings-recent-list">
          {rows.map((row) => (
            <li key={row.key} className="savings-recent-item">
              <span className="savings-recent-date">{row.date}</span>
              <span className="savings-recent-who">
                {row.contractId !== null ? (
                  <Link to={`/contracts/${row.contractId}`} state={{ from: "savings" }}>
                    {row.supplierLabel}
                  </Link>
                ) : (
                  row.supplierLabel
                )}
                <span className="savings-recent-lever"> · {row.lever}</span>
              </span>
              <span className="savings-recent-amount">{row.amount}</span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
