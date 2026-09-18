import { useMemo, useState, type ChangeEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { formatAnnualSpend, formatAutoRenewal, formatDateOnly, getContractTypeLabel, getPortfolioRiskTag, getPortfolioStatusTag } from "./portfolioTableFormatters";
import {
  EMPTY_PORTFOLIO_COLUMN_FILTERS,
  filterPortfolioRows,
  getPortfolioRiskFilterOptions,
  getPortfolioStatusFilterOptions,
  isPortfolioColumnFilterActive,
  PORTFOLIO_AUTO_FILTER_OPTIONS,
  type PortfolioColumnFilters,
} from "./portfolioColumnFilters";
import type { PortfolioRow } from "./portfolioViewModel";

/**
 * w17 immediate-visibility: returns the label to show in the "Contract" column. For validated
 * contracts, this is the contract-type label (same as before). For pending contracts (still
 * uploading / processing), the filename is the only identifying information available, so it is
 * shown instead. A missing filename (orphaned shell) falls back to the type label.
 */
function getContractLabel(isPending: boolean, fileName: string | null | undefined, type: import("../../api/client").PortfolioContractType): string {
  if (isPending && fileName) return fileName;
  return getContractTypeLabel(type);
}

export interface PortfolioTableProps {
  /** Already validated-only and sorted by notice deadline (`portfolioViewModel.ts#buildPortfolioRows`). */
  rows: readonly PortfolioRow[];
  /** `moreCols` (`app.jsx`): also show Start · Auto · Risk. */
  moreColumns: boolean;
}

function ColumnInputFilter({
  id,
  label,
  value,
  onChange,
  type,
  placeholder,
  min,
  step,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  type: "search" | "date" | "number";
  placeholder?: string;
  min?: number;
  step?: string;
}) {
  return (
    <input
      id={id}
      className="input portfolio-col-filter"
      type={type}
      value={value}
      placeholder={placeholder}
      min={min}
      step={step}
      aria-label={label}
      autoComplete="off"
      onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)}
      onClick={(event) => event.stopPropagation()}
    />
  );
}

function ColumnSelectFilter({
  id,
  label,
  value,
  options,
  allLabel,
  onChange,
}: {
  id: string;
  label: string;
  value: string | null;
  options: readonly string[];
  allLabel: string;
  onChange: (value: string | null) => void;
}) {
  return (
    <select
      id={id}
      className="input portfolio-col-filter"
      value={value ?? ""}
      aria-label={label}
      onChange={(event) => onChange(event.target.value === "" ? null : event.target.value)}
      onClick={(event) => event.stopPropagation()}
    >
      <option value="">{allLabel}</option>
      {options.map((option) => (
        <option key={option} value={option}>
          {option}
        </option>
      ))}
    </select>
  );
}

/**
 * The V2 Portfolio table (screens-v2.md #6; `raffa-v2/markup.html` "PORTFOLIO" block): Supplier ·
 * Contract · Annual spend · Ends · Give notice by (+ "· N d") [· Start · Auto · Risk when "More
 * columns" is on] · Status, `font-size:13px; font-variant-numeric:tabular-nums; table-layout:fixed;
 * min-width:760px`, a 3px left bar on the supplier cell and an accent-100 row tint for the rows whose
 * notice deadline falls within 45 days (`c.rowBg` / `c.bar` / `c.cancelFg` / `c.cancelW`), and
 * "Rows open Contract 360" (`c.open`).
 *
 * Every row is a real `<Link>` in its Contract cell (the keyboard-/screen-reader-operable control,
 * ADR-019 accessibility baseline); the row's own click is the prototype's `cg-row` mouse convenience
 * layered on top of it, never the only way in. Supplier is the wire's own `supplierName` (R-SUP-04),
 * an honest "—" when no supplier is linked; Contract shows the type label -- `Contract` has no
 * title field, the same proxy every other screen uses for this gap.
 *
 * Column filters live in each header cell (not a disconnected toolbar), typed by the column:
 * text contains on Supplier / Contract, native date on Ends / Give notice by / Start, number on
 * Annual spend, selects on Auto / Risk / Status (the same `.input` + `<select>` pattern Savings
 * and review fields already use). Filtering is client-side over the already-loaded page.
 */
