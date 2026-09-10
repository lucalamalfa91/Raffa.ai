import { Link } from "react-router-dom";
import type { Contract360ClauseBody, Contract360DocumentBody } from "../../../api/client";
import { getConfidenceTag } from "../../../styles/semantics";
import ClauseHighlight from "./ClauseHighlight";
import { buildClauseRows } from "./contract360ViewModel";

export interface WhyClausesProps {
  contractId: string;
  clauses: readonly Contract360ClauseBody[];
  documents: readonly Contract360DocumentBody[];
  selectedClauseId: string | null;
  onSelect: (clauseId: string) => void;
}

/**
 * "Why — the clauses behind it" (`contigo-v2/markup.html` "CONTRACT 360" block, middle; screens-v2.md
 * #5 "2–3 clauses (type · normalized · page § · risk · confidence), click → original wording
 * highlighted"). Each clause is a native `<button>` row -- type · normalised value · "p.N · §span ·
 * risk tag · confidence" -- and the selected one opens the evidence card below the list.
 */
export default function WhyClauses({ contractId, clauses, documents, selectedClauseId, onSelect }: WhyClausesProps) {
  const rows = buildClauseRows(clauses);
  const selected = clauses.find((c) => c.clauseId === selectedClauseId) ?? null;

  return (
    <section className="contract360-why" aria-label="Why — the clauses behind it">
      <div className="contract360-why-head">
        <h6>Why — the clauses behind it</h6>
        {rows.length > 0 && <span className="contract360-why-hint">Click a clause to read the original wording</span>}
      </div>

      {rows.length === 0 ? (
        <p className="contract360-why-empty">
          No clauses extracted for this contract yet.{" "}
          <Link to={`/contracts/${contractId}/review`}>Review the extraction</Link> to see the wording behind these answers.
        </p>
      ) : (
        <div className="contract360-clauses" role="list">
          {rows.map((row) => {
            const confidence = row.confidencePct === null ? null : getConfidenceTag(row.confidencePct);
            const isSelected = row.clauseId === selectedClauseId;
            return (
              <button
                key={row.clauseId}
                type="button"
                role="listitem"
                className={`contract360-clause${isSelected ? " is-selected" : ""}`}
                aria-pressed={isSelected}
                onClick={() => onSelect(row.clauseId)}
              >
                <span className="contract360-clause-type">{row.type}</span>
                <span className="contract360-clause-normalized">{row.normalized}</span>
                <span className="contract360-clause-meta">
                  {row.source ?? "No source recorded"}
                  {row.risk !== null && (
                    <>
                      {" · "}
                      <span className={`tag tag-${row.risk.variant} contract360-clause-tag`}>{row.risk.label}</span>
                    </>
                  )}
                  {confidence !== null && (
                    <>
                      {" · "}
                      <span className={`tag tag-${confidence.variant} contract360-clause-tag`}>{confidence.label}</span>
                    </>
                  )}
                </span>
              </button>
            );
          })}
        </div>
      )}

      {selected !== null && <ClauseHighlight clause={selected} documents={documents} />}
    </section>
  );
}
