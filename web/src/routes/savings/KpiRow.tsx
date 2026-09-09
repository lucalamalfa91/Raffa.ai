import { buildKpiCells, type KpiFetchState } from "./savingsViewModel";

export interface KpiRowProps {
  kpiState: KpiFetchState;
  onRetry: () => void;
}

/**
 * The V2 three-cell KPI band (screens-v2.md #8: "Contracts analyzed · Upcoming renewals · Savings
 * identified (with meta lines)") plus the "benchmark-provider-unreachable → KPIs stale-labelled"
 * state. Cells always render from `kpiState.kpis` (`buildKpiCells` -- an empty `lines` array is an
 * honest "—", never a fabricated number); the notice above them renders only while `kpiState.stale`
 * is true, through the shared `.error-state` every other screen's error uses.
 */
export default function KpiRow({ kpiState, onRetry }: KpiRowProps) {
  if (kpiState.phase === "loading") {
    return (
      <div className="savings-kpi-skeleton" role="status" aria-live="polite">
        <p className="micro-meta">Loading savings KPIs…</p>
        {Array.from({ length: 3 }, (_, index) => (
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
            Spend and renewal KPIs are current; savings figures are from the last successful refresh. Contigo does not show
            percentiles without provenance.
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
              {cell.lines.length === 0 ? <span>—</span> : cell.lines.map((line, index) => <span key={index}>{line}</span>)}
            </div>
            {cell.meta !== null && <span className="savings-kpi-meta">{cell.meta}</span>}
            {kpiState.stale && (
              // Text, not colour, carries the meaning (ADR-019 accessibility baseline); applied to
              // every cell because GET /api/savings/kpis returns all three in one response.
              <span className="tag tag-outline savings-kpi-stale-tag">Stale</span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
