import TablePager from "../../components/table/TablePager";
import { usePagedRows } from "../../components/table/pager";
import {
  formatContractRef,
  formatDays,
  formatRenewalSpend,
  formatRenewalSupplier,
  formatScore,
  getRenewalStatusTag,
  isHighPriorityScore,
  isNoticeUrgent,
  type RenewalTableRow,
} from "./renewalPipelineViewModel";

export interface RenewalTableProps {
  /** Already sorted by priority (`renewalPipelineViewModel.ts#buildRenewalRows`). */
  rows: readonly RenewalTableRow[];
  selectedContractId: string | null;
  onSelect: (contractId: string) => void;
  /** Rows ticked for a bulk action (`RenewalBulkBar.tsx`) -- a separate thing from the one row the pane shows. */
  checkedIds: ReadonlySet<string>;
  onToggleChecked: (contractId: string) => void;
  /** Ticks (or clears, when all are ticked) every row in `rows`, not just the page on screen. */
  onToggleAllChecked: () => void;
}

/**
 * The priority list (screens-v2.md #7): a tick box for bulk actions · Score (64px, heading face
 * 18px/800, accent-700 from 80 up) · Supplier · contract (supplier bold, contract type muted, one
 * line, ellipsis) · Annual spend · Renews in · Notice in (accent-700 + 600 within 45 days) · Status
 * (tag), `font-size:13px; font-variant-numeric:tabular-nums`. The selected row carries the
 * prototype's own `bg`/`bar` treatment (neutral-200 background, 3px accent bar -- `.row-selected`).
 *
 * Selecting a row is a native `<button>` in the Score cell (ADR-019 accessibility baseline: every
 * interactive control is native); the row's own click is the prototype's `cg-row` mouse convenience
 * layered on top, never the only way in. The tick box is its own native checkbox and never selects.
 */
export default function RenewalTable({ rows, selectedContractId, onSelect, checkedIds, onToggleChecked, onToggleAllChecked }: RenewalTableProps) {
  const { page, setPage, pageItems, totalItems } = usePagedRows(rows);
  const allChecked = rows.length > 0 && rows.every((row) => checkedIds.has(row.item.contractId));
  const someChecked = !allChecked && rows.some((row) => checkedIds.has(row.item.contractId));

  return (
    <div className="renewal-table-wrapper">
      <table className="table renewal-table" aria-label="Renewals by priority">
        <thead>
          <tr>
            <th scope="col" className="renewal-col-check">
              <input
                type="checkbox"
                className="renewal-check"
                aria-label={allChecked ? "Clear the selection" : `Select all ${rows.length} renewals in this list`}
                checked={allChecked}
                ref={(element) => {
                  if (element) element.indeterminate = someChecked;
                }}
                onChange={onToggleAllChecked}
              />
            </th>
            <th scope="col" className="renewal-col-score">
              Score
            </th>
            <th scope="col">Supplier · contract</th>
            <th scope="col" className="renewal-table-numeric renewal-col-spend">
              Annual spend
            </th>
            <th scope="col" className="renewal-table-numeric renewal-col-days">
              Renews in
            </th>
            <th scope="col" className="renewal-table-numeric renewal-col-days">
              Notice in
            </th>
            <th scope="col" className="renewal-col-status">
              Status
            </th>
          </tr>
        </thead>
        <tbody>
          {pageItems.map(({ item, score, tracked, contract }) => {
            const supplier = formatRenewalSupplier(item.supplierName);
            const contractRef = formatContractRef(item.contractId, contract);
            const statusTag = getRenewalStatusTag(tracked);
            const isSelected = item.contractId === selectedContractId;
            const isChecked = checkedIds.has(item.contractId);
            const urgentScore = score !== null && isHighPriorityScore(score);
            const urgentNotice = isNoticeUrgent(item.daysUntilCancellationDeadline);

            return (
              <tr
                key={item.contractId}
                className={`renewal-row${isSelected ? " row-selected" : ""}${isChecked ? " row-checked" : ""}`}
                aria-selected={isSelected}
                onClick={(event) => {
                  // The Score cell's own <button> and the tick box handle their clicks natively;
                  // everywhere else on the row, select it too (markup.html's `cg-row` row click).
                  if ((event.target as HTMLElement).closest("button, input") !== null) return;
                  onSelect(item.contractId);
                }}
              >
                <td className="renewal-cell-check">
                  <input
                    type="checkbox"
                    className="renewal-check"
                    aria-label={`Select ${supplier} · ${contractRef.label}`}
                    checked={isChecked}
                    onChange={() => onToggleChecked(item.contractId)}
                  />
                </td>
                <td className="renewal-cell-score">
                  <button
                    type="button"
                    className={`renewal-score-select${urgentScore ? " is-urgent" : ""}`}
                    aria-label={`Show why ${supplier} · ${contractRef.label} is here`}
                    aria-pressed={isSelected}
                    onClick={() => onSelect(item.contractId)}
                  >
                    {formatScore(score)}
                  </button>
                </td>
                <td className="renewal-cell-contract">
                  <span className="renewal-supplier">{supplier}</span>{" "}
                  <span className="renewal-contract-ref" title={contractRef.title}>
                    · {contractRef.label}
                  </span>
                </td>
                <td className="renewal-table-numeric renewal-cell-spend">{formatRenewalSpend(item.annualSpend, contract)}</td>
                <td className="renewal-table-numeric">{formatDays(item.daysUntilRenewal)}</td>
                <td className={`renewal-table-numeric${urgentNotice ? " deadline-critical" : ""}`}>
                  {formatDays(item.daysUntilCancellationDeadline)}
                </td>
                <td>
                  <span className={`tag tag-${statusTag.variant}`}>{statusTag.label}</span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <TablePager page={page} totalItems={totalItems} onPageChange={setPage} label="Renewal pages" />
    </div>
  );
}
