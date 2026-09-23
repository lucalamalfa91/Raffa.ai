import { SAVINGS_STAGES, type BreakdownDimension, type BreakdownRowView } from "./savingsDashboard";

export interface SavingsBreakdownProps {
  dimension: BreakdownDimension;
  onDimensionChange: (dimension: BreakdownDimension) => void;
  rows: readonly BreakdownRowView[];
  /** The supplier the table is filtered to right now -- its row reads as pressed. */
  activeSupplier: string | null;
  /** A supplier row filters the opportunities table to that supplier (a second press clears it). */
  onSelectSupplier: (supplier: string | null) => void;
}

/**
 * "Where the savings are": one horizontal bar per supplier (or per lever), largest first, each bar
 * stacked identified / in progress / verified in the same three colours as the pipeline and sized
 * against the largest row; the total rides the bar end. Past six rows the tail folds into "Other".
 * Supplier rows are also a filter: pressing one narrows the opportunities table to it.
 */
export default function SavingsBreakdown({ dimension, onDimensionChange, rows, activeSupplier, onSelectSupplier }: SavingsBreakdownProps) {
  return (
    <section className="savings-panel savings-breakdown" aria-labelledby="savings-breakdown-title">
      <div className="savings-panel-head savings-panel-head-row">
        <h3 id="savings-breakdown-title" className="savings-panel-title">
          Where the savings are
        </h3>
        <div className="seg savings-seg" role="group" aria-label="Group savings by">
          <button type="button" aria-pressed={dimension === "supplier"} onClick={() => onDimensionChange("supplier")}>
            By supplier
          </button>
          <button type="button" aria-pressed={dimension === "lever"} onClick={() => onDimensionChange("lever")}>
            By lever
          </button>
        </div>
      </div>

      {rows.length === 0 ? (
        <p className="savings-panel-empty">No opportunity in this currency yet.</p>
      ) : (
        <ul className="savings-breakdown-list">
          {rows.map((row) => {
            const interactive = dimension === "supplier" && !row.isOther;
            const pressed = interactive && activeSupplier === row.label;
            const description = SAVINGS_STAGES.filter(({ key }) => row.values[key] > 0)
              .map(({ key, label }) => `${label.toLowerCase()} ${Math.round(row.values[key]).toLocaleString("en-GB")}`)
              .join(", ");
            return (
              <li key={row.key} className="savings-breakdown-row">
                {interactive ? (
                  <button
                    type="button"
                    className="savings-breakdown-label is-button"
                    aria-pressed={pressed}
                    title={pressed ? "Show every supplier in the table" : `Show only ${row.label} in the table`}
                    onClick={() => onSelectSupplier(pressed ? null : row.label)}
                  >
                    {row.label}
                  </button>
                ) : (
                  <span className="savings-breakdown-label">{row.label}</span>
                )}
                <span className="savings-breakdown-track" role="img" aria-label={`${row.label}: ${row.totalLabel} (${description})`}>
                  <span className="savings-breakdown-bar" style={{ width: `${Math.max(row.scale * 100, 0.5)}%` }}>
                    {SAVINGS_STAGES.filter(({ key }) => row.values[key] > 0).map(({ key }) => (
                      <span key={key} className={`savings-breakdown-segment savings-stage-${key}`} style={{ flexGrow: row.values[key] }} />
                    ))}
                  </span>
                  <span className="savings-breakdown-total">{row.totalLabel}</span>
                </span>
                <span className="savings-breakdown-count">
                  {row.opportunityCount} opp.
                </span>
              </li>
            );
          })}
        </ul>
      )}

      <ul className="savings-legend" aria-label="Stages">
        {SAVINGS_STAGES.map(({ key, label }) => (
          <li key={key}>
            <span className={`savings-swatch savings-stage-${key}`} aria-hidden="true" />
            {label}
          </li>
        ))}
      </ul>
    </section>
  );
}
