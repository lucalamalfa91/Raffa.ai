import { isKpiFilterActive, type RenewalKpiCellView, type RenewalListFilters } from "./renewalPipelineViewModel";

export interface RenewalKpiStripProps {
  cells: readonly RenewalKpiCellView[];
  filters: RenewalListFilters;
  onToggle: (cell: RenewalKpiCellView) => void;
}

/**
 * The design system's attention strip (`.attention-strip`: equal cells, big number + label, each a
 * filter): notice due in 30 / 90 days, spend renewing inside 90 days, and the team's own workflow --
 * not started, in negotiation, closed. A press filters the list below; a second press clears it.
 */
export default function RenewalKpiStrip({ cells, filters, onToggle }: RenewalKpiStripProps) {
  return (
    <div className="renewal-kpis" role="group" aria-label="Renewal KPIs">
      <div className="attention-strip renewal-kpi-strip">
        {cells.map((cell) => {
          const active = isKpiFilterActive(cell, filters);
          return (
            <button
              key={cell.key}
              type="button"
              className={`attention-cell renewal-kpi-cell${cell.urgent ? " is-urgent" : ""}${active ? " is-active" : ""}`}
              aria-pressed={active}
              data-kpi={cell.key}
              onClick={() => onToggle(cell)}
            >
              <span className="attention-label renewal-kpi-label">{cell.label}</span>
              <span className="attention-number renewal-kpi-number">{cell.value}</span>
              <span className="renewal-kpi-meta">{cell.meta}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
