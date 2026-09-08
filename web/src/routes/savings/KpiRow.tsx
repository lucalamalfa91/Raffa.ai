import { buildKpiCells, type KpiFetchState } from "./savingsViewModel";

export interface KpiRowProps {
  kpiState: KpiFetchState;
  onRetry: () => void;
}

/**
 * AC-1 "6 KPI cells" (screens.md #9) + AC-3's own "benchmark-provider-unreachable (KPIs
 * stale-labelled)" state. Cells always render from `kpiState.kpis` (`buildKpiCells` -- see that
 * function's own doc comment for the empty-`lines` "-" honest-gap rule); the notice block above them
 * renders only while `kpiState.stale` is true, matching ADR-018's own IA-level error contract ("2px
 * accent left rule + h4 + plain endpoint/job name + secondary Retry") via the shared `.error-state`
 * class every other screen's own error state already uses (`../renewals/index.tsx`,
 * `../contracts/contract360/index.tsx`) -- `role="alert"` matches that same sibling convention. Copy
 * is quoted from the compiled prototype's own copy for this exact state
 * (`inputs/design/prototypes/day1-demo.html`): "Spend and renewal KPIs are current; savings figures
 * are from the last successful refresh ... Contigo does not show percentiles without provenance." --
 * the "last successful refresh" date is dropped (this screen tracks no such timestamp; inventing one
 * would be exactly the fabricated precision Appendix C rule 10 forbids).
 */
export default function KpiRow({ kpiState, onRetry }: KpiRowProps) {
  if (kpiState.phase === "loading") {
    return (
      <div className="savings-kpi-skeleton" role="status" aria-live="polite">
        <p className="micro-meta">Loading savings KPIs…</p>
        {Array.from({ length: 6 }, (_, index) => (
          <div key={index} className="skeleton savings-kpi-skeleton-cell" />
        ))}
      </div>
    );
  }

  const cells = buildKpiCells(kpiState.kpis);

  return (
    <div className="savings-kpi-section">
      {kpiState.stale && (
        <div className="error-state" role="alert">
          <h4>Benchmark provider unreachable</h4>
          <p className="micro-meta">
            Spend and renewal KPIs are current; savings figures are from the last successful refresh.
            Contigo does not show percentiles without provenance.
          </p>
          <button type="button" className="btn btn-secondary" onClick={onRetry}>
            Retry refresh
          </button>
        </div>
      )}

      <div className="savings-kpi-row" role="group" aria-label="Savings KPIs">
        {cells.map((cell) => (
          <div key={cell.key} className="savings-kpi-cell">
            <span className="savings-kpi-label">{cell.label}</span>
            <div className="savings-kpi-value">
              {cell.lines.length === 0 ? (
                <span className="kpi-number">—</span>
              ) : (
                cell.lines.map((line, index) => (
                  <span key={index} className={`kpi-number${cell.emphasize ? " savings-kpi-emphasize" : ""}`}>
                    {line}
                  </span>
                ))
              )}
            </div>
            {cell.meta !== null && <span className="micro-meta">{cell.meta}</span>}
            {kpiState.stale && (
              // AC-3's own literal "KPIs stale-labelled" -- text, not colour, carries the meaning
              // (ADR-019 accessibility baseline), applied to all six cells (not just the three
              // savings-derived ones) because GET /api/savings/kpis returns all six in one response:
              // a single failed refresh means every figure on screen is from the last successful
              // call, not a subset of them.
              <span className="tag tag-outline savings-kpi-stale-tag">Stale</span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
