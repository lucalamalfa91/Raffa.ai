import { Link } from "react-router-dom";
import type { AttentionRow } from "./portfolioAttention";
import {
  formatAnnualSpend,
  formatAutoRenewal,
  formatDateOnly,
  formatSupplier,
  getContractTypeLabel,
  getPortfolioRiskTag,
  getPortfolioStatusTag,
} from "./portfolioTableFormatters";

export interface PortfolioTableProps {
  rows: readonly AttentionRow[];
}

/**
 * Column-one text colour by severity tier, quoted verbatim from day1-demo.html's own
 * `issueFg: sev===3 ? 'var(--color-accent-700)' : sev===2 ? 'var(--color-text)' :
 * 'var(--color-neutral-500)'` -- ADR-019's "urgency in column one, not colour-only": the *label*
 * ("Cancellation notice due in 12 d", "Needs review", ...) is what actually carries the meaning; this
 * colour only reinforces it, it never stands alone.
 */
function issueColor(severity: AttentionRow["severity"]): string {
  if (severity === 3) return "var(--color-accent-700)";
  if (severity === 2) return "var(--color-text)";
  return "var(--color-neutral-500)";
}

/**
 * AC-3 "Table sorted by severity -> deadline; critical rows tinted + red bar." `rows` is expected
 * already sorted (`portfolioAttention.ts#compareBySeverityThenDeadline`) and already filtered
 * (`index.tsx`) -- this component only renders. Columns are quoted verbatim from screens.md #4:
 * "Attention · Supplier · Contract · Annual spend · Start · End · Renewal · Cancel by · Auto ·
 * Status" -- ten columns, no separate Risk column (risk surfaces as attention-column text/tag
 * instead, the same way the compiled prototype's own row view-model folds it in rather than giving it
 * a column of its own).
 */
export default function PortfolioTable({ rows }: PortfolioTableProps) {
  return (
    // screens.md #4: "Table (fixed widths, min 1000px, scrolls)" -- the wrapper, not the <table>
    // itself, owns the horizontal scrollbar so .table's own border/hover-tint rules stay unchanged.
    <div className="portfolio-table-wrapper">
      <table className="table portfolio-table">
        <thead>
          <tr>
            <th scope="col">Attention</th>
            <th scope="col">Supplier</th>
            <th scope="col">Contract</th>
            <th scope="col" className="portfolio-table-numeric">
              Annual spend
            </th>
            <th scope="col">Start</th>
            <th scope="col">End</th>
            <th scope="col">Renewal</th>
            <th scope="col">Cancel by</th>
            <th scope="col">Auto</th>
            <th scope="col">Status</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const { item } = row;
            const statusTag = getPortfolioStatusTag(item.status);
            const riskTag = getPortfolioRiskTag(item.risk);
            const supplier = formatSupplier(item.supplierId);
            // AC-3 "critical rows tinted + red bar" is exactly severity 3 (the shared .row-critical
            // class, ADR-019 component catalogue); severity 2 gets a lighter, screen-scoped
            // acknowledgement (.row-attention, contracts.css) rather than the full accent treatment --
            // both quoted verbatim from day1-demo.html's own `rowBg`/`bar` fields (sev===3 -> accent
            // tint + accent bar; sev===2 -> no tint, a neutral bar only).
            const rowClassName = row.severity === 3 ? "row-critical" : row.severity === 2 ? "row-attention" : undefined;

            return (
              <tr key={item.contractId} className={rowClassName}>
                <td>
                  <div className="portfolio-attention-cell">
                    <span style={{ color: issueColor(row.severity), fontWeight: row.severity >= 2 ? 600 : 400 }}>
                      {row.issue}
                    </span>
                    {row.isHighRisk && <span className={`tag tag-${riskTag.variant}`}>{riskTag.label}</span>}
                  </div>
                </td>
                <td title={supplier.title}>{supplier.label}</td>
                <td>
                  <Link to={`/contracts/${item.contractId}`}>{getContractTypeLabel(item.type)}</Link>
                </td>
                <td className="portfolio-table-numeric">{formatAnnualSpend(item.annualSpend)}</td>
                <td>{formatDateOnly(item.startDate)}</td>
                <td>{formatDateOnly(item.endDate)}</td>
                <td>{formatDateOnly(item.renewalDate)}</td>
                <td className={row.isDeadlineSoon ? "deadline-critical" : undefined}>
                  {formatDateOnly(item.cancellationDeadline)}
                  {row.cancelDays !== null && <div className="micro-meta">{row.cancelDays} d</div>}
                </td>
                <td>{formatAutoRenewal(item.autoRenewal)}</td>
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
