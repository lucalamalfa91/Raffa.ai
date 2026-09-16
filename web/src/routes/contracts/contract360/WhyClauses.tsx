import { Link } from "react-router-dom";
import type { Contract360ClauseBody, Contract360DocumentBody } from "../../../api/client";
import ClauseHighlight from "./ClauseHighlight";
import { AUTO_ACCEPT_THRESHOLD, LEVERAGE_LEGEND, buildClauseRows } from "./contract360ViewModel";

export interface WhyClausesProps {
  contractId: string;
  clauses: readonly Contract360ClauseBody[];
  documents: readonly Contract360DocumentBody[];
  autoAcceptThreshold: number;
  selectedClauseId: string | null;
  onSelect: (clauseId: string) => void;
}

/**
 * "Why — the clauses behind it" (`raffa-v2/markup.html` "CONTRACT 360" block, middle). Each clause
 * is a row — type · accepted value · leverage tag · one-line why · Open in document viewer — and
 * the selected one opens the original wording in `ClauseHighlight`. No source cell and no
 * confidence tag on the row (ADR-020 w17 §14(e); ADR-019 w17 clause 11).
 */
export default function WhyClauses({
  contractId,
  clauses,
  documents,
  autoAcceptThreshold,
  selectedClauseId,
  onSelect,
}: WhyClausesProps) {
  const rows = buildClauseRows(clauses, documents, autoAcceptThreshold > 0 ? autoAcceptThreshold : AUTO_ACCEPT_THRESHOLD);
  const selected = clauses.find((c) => c.clauseId === selectedClauseId) ?? null;

  return (
    <section className="contract360-why" aria-label="Why — the clauses behind it">
      <div className="contract360-why-head">
        <h6>Why — the clauses behind it</h6>
        {rows.length > 0 && <span className="contract360-why-hint">{LEVERAGE_LEGEND}</span>}
      </div>

      {rows.length === 0 ? (
        <p className="contract360-why-empty">
          No clauses extracted for this contract yet.{" "}
          <Link to={`/contracts/${contractId}/review`}>Review the extraction</Link> to see the wording behind these answers.
        </p>
      ) : (
        <div className="contract360-clauses" role="list">
          {rows.map((row) => {
            const isSelected = row.clauseId === selectedClauseId;
            return (
              <div
                key={row.clauseId}
                role="listitem"
                className={`contract360-clause${isSelected ? " is-selected" : ""}`}
              >
                <button
                  type="button"
                  className="contract360-clause-select"
                  aria-pressed={isSelected}
                  onClick={() => onSelect(row.clauseId)}
                >
                  <span className="contract360-clause-type">{row.type}</span>
                  <span className="contract360-clause-normalized">{row.normalized}</span>
                </button>
                <span className="contract360-clause-meta">
                  {row.risk !== null && <span className={`tag tag-${row.risk.variant} contract360-clause-tag`}>{row.risk.label}</span>}
                  {row.why !== null && <span className="contract360-clause-why">{row.why}</span>}
                  {row.viewerHref !== null && (
                    <Link to={row.viewerHref} className="btn btn-ghost contract360-clause-viewer">
                      Open in document viewer
                    </Link>
                  )}
                </span>
              </div>
            );
          })}
        </div>
      )}

      {selected !== null && <ClauseHighlight clause={selected} documents={documents} />}
    </section>
  );
}
