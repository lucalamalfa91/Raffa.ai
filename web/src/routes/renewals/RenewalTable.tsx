import {
  formatContractRef,
  formatDays,
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
}

/**
 * The V2 priority list (screens-v2.md #7; `raffa-v2/markup.html` "RENEWALS" block): Score (64px,
 * heading face 18px/800, accent-700 from 80 up) · Supplier · contract (supplier bold, "· contract"
 * muted, one line, ellipsis) · Renews in (right, "N d") · Notice in (right, "N d", accent-700 + 600
 * within 45 days) · Status (tag), `font-size:13px; font-variant-numeric:tabular-nums;
 * table-layout:fixed`. The selected row carries the prototype's own `bg`/`bar` treatment
 * (neutral-200 background, 3px accent bar -- `.row-selected`, `renewals.css`).
 *
 * Selecting a row is a native `<button>` in the Score cell (ADR-019 accessibility baseline: every
 * interactive control is native); the row's own click is the prototype's `cg-row` mouse convenience
 * layered on top, never the only way in.
 */
export default function RenewalTable({ rows, selectedContractId, onSelect }: RenewalTableProps) {
  return (
    <div className="renewal-table-wrapper">
      <table className="table renewal-table">
        <thead>
          <tr>
            <th scope="col" className="renewal-col-score">
              Score
            </th>
            <th scope="col">Supplier · contract</th>
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
          {rows.map(({ item, score, tracked }) => {
            const supplier = formatRenewalSupplier(item.supplierName);
            const contractRef = formatContractRef(item.contractId);
            const statusTag = getRenewalStatusTag(tracked);
            const isSelected = item.contractId === selectedContractId;
            const urgentScore = score !== null && isHighPriorityScore(score);
            const urgentNotice = isNoticeUrgent(item.daysUntilCancellationDeadline);

            return (
              <tr
                key={item.contractId}
                className={`renewal-row${isSelected ? " row-selected" : ""}`}
                aria-selected={isSelected}
                onClick={(event) => {
                  // The Score cell's own <button> handles its click natively; everywhere else on the
                  // row, select it too (markup.html's `cg-row` row click).
                  if ((event.target as HTMLElement).closest("button") !== null) return;
                  onSelect(item.contractId);
                }}
              >
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
    </div>
  );
}
