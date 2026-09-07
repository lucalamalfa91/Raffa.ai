import { Link } from "react-router-dom";
import { daysUntil } from "../contracts/portfolioAttention";
import { formatAnnualSpend, formatDateOnly, formatSupplier } from "../contracts/portfolioTableFormatters";
import { isDeadlineCritical } from "../../styles/semantics";
import {
  formatContractRef,
  formatScore,
  getRenewalStatusTag,
  isHighPriorityScore,
  type RenewalTableRow,
} from "./renewalPipelineViewModel";

export interface RenewalTableProps {
  rows: readonly RenewalTableRow[];
  selectedContractId: string | null;
  onSelect: (contractId: string) => void;
}

/**
 * AC-2 "Table: Score - Supplier - contract - Annual spend - Renews in - Cancel by - Status" (screens.md
 * #8), quoted verbatim as the seven columns below. `rows` is expected already filtered by the
 * currently-active threshold window (`index.tsx`) -- this component only renders and lets a row be
 * selected for the insight card (AC-3).
 *
 * Selecting a row is a native `<button>` inside the Score cell (ADR-019 accessibility baseline:
 * "every interactive control is native"), not a bare `<tr onClick>` -- a table row itself is not a
 * keyboard-operable control, unlike day1-demo.html's own framework-level `sc-camel-on-click` on its
 * raw `<tr>`. The selected row still gets the visible `.row-selected` treatment
 * (`renewals.css`, mirroring `--color-neutral-200)` quoted from day1-demo.html's own
 * `bg:s.rsel===c.id?'var(--color-neutral-200)':'transparent'`) regardless of which exact control
 * triggered it.
 */
export default function RenewalTable({ rows, selectedContractId, onSelect }: RenewalTableProps) {
  return (
    <div className="portfolio-table-wrapper">
      <table className="table renewal-table">
        <thead>
          <tr>
            <th scope="col" className="renewal-table-numeric">
              Score
            </th>
            <th scope="col">Supplier</th>
            <th scope="col">Contract</th>
            <th scope="col" className="renewal-table-numeric">
              Annual spend
            </th>
            <th scope="col" className="renewal-table-numeric">
              Renews in
            </th>
            <th scope="col">Cancel by</th>
            <th scope="col">Status</th>
          </tr>
        </thead>
        <tbody>
          {rows.map(({ item, score, tracked }) => {
            const supplier = formatSupplier(item.supplierId);
            const contractRef = formatContractRef(item.contractId);
            const cancelDays = daysUntil(item.cancellationDeadline);
            const statusTag = getRenewalStatusTag(tracked);
            const isSelected = item.contractId === selectedContractId;
            const isUrgentScore = score !== null && isHighPriorityScore(score);

            return (
              <tr key={item.contractId} className={isSelected ? "row-selected" : undefined}>
                <td className="renewal-table-numeric">
                  <button
                    type="button"
                    className="renewal-score-select"
                    aria-label={`Show insight card for ${supplier.label} - ${contractRef.label}`}
                    onClick={() => onSelect(item.contractId)}
                  >
                    {/* Quoted from day1-demo.html's own `scoreFg:c.score>=80?'var(--color-accent-700)':'inherit'`. */}
                    <span style={isUrgentScore ? { color: "var(--color-accent-700)", fontWeight: 600 } : undefined}>
                      {formatScore(score)}
                    </span>
                  </button>
                </td>
                <td title={supplier.title}>{supplier.label}</td>
                <td>
                  <Link to={`/contracts/${item.contractId}`} title={contractRef.title}>
                    {contractRef.label}
                  </Link>
                </td>
                <td className="renewal-table-numeric">{formatAnnualSpend(item.annualSpend)}</td>
                <td className="renewal-table-numeric">{item.daysUntilRenewal !== null ? `${item.daysUntilRenewal} d` : "—"}</td>
                <td className={cancelDays !== null && isDeadlineCritical(cancelDays) ? "deadline-critical" : undefined}>
                  {formatDateOnly(item.cancellationDeadline)}
                </td>
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
