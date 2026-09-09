import { Link } from "react-router-dom";
import type { OpportunityRowView } from "./savingsViewModel";

export interface OpportunitiesTableProps {
  rows: readonly OpportunityRowView[];
}

/**
 * AC-2 "Opportunities table: Opportunity - Type - Current spend - Estimated savings - Confidence -
 * Owner - Status - Realized", quoted verbatim as the eight columns below (screens.md #9). AC-3 "Rows
 * open Contract 360 > Benchmark or Quote check" -- the "Opportunity" cell's own label is a real
 * `<Link>` carrying `row.navigation` (`savingsViewModel.ts#getOpportunityNavigation`), the same
 * cell-level-link-not-whole-row-click pattern `../contracts/PortfolioTable.tsx` already established
 * for its own "Rows open Contract 360" row (a bare `<tr onClick>` is not a keyboard-operable control
 * -- ADR-019 accessibility baseline).
 */
export default function OpportunitiesTable({ rows }: OpportunitiesTableProps) {
  return (
    <div className="portfolio-table-wrapper">
      <table className="table savings-opportunities-table">
        <thead>
          <tr>
            <th scope="col">Opportunity</th>
            <th scope="col">Type</th>
            <th scope="col" className="savings-table-numeric">
              Current spend
            </th>
            <th scope="col" className="savings-table-numeric">
              Estimated savings
            </th>
            <th scope="col">Confidence</th>
            <th scope="col">Owner</th>
            <th scope="col">Status</th>
            <th scope="col" className="savings-table-numeric">
              Realized
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key}>
              <td title={row.primaryTitle}>
                {row.navigation.kind === "contract" ? (
                  <Link to={`/contracts/${row.navigation.contractId}`} state={{ tab: "Benchmark" }}>
                    {row.primaryLabel}
                  </Link>
                ) : (
                  <Link to="/quotes">{row.primaryLabel}</Link>
                )}
              </td>
              <td>{row.type}</td>
              <td className="savings-table-numeric">{row.currentSpend}</td>
              <td className="savings-table-numeric">{row.estimatedSavings}</td>
              <td>
                {row.confidence !== null ? (
                  <span className={`tag tag-${row.confidence.variant}`}>{row.confidence.label}</span>
                ) : (
                  <span className="micro-meta">Not yet available</span>
                )}
              </td>
              <td>{row.owner}</td>
              <td>
                <span className={`tag tag-${row.status.variant}`}>{row.status.label}</span>
              </td>
              <td className="savings-table-numeric">{row.realized}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
