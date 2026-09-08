import { Link } from "react-router-dom";
import type { ReviewQueueRow } from "./reviewQueueViewModel";

export interface ReviewQueueTableProps {
  rows: readonly ReviewQueueRow[];
}

/**
 * Landing list for `/review`. Rows with a `contractId` open the already-real
 * `/contracts/:id/review` detail (task E07/F03/US01/T01). Unlinked uploads stay
 * visible with a named reason instead of a dead link.
 */
export default function ReviewQueueTable({ rows }: ReviewQueueTableProps) {
  return (
    <div className="review-queue-table-wrapper">
      <table className="table review-queue-table">
        <thead>
          <tr>
            <th scope="col">Contract</th>
            <th scope="col">Supplier</th>
            <th scope="col">Attention</th>
            <th scope="col">Status</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id}>
              <td>
                {row.contractId !== null ? (
                  <Link to={`/contracts/${row.contractId}/review`} className="review-queue-table-link">
                    {row.label}
                  </Link>
                ) : (
                  <>
                    <span>{row.label}</span>
                    <span className="hint">Not yet linked to a contract</span>
                  </>
                )}
              </td>
              <td>
                <span title={row.supplierTitle}>{row.supplierLabel}</span>
              </td>
              <td>{row.attention}</td>
              <td>
                <span className={`tag tag-${row.statusTag.variant}`}>{row.statusTag.label}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
