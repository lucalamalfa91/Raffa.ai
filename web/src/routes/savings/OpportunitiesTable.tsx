import { Link, useNavigate } from "react-router-dom";
import type { OpportunityNavigation, OpportunityRowView } from "./savingsViewModel";

export interface OpportunitiesTableProps {
  rows: readonly OpportunityRowView[];
}

function hrefFor(navigation: OpportunityNavigation): string {
  return navigation.kind === "contract" ? `/contracts/${navigation.contractId}` : "/quotes";
}

/**
 * The V2 opportunities table (screens-v2.md #8: "Supplier · Action · Estimate · Status, rows open
 * Contract 360"; `app.jsx` `opps`). The Supplier cell's `<Link>` is the keyboard-operable control
 * (ADR-019 accessibility baseline); the row's own click is the prototype's `cg-row` mouse
 * convenience on top. Contract 360 reads `state.from === "savings"` for its back label.
 */
export default function OpportunitiesTable({ rows }: OpportunitiesTableProps) {
  const navigate = useNavigate();

  return (
    <div className="savings-table-wrapper">
      <table className="table savings-table">
        <thead>
          <tr>
            <th scope="col">Supplier</th>
            <th scope="col">Action</th>
            <th scope="col" className="savings-table-numeric">
              Estimate
            </th>
            <th scope="col" className="savings-col-status">
              Status
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const href = hrefFor(row.navigation);
            return (
              <tr
                key={row.key}
                className="savings-row"
                onClick={(event) => {
                  if ((event.target as HTMLElement).closest("a") !== null) return;
                  navigate(href, { state: { from: "savings" } });
                }}
              >
                <td className="savings-cell-supplier" title={row.supplierTitle}>
                  <Link to={href} state={{ from: "savings" }}>
                    {row.supplierLabel}
                  </Link>
                </td>
                <td>{row.action}</td>
                <td className="savings-table-numeric">
                  {row.estimate}
                  {row.confidence !== null && (
                    <span className={`tag tag-${row.confidence.variant} savings-confidence-tag`}>{row.confidence.label}</span>
                  )}
                </td>
                <td>
                  <span className={`tag tag-${row.status.variant}`}>{row.status.label}</span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