export default function PortfolioTable({ rows, moreColumns }: PortfolioTableProps) {
  const navigate = useNavigate();
  const [filters, setFilters] = useState<PortfolioColumnFilters>(EMPTY_PORTFOLIO_COLUMN_FILTERS);

  const visibleRows = useMemo(() => filterPortfolioRows(rows, filters), [rows, filters]);
  const statusOptions = useMemo(() => getPortfolioStatusFilterOptions(rows), [rows]);
  const riskOptions = useMemo(() => getPortfolioRiskFilterOptions(rows), [rows]);
  const filtersActive = isPortfolioColumnFilterActive(filters);

  const patchFilters = (patch: Partial<PortfolioColumnFilters>) => {
    setFilters((current) => ({ ...current, ...patch }));
  };

  return (
    <div className="portfolio-table-wrapper">
      {filtersActive && (
        <div className="portfolio-filter-clear">
          <button type="button" className="btn-ghost" onClick={() => setFilters(EMPTY_PORTFOLIO_COLUMN_FILTERS)}>
            Clear filters
          </button>
        </div>
      )}
      <table className="table portfolio-table">
        <thead>
          <tr>
            <th scope="col" className="portfolio-col-supplier">
              <span>Supplier</span>
              <ColumnInputFilter
                id="portfolio-filter-supplier"
                type="search"
                label="Filter by supplier"
                placeholder="Contains"
                value={filters.supplier}
                onChange={(supplier) => patchFilters({ supplier })}
              />
            </th>
            <th scope="col">
              <span>Contract</span>
              <ColumnInputFilter
                id="portfolio-filter-contract"
                type="search"
                label="Filter by contract"
                placeholder="Contains"
                value={filters.contract}
                onChange={(contract) => patchFilters({ contract })}
              />
            </th>
            <th scope="col" className="portfolio-table-numeric portfolio-col-spend">
              <span>Annual spend</span>
              <ColumnInputFilter
                id="portfolio-filter-spend"
                type="number"
                label="Filter by annual spend"
                min={0}
                step="1"
                value={filters.spend}
                onChange={(spend) => patchFilters({ spend })}
              />
            </th>
            <th scope="col" className="portfolio-col-ends">
              <span>Ends</span>
              <ColumnInputFilter id="portfolio-filter-ends" type="date" label="Filter by end date" value={filters.ends} onChange={(ends) => patchFilters({ ends })} />
            </th>
            <th scope="col" className="portfolio-col-notice">
              <span>Give notice by</span>
              <ColumnInputFilter
                id="portfolio-filter-notice"
                type="date"
                label="Filter by notice date"
                value={filters.notice}
                onChange={(notice) => patchFilters({ notice })}
              />
            </th>
            {moreColumns && (
              <>
                <th scope="col" className="portfolio-col-start">
                  <span>Start</span>
                  <ColumnInputFilter
                    id="portfolio-filter-start"
                    type="date"
                    label="Filter by start date"
                    value={filters.start}
                    onChange={(start) => patchFilters({ start })}
                  />
                </th>
                <th scope="col" className="portfolio-col-auto">
                  <span>Auto</span>
                  <ColumnSelectFilter
                    id="portfolio-filter-auto"
                    label="Filter by auto-renewal"
                    value={filters.auto}
                    options={PORTFOLIO_AUTO_FILTER_OPTIONS}
                    allLabel="All"
                    onChange={(auto) => patchFilters({ auto })}
                  />
                </th>
                <th scope="col" className="portfolio-col-risk">
                  <span>Risk</span>
                  <ColumnSelectFilter
                    id="portfolio-filter-risk"
                    label="Filter by risk"
                    value={filters.risk}
                    options={riskOptions}
                    allLabel="All"
                    onChange={(risk) => patchFilters({ risk })}
                  />
                </th>
              </>
            )}
            <th scope="col" className="portfolio-col-status">
              <span>Status</span>
              <ColumnSelectFilter
                id="portfolio-filter-status"
                label="Filter by status"
                value={filters.status}
                options={statusOptions}
                allLabel="All"
                onChange={(status) => patchFilters({ status })}
              />
            </th>
          </tr>
        </thead>
        <tbody>
          {visibleRows.length === 0 ? (
            <tr>
              <td colSpan={moreColumns ? 9 : 6}>
                <p className="micro-meta" role="status">
                  No contracts match the selected filters. Clear a column filter to see the full list.
                </p>
              </td>
            </tr>
          ) : (
            visibleRows.map(({ item, cancelDays, isUrgent, isPending }) => {
              const statusTag = getPortfolioStatusTag(item.status);
              const riskTag = getPortfolioRiskTag(item.risk);
              const contractHref = `/contracts/${item.contractId}`;
              const contractLabel = getContractLabel(isPending, item.fileName, item.type);

              return (
                <tr
                  key={item.contractId}
                  className={`portfolio-row${isUrgent ? " row-critical" : ""}${isPending ? " row-pending" : ""}`}
                  onClick={(event) => {
                    // The Contract cell's own <Link> handles its click natively; everywhere else on
                    // the row, follow it (markup.html's `cg-row` row click). Filter controls in the
                    // header never reach here.
                    if ((event.target as HTMLElement).closest("a") !== null) return;
                    navigate(contractHref);
                  }}
                >
                  <td className="portfolio-cell-supplier">{item.supplierName ?? "—"}</td>
                  <td className="portfolio-cell-contract">
                    <Link to={contractHref} title={isPending ? item.fileName ?? undefined : undefined}>
                      {contractLabel}
                    </Link>
                  </td>
                  <td className="portfolio-table-numeric portfolio-cell-spend">{formatAnnualSpend(item.annualSpend)}</td>
                  <td>{formatDateOnly(item.endDate)}</td>
                  <td className={isUrgent ? "portfolio-cell-notice deadline-critical" : "portfolio-cell-notice"}>
                    {formatDateOnly(item.cancellationDeadline)}
                    {cancelDays !== null && <span className="portfolio-notice-days"> · {cancelDays} d</span>}
                  </td>
                  {moreColumns && (
                    <>
                      <td className="portfolio-cell-start">{formatDateOnly(item.startDate)}</td>
                      <td>{formatAutoRenewal(item.autoRenewal)}</td>
                      <td>
                        <span className={`tag tag-${riskTag.variant}`}>{riskTag.label}</span>
                      </td>
                    </>
                  )}
                  <td>
                    <span className={`tag tag-${statusTag.variant}`}>{statusTag.label}</span>
                  </td>
                </tr>
              );
            })
          )}
        </tbody>
      </table>
    </div>
  );
}
