import { Link, useNavigate } from "react-router-dom";
import { formatAnnualSpend, formatAutoRenewal, formatDateOnly, getContractTypeLabel, getPortfolioRiskTag, getPortfolioStatusTag } from "./portfolioTableFormatters";
import type { PortfolioRow } from "./portfolioViewModel";

export interface PortfolioTableProps {
  /** Already validated-only and sorted by notice deadline (`portfolioViewModel.ts#buildPortfolioRows`). */
  rows: readonly PortfolioRow[];
  /** `moreCols` (`app.jsx`): also show Start · Auto · Risk. */
  moreColumns: boolean;
}

/**
 * The V2 Portfolio table (screens-v2.md #6; `contigo-v2/markup.html` "PORTFOLIO" block): Supplier ·
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
 */
export default function PortfolioTable({ rows, moreColumns }: PortfolioTableProps) {
  const navigate = useNavigate();

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
          {rows.map(({ item, cancelDays, isUrgent }) => {
            const statusTag = getPortfolioStatusTag(item.status);
            const riskTag = getPortfolioRiskTag(item.risk);
            const contractHref = `/contracts/${item.contractId}`;

            return (
              <tr
                key={item.contractId}
                className={`portfolio-row${isUrgent ? " row-critical" : ""}`}
                onClick={(event) => {
                  // The Contract cell's own <Link> handles its click natively; everywhere else on
                  // the row, follow it (markup.html's `cg-row` row click).
                  if ((event.target as HTMLElement).closest("a") !== null) return;
                  navigate(contractHref);
                }}
              >
                <td className="portfolio-cell-supplier">{item.supplierName ?? "—"}</td>
                <td className="portfolio-cell-contract">
                  <Link to={contractHref}>{getContractTypeLabel(item.type)}</Link>
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
          })}
        </tbody>
      </table>
    </div>
  );
}
