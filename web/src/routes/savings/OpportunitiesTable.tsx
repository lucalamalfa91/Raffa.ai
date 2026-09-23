import { Link, useNavigate } from "react-router-dom";
import TablePager from "../../components/table/TablePager";
import { usePagedRows } from "../../components/table/pager";
import AskRaffaLink from "../../components/ask-bar/AskRaffaLink";
import { formatNoticeDays, type OpportunityNavigation, type OpportunityRowView } from "./savingsViewModel";

export interface OpportunitiesTableProps {
  rows: readonly OpportunityRowView[];
}

function hrefFor(navigation: OpportunityNavigation): string {
  return navigation.kind === "contract" ? `/contracts/${navigation.contractId}` : "/quotes";
}

/**
 * The opportunities table: Supplier · Lever · Current spend · Estimate (+ confidence) · Status ·
 * Notice in · Next step. Rows open Contract 360; "Next step" leads to the two places an open saving
 * is worked -- Renewals (with that contract selected, while its notice date is ahead) and Ask Raffa
 * (bound to the contract). The Supplier cell's `<Link>` is the keyboard-operable way into the row
 * (ADR-019 accessibility baseline); the row's own click is the mouse convenience on top. Contract
 * 360 reads `state.from === "savings"` for its back label.
 */
export default function OpportunitiesTable({ rows }: OpportunitiesTableProps) {
  const navigate = useNavigate();
  const { page, setPage, pageItems, totalItems } = usePagedRows(rows);

  return (
    <div className="savings-table-wrapper">
      <table className="table savings-table" aria-label="Opportunities">
        <thead>
          <tr>
            <th scope="col">Supplier</th>
            <th scope="col">Lever</th>
            <th scope="col" className="savings-table-numeric">
              Current spend
            </th>
            <th scope="col" className="savings-table-numeric">
              Estimate
            </th>
            <th scope="col" className="savings-col-status">
              Status
            </th>
            <th scope="col" className="savings-table-numeric">
              Notice in
            </th>
            <th scope="col" className="savings-col-next">
              Next step
            </th>
          </tr>
        </thead>
        <tbody>
          {pageItems.map((row) => {
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
                <td className="savings-table-numeric savings-cell-muted">{row.currentSpend}</td>
                <td className="savings-table-numeric">
                  <span className="savings-cell-estimate">{row.estimate}</span>
                  {row.confidence !== null && (
                    <span className={`tag tag-${row.confidence.variant} savings-confidence-tag`}>{row.confidence.label}</span>
                  )}
                </td>
                <td>
                  <span className={`tag tag-${row.status.variant}`}>{row.status.label}</span>
                </td>
                <td className={`savings-table-numeric${row.noticeUrgent ? " deadline-critical" : ""}`}>{formatNoticeDays(row.noticeDays)}</td>
                <td className="savings-cell-next">
                  {row.renewalHref !== null && (
                    <Link to={row.renewalHref} className="savings-next-link" aria-label={`Work the ${row.supplierLabel} saving in Renewals`}>
                      Renewals →
                    </Link>
                  )}
                  <AskRaffaLink
                    question={row.askQuestion}
                    scopeContractId={row.contractId}
                    className="savings-next-link"
                    ariaLabel={`Ask Raffa about the ${row.supplierLabel} saving`}
                  >
                    Ask →
                  </AskRaffaLink>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <TablePager page={page} totalItems={totalItems} onPageChange={setPage} label="Opportunity pages" />
    </div>
  );
}
