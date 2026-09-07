import { isPortfolioRiskSeverity, type PortfolioRiskSeverity } from "../../api/client";
import {
  EMPTY_PORTFOLIO_FILTERS,
  RENEWAL_WINDOW_PRESETS,
  isAnyPortfolioFilterActive,
  type AutoRenewalFilterValue,
  type PortfolioFilterState,
} from "./portfolioFilterState";

export interface PortfolioFiltersProps {
  filters: PortfolioFilterState;
  onChange: (filters: PortfolioFilterState) => void;
}

const RISK_OPTIONS: readonly PortfolioRiskSeverity[] = ["Low", "Medium", "High", "Critical"];
const AUTO_RENEWAL_OPTIONS: readonly { value: AutoRenewalFilterValue; label: string }[] = [
  { value: "any", label: "Any" },
  { value: "yes", label: "Yes" },
  { value: "no", label: "No" },
];

/**
 * AC-1 "Filter chips (Supplier, Category, Renewal period, Spend, Status, Risk, Auto-renewal)" -- the
 * seven chips, in this exact order (day1-demo.html's own `filters` array; the parent story's own AC-1
 * list). See `portfolioFilterState.ts`'s header comment for why Supplier is a free-text id (no
 * name-resolution endpoint exists yet) and Category is a disabled placeholder (no Category concept
 * exists on the backend yet) rather than the two being silently dropped.
 */
export default function PortfolioFilters({ filters, onChange }: PortfolioFiltersProps) {
  const update = (patch: Partial<PortfolioFilterState>) => onChange({ ...filters, ...patch });

  return (
    <fieldset className="portfolio-filters">
      <legend className="screen-kicker">Filters</legend>

      <div className="field portfolio-filter-field">
        <label htmlFor="portfolio-filter-supplier">Supplier</label>
        <input
          id="portfolio-filter-supplier"
          className="input"
          type="text"
          placeholder="Supplier id"
          value={filters.supplierId}
          onChange={(event) => update({ supplierId: event.target.value })}
          aria-describedby="portfolio-filter-supplier-hint"
        />
        <span id="portfolio-filter-supplier-hint" className="hint">
          Name lookup isn't available yet -- filters by id.
        </span>
      </div>

      <div className="field portfolio-filter-field">
        <label htmlFor="portfolio-filter-category">Category</label>
        <select id="portfolio-filter-category" className="input" disabled aria-describedby="portfolio-filter-category-hint">
          <option>All categories</option>
        </select>
        <span id="portfolio-filter-category-hint" className="hint">
          Ships once Suppliers/Products exists.
        </span>
      </div>

      <div className="field portfolio-filter-field">
        <label htmlFor="portfolio-filter-renewal">Renewal period</label>
        <select
          id="portfolio-filter-renewal"
          className="input"
          value={String(filters.renewalWithinDays)}
          onChange={(event) => {
            const raw = event.target.value;
            update({ renewalWithinDays: raw === "" ? "" : Number(raw) });
          }}
        >
          {RENEWAL_WINDOW_PRESETS.map((preset) => (
            <option key={preset.label} value={String(preset.days)}>
              {preset.label}
            </option>
          ))}
        </select>
      </div>

      <div className="field portfolio-filter-field">
        <label id="portfolio-filter-spend-label">Spend</label>
        <div className="portfolio-filter-spend-inputs" role="group" aria-labelledby="portfolio-filter-spend-label">
          <input
            className="input"
            type="number"
            inputMode="decimal"
            placeholder="Min"
            aria-label="Minimum annual spend"
            value={filters.minAnnualSpend}
            onChange={(event) => update({ minAnnualSpend: event.target.value })}
          />
          <input
            className="input"
            type="number"
            inputMode="decimal"
            placeholder="Max"
            aria-label="Maximum annual spend"
            value={filters.maxAnnualSpend}
            onChange={(event) => update({ maxAnnualSpend: event.target.value })}
          />
        </div>
      </div>

      <div className="field portfolio-filter-field">
        <label htmlFor="portfolio-filter-status">Status</label>
        <input
          id="portfolio-filter-status"
          className="input"
          type="text"
          placeholder="e.g. active"
          value={filters.status}
          onChange={(event) => update({ status: event.target.value })}
          aria-describedby="portfolio-filter-status-hint"
        />
        <span id="portfolio-filter-status-hint" className="hint">
          Exact match against the contract's recorded status.
        </span>
      </div>

      <div className="field portfolio-filter-field">
        <label htmlFor="portfolio-filter-risk">Risk</label>
        <select
          id="portfolio-filter-risk"
          className="input"
          value={filters.risk}
          onChange={(event) => {
            const raw = event.target.value;
            if (raw === "") {
              update({ risk: "" });
              return;
            }
            if (isPortfolioRiskSeverity(raw)) {
              update({ risk: raw });
            }
          }}
        >
          <option value="">Any risk</option>
          {RISK_OPTIONS.map((risk) => (
            <option key={risk} value={risk}>
              {risk}
            </option>
          ))}
        </select>
      </div>

      <div className="field portfolio-filter-field">
        <label id="portfolio-filter-auto-renewal-label">Auto-renewal</label>
        <div className="seg" role="group" aria-labelledby="portfolio-filter-auto-renewal-label">
          {AUTO_RENEWAL_OPTIONS.map((option) => (
            <button
              key={option.value}
              type="button"
              aria-pressed={filters.autoRenewal === option.value}
              onClick={() => update({ autoRenewal: option.value })}
            >
              {option.label}
            </button>
          ))}
        </div>
      </div>

      <button
        type="button"
        className="btn btn-ghost portfolio-filters-clear"
        disabled={!isAnyPortfolioFilterActive(filters)}
        onClick={() => onChange(EMPTY_PORTFOLIO_FILTERS)}
      >
        Clear filters
      </button>
    </fieldset>
  );
}
