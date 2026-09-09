import { useEffect, useRef } from "react";
import type { Contract360ClauseBody, Contract360DocumentBody } from "../../../api/client";
import { buildClauseEvidence } from "./contract360ViewModel";

export interface ClauseHighlightProps {
  clause: Contract360ClauseBody;
  documents: readonly Contract360DocumentBody[];
}

/**
 * The evidence card under "Why — the clauses behind it" (`contigo-v2/markup.html` `hasHl`: a
 * paper-white, serif card headed "{{ hl.doc }} · page {{ hl.page }} · §{{ hl.sec }}" with the cited
 * wording `<mark>`ed inside its context). Shown for the selected clause -- a row click, or the
 * citation landing (`?clause=`/`?page=`, R-EVD-02) which selects it without a click and scrolls
 * here. The backend `Clause` carries one `rawText`; `buildClauseEvidence` marks the normalised value
 * inside it when it is a literal substring, else the whole original wording -- never a synthesised
 * excerpt.
 */
export default function ClauseHighlight({ clause, documents }: ClauseHighlightProps) {
  const highlightRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    highlightRef.current?.scrollIntoView?.({ behavior: "smooth", block: "center" });
  }, [clause.clauseId]);

  const evidence = buildClauseEvidence(clause, documents);

  return (
    <div ref={highlightRef} className="contract360-evidence" data-testid="clause-highlight">
      <p className="contract360-evidence-citation">{evidence.citation}</p>
      <p className="contract360-evidence-text">
        {evidence.before}
        <mark className="contract360-evidence-mark">{evidence.quote}</mark>
        {evidence.after}
      </p>
    </div>
  );
}
