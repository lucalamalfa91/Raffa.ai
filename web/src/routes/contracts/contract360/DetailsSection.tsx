import { Link } from "react-router-dom";
import type { Contract360Body, ContractFieldEvidenceBody, RenewalPriorityBody } from "../../../api/client";
import FactTable from "./FactTable";
import {
  AUTO_ACCEPT_THRESHOLD,
  DETAILS_LABEL_CLOSED,
  DETAILS_LABEL_OPEN,
  buildDocumentRows,
  buildKeyTerms,
  buildObligationsRows,
  buildPriorityComponentRows,
  buildProductsRows,
  buildRisksRows,
  computeNeedsAttention,
  formatPriorityFact,
  formatReviewCountLine,
} from "./contract360ViewModel";

export interface DetailsSectionProps {
  contract: Contract360Body;
  priority: RenewalPriorityBody | null;
  evidence: readonly ContractFieldEvidenceBody[];
  autoAcceptThreshold: number;
  open: boolean;
  onToggle: () => void;
}

/**
 * "Details ▾" (`raffa-v2/markup.html` "CONTRACT 360" block, bottom; screens-v2.md #5). Closed by
 * default; open, it holds key terms, the documents, a trailing count line to Review when facts
 * still need a decision, the explainable priority score, and the extracted Products / Obligations
 * / Risks lists when the contract has any. Every row stays; unofficialized values are the em-dash.
 * No "facts still to decide" list and no confidence tag (ADR-020 w17 §14, ADR-019 w17 clause 11).
 */
export default function DetailsSection({
  contract,
  priority,
  evidence,
  autoAcceptThreshold,
  open,
  onToggle,
}: DetailsSectionProps) {
  const { tabs } = contract;
  const threshold = autoAcceptThreshold > 0 ? autoAcceptThreshold : AUTO_ACCEPT_THRESHOLD;
  const keyTerms = buildKeyTerms(contract, evidence);
  const documents = buildDocumentRows(tabs.documents);
  const reviewCount = computeNeedsAttention(evidence);
  const priorityRows = buildPriorityComponentRows(priority);
  const products = buildProductsRows(tabs.products, threshold);
  const obligations = buildObligationsRows(tabs.obligations, threshold);
  const risks = buildRisksRows(tabs.risks, threshold);

  return (
    <section className="contract360-details" aria-label="Details">
      <button type="button" className="btn btn-ghost contract360-details-toggle" aria-expanded={open} onClick={onToggle}>
        {open ? DETAILS_LABEL_OPEN : DETAILS_LABEL_CLOSED}
      </button>

      {open && (
        <>
          <div className="contract360-details-grid">
            <div>
              <h6>Key terms</h6>
              {keyTerms.map((row) => (
                <div key={row.key} className="contract360-detail-row">
                  <span className="contract360-detail-label">{row.term}</span>
                  <strong className="contract360-detail-value">{row.value}</strong>
                  {row.source !== null && <span className="contract360-detail-side">{row.source}</span>}
                </div>
              ))}
            </div>

            <div>
              <h6>Documents</h6>
              {documents.length === 0 ? (
                <p className="contract360-detail-empty">No documents linked to this contract.</p>
              ) : (
                documents.map((row) => (
                  <div key={row.documentId} className="contract360-detail-row">
                    <span className="contract360-detail-label">{row.type}</span>
                    <span className="contract360-detail-value">{row.fileName}</span>
                    <span className={`tag tag-${row.status.variant} contract360-detail-tag`}>{row.status.label}</span>
                  </div>
                ))
              )}
              {reviewCount > 0 && (
                <Link to={`/contracts/${contract.contractId}/review`} className="btn btn-ghost contract360-detail-link">
                  {formatReviewCountLine(reviewCount)}
                </Link>
              )}
            </div>

            <div className="contract360-details-span">
              <div className="contract360-detail-heading-row">
                <h6>Priority score</h6>
                <span className="contract360-detail-side">{formatPriorityFact(priority)}</span>
              </div>
              {priorityRows.length === 0 ? (
                <p className="contract360-detail-empty">The priority score has not been computed for this contract yet.</p>
              ) : (
                priorityRows.map((row) => (
                  <div key={row.key} className="contract360-detail-row" title={row.explanation}>
                    <span className="contract360-detail-label">{row.label}</span>
                    <strong className="contract360-detail-value">{Math.round(row.score)}</strong>
                    <span className="contract360-detail-side contract360-detail-explanation">{row.explanation}</span>
                  </div>
                ))
              )}
            </div>
          </div>

          {products.length > 0 && <FactTable title="Products" rows={products} emptyMessage="No line items recorded for this contract." />}
          {obligations.length > 0 && <FactTable title="Obligations" rows={obligations} emptyMessage="No obligations extracted for this contract." />}
          {risks.length > 0 && <FactTable title="Risks" rows={risks} emptyMessage="No risks recorded for this contract." />}
        </>
      )}
    </section>
  );
}
