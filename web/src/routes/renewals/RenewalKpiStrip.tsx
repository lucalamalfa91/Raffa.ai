import type { KpiCell, KpiKey } from "./case/portfolio";

export interface RenewalKpiStripProps {
  cells: readonly KpiCell[];
  /** The cell currently filtering the list, if any. */
  active: KpiKey | null;
  onToggle: (key: KpiKey) => void;
  label: string;
}

/**
 * The attention strip (`.attention-strip`): one cell per threshold -- notice in 30 / 90 days, not
 * started, in negotiation, closed. Each cell is a filter on the list below: a press selects it, a
 * second press clears it. The number is red only when it is urgent and above zero.
 */
export default function RenewalKpiStrip({ cells, active, onToggle, label }: RenewalKpiStripProps) {
  return (
    <div className="rn-kpis" role="group" aria-label={label}>
      <div className="attention-strip rn-kpi-strip">
        {cells.map((cell) => (
          <button
            key={cell.key}
            type="button"
            className={`attention-cell rn-kpi-cell${cell.urgent ? " is-urgent" : ""}${active === cell.key ? " is-active" : ""}`}
            aria-pressed={active === cell.key}
            data-kpi={cell.key}
            title={cell.meta}
            onClick={() => onToggle(cell.key)}
          >
            <span className="attention-number rn-kpi-number">{cell.value}</span>
            <span className="attention-label rn-kpi-label">{cell.label}</span>
          </button>
        ))}
      </div>
    </div>
  );
}
