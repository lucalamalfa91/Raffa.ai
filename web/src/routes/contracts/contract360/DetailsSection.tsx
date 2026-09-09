import { Link } from "react-router-dom";
import type { Contract360Body, RenewalPriorityBody } from "../../../api/client";
import FactTable from "./FactTable";
import {
  DETAILS_LABEL_CLOSED,
  DETAILS_LABEL_OPEN,
  NO_ATTENTION_MESSAGE,
  buildDocumentRows,
  buildKeyTerms,
  buildObligationsRows,
  buildPriorityComponentRows,
  buildProductsRows,
  buildRisksRows,
  computeNeedsAttention,
  formatPriorityFact,
} from "./contract360ViewModel";

export interface DetailsSectionProps {
  contract: Contract360Body;
  priority: RenewalPriorityBody | null;
  open: boolean;
  onToggle: () => void;
}

/**
 * "Details ▾" (`contigo-v2/markup.html` "CONTRACT 360" block, bottom; screens-v2.md #5 "Details ▾
 * ('All terms, documents and open facts ▾'): key terms table, documents in family, facts still to
 * decide"). Closed by default; open, it holds what the Day-1 tabs used to hold: key terms, the
 * documents, the facts still to decide (with "Review all →"), the explainable priority score, and
 * the extracted Products / Obligations / Risks lists when the contract has any. Every row is a
 * deterministic fact -- the recommendation never appears here (ADR-019 facts vs AI).
 */
export default function DetailsSection({ contract, priority, open, onToggle }: DetailsSectionProps) {
  const { tabs } = contract;
  const keyTerms = buildKeyTerms(contract);
  const documents = buildDocumentRows(tabs.documents);
  const attention = computeNeedsAttention(tabs);
  const priorityRows = buildPriorityComponentRows(priority);
  const products = buildProductsRows(tabs.products);
  const obligations = buildObligationsRows(tabs.obligations);
  const risks = buildRisksRows(tabs.risks);

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
                  <div>
                    <span className="contract360-detail-label">{row.term}</span> <strong>{row.value}</strong>
                  </div>
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
                    <div>
                      <span className="contract360-detail-label">{row.type}</span> {row.fileName}
                    </div>
                    <span className={`tag tag-${row.status.variant} contract360-detail-tag`}>{row.status.label}</span>
                  </div>
                ))
              )}

              <div className="contract360-detail-heading-row">
                <h6>Facts you still need to decide</h6>
                <Link to={`/contracts/${contract.contractId}/review`} className="btn btn-ghost contract360-detail-link">
                  Review all →
                </Link>
              </div>
              {attention.length === 0 ? (
                <p className="contract360-detail-empty">{NO_ATTENTION_MESSAGE}</p>
              ) : (
                attention.map((term) => (
                  <div key={term.key} className="contract360-detail-row">
                    <div>
                      <span className="contract360-detail-label">{term.term}</span> <strong>{term.value}</strong>
                    </div>
                    <span className={`tag tag-${term.tag.variant} contract360-detail-tag`}>{term.tag.label}</span>
                  </div>
                ))
              )}
            </div>

            <div>
              <div className="contract360-detail-heading-row">
                <h6>Priority score</h6>
                <span className="contract360-detail-side">{formatPriorityFact(priority)}</span>
              </div>
              {priorityRows.length === 0 ? (
                <p className="contract360-detail-empty">The priority score has not been computed for this contract yet.</p>
              ) : (
                priorityRows.map((row) => (
                  <div key={row.key} className="contract360-detail-row" title={row.explanation}>
                    <div>
                      <span className="contract360-detail-label">{row.label}</span> <strong>{Math.round(row.score)}</strong>
                    </div>
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
