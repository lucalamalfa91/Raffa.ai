import { Link, useNavigate } from "react-router-dom";
import TablePager from "../../components/table/TablePager";
import { usePagedRows } from "../../components/table/pager";
import { formatAnnualSpend, formatAutoRenewal, formatPortfolioDate, formatRisk, getContractTypeLabel, getPortfolioStatusTag } from "./portfolioTableFormatters";
import type { PortfolioRow } from "./portfolioViewModel";

export interface PortfolioTableProps {
  /** Already validated-only and sorted by notice deadline (`portfolioViewModel.ts#buildPortfolioRows`). */
  rows: readonly PortfolioRow[];
  /** `moreCols`: also show Start · Auto · Risk. */
  moreColumns: boolean;
}

/**
 * The V2 Portfolio table (`Raffa.ai V2.dc.html` PORTFOLIO block, quoted verbatim in
 * `contracts.css`'s header comment): Supplier · Contract · Annual spend · Ends · Give notice by
 * (+ "· N d") [· Start · Auto · Risk when "More columns" is on] · Status. `font-size:13px;
 * font-variant-numeric:tabular-nums; table-layout:fixed; min-width:760px`, a 3px left bar on the
 * supplier cell and an accent-100 row tint for the rows whose notice deadline falls within 45 days
 * (`c.rowBg` / `c.bar` / `c.cancelFg` / `c.cancelW`), and "Rows open Contract 360" (`c.open`).
 *
 * Every row is a real `<Link>` in its Contract cell (the keyboard-/screen-reader-operable control,
 * ADR-019 accessibility baseline); the row's own click is the prototype's `cg-row` mouse convenience
 * layered on top of it, never the only way in. Supplier is the wire's own `supplierName` (R-SUP-04),
 * an honest "—" when no supplier is linked; Contract shows the type label -- `Contract` has no
 * title field, the same proxy every other screen uses for this gap.
 *
 * The header row is the prototype's own: one uppercase label per column and nothing else. Risk is
 * plain text (`{{ c.risk }}`), Status the one `.tag` in the row (`{{ c.statusTag }}`, `padding:2px
 * 8px; font-size:10px`).
 */
export default function PortfolioTable({ rows, moreColumns }: PortfolioTableProps) {
  const navigate = useNavigate();
  const { page, setPage, pageItems: visibleRows, totalItems } = usePagedRows(rows, moreColumns);

  return (
    <div className="portfolio-table-wrapper">
      <table className="table portfolio-table">
        <thead>
          <tr>
            <th scope="col" className="portfolio-col-supplier">
              Supplier
            </th>
            <th scope="col">Contract</th>
            <th scope="col" className="portfolio-table-numeric portfolio-col-spend">
              Annual spend
            </th>
            <th scope="col" className="portfolio-col-ends">
              Ends
            </th>
            <th scope="col" className="portfolio-col-notice">
              Give notice by
            </th>
            {moreColumns && (
              <>
                <th scope="col" className="portfolio-col-start">
                  Start
                </th>
                <th scope="col" className="portfolio-col-auto">
                  Auto
                </th>
                <th scope="col" className="portfolio-col-risk">
                  Risk
                </th>
              </>
            )}
            <th scope="col" className="portfolio-col-status">
              Status
            </th>
          </tr>
        </thead>
        <tbody>
          {visibleRows.map(({ item, cancelDays, isUrgent }) => {
            const statusTag = getPortfolioStatusTag(item.status);
            const contractHref = `/contracts/${item.contractId}`;

            return (
              <tr
                key={item.contractId}
                className={`portfolio-row${isUrgent ? " row-critical" : ""}`}
                onClick={(event) => {
                  // The Contract cell's own <Link> handles its click natively; everywhere else on
                  // the row, follow it (the prototype's `cg-row` row click).
                  if ((event.target as HTMLElement).closest("a") !== null) return;
                  navigate(contractHref);
                }}
              >
                <td className="portfolio-cell-supplier">{item.supplierName ?? "—"}</td>
                <td className="portfolio-cell-contract">
                  <Link to={contractHref}>{getContractTypeLabel(item.type)}</Link>
                </td>
                <td className="portfolio-table-numeric portfolio-cell-spend">{formatAnnualSpend(item.annualSpend, item.currency)}</td>
                <td>{formatPortfolioDate(item.endDate)}</td>
                <td className={isUrgent ? "portfolio-cell-notice deadline-critical" : "portfolio-cell-notice"}>
                  {formatPortfolioDate(item.cancellationDeadline)}
                  {cancelDays !== null && <span className="portfolio-notice-days"> · {cancelDays} d</span>}
                </td>
                {moreColumns && (
                  <>
                    <td className="portfolio-cell-start">{formatPortfolioDate(item.startDate)}</td>
                    <td>{formatAutoRenewal(item.autoRenewal)}</td>
                    <td>{formatRisk(item.risk)}</td>
                  </>
                )}
                <td>
                  <span className={`tag tag-${statusTag.variant} portfolio-status-tag`}>{statusTag.label}</span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <TablePager page={page} totalItems={totalItems} onPageChange={setPage} label="Portfolio pages" />
    </div>
  );
}
