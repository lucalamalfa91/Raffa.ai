import { useEffect, useRef } from "react";
import { Link } from "react-router-dom";
import type { Contract360ClauseBody, Contract360DocumentBody } from "../../../api/client";
import { buildClauseEvidence, clauseViewerHref } from "./contract360ViewModel";

export interface ClauseHighlightProps {
  clause: Contract360ClauseBody;
  documents: readonly Contract360DocumentBody[];
}

/**
 * The evidence card under "Why — the clauses behind it" (`raffa-v2/markup.html` `hasHl`: a
 * paper-white, serif card headed with the short capped `p.N · §` reference and the cited wording
 * `<mark>`ed inside its context). Shown for the selected clause -- a row click, or the citation
 * landing (`?clause=`/`?page=`, R-EVD-02). The original quote renders here and nowhere else on
 * the Why row. "Open in document viewer" uses the same route the row does, omitted when the
 * clause has no resolvable document or page.
 */
export default function ClauseHighlight({ clause, documents }: ClauseHighlightProps) {
  const highlightRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    highlightRef.current?.scrollIntoView?.({ behavior: "smooth", block: "center" });
  }, [clause.clauseId]);

  const evidence = buildClauseEvidence(clause, documents);
  const viewerHref = clauseViewerHref(clause, documents);

  return (
    <div ref={highlightRef} className="contract360-evidence" data-testid="clause-highlight">
      <p className="contract360-evidence-citation">{evidence.citation}</p>
      <p className="contract360-evidence-text">
        {evidence.before}
        <mark className="contract360-evidence-mark">{evidence.quote}</mark>
        {evidence.after}
      </p>
      {viewerHref !== null && (
        <p className="contract360-evidence-viewer">
          <Link to={viewerHref} className="btn btn-ghost">
            Open in document viewer
          </Link>
        </p>
      )}
    </div>
  );
}
