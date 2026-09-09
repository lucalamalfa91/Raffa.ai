import { useEffect, useRef } from "react";
import type { Contract360ClauseBody } from "../../../api/client";

export interface ClauseHighlightProps {
  clause: Contract360ClauseBody;
}

/**
 * Citation landing's "original wording" block (route `/contracts/:contractId?clause=`/`?page=`,
 * task E13/F10/US01/T01; ADR-024 "citation landing, scoped conversations"; ADR-020 screen 5
 * amendment "citation landing with highlighted clause"; parent story us-01-contract360-landing
 * AC-1). Mirrors `inputs/design/prototypes/contigo-v2/markup.html`'s own screen-5 `hasHl` evidence
 * card (a clause row's `c.show` click reveals `{{ hl.before }}` **`{{ hl.quote }}`** `{{ hl.after }}`)
 * -- adapted to this task's real schema and its own "without waiting for a click" entry point (a
 * `?clause=`/`?page=` landing, not a row click): the backend `Clause` domain entity carries one
 * `RawText` field, not the prototype's separate mock before/quote/after strings, so this renders the
 * whole `rawText` as the original wording and emphasises `sourceSpan` (the page/section evidence
 * pointer, e.g. "§17.2") next to it instead of `<mark>`-highlighting a substring the schema does not
 * carry -- an honest adaptation, not a re-derivation of the prototype's mock shape, the same kind of
 * documented gap `Contract360Header.tsx`'s own header comment already names for `supplierId`/
 * contract-title.
 *
 * Rendered unconditionally by `index.tsx` whenever the landing resolves a real highlighted clause
 * (`contract360ViewModel.ts#resolveHighlightedClauseId`) -- "without waiting for a click" (task
 * text) -- immediately below the Clauses tab's `FactTable`, left otherwise unmodified (that file is
 * out of this task's own "Files to create or modify"). This one card is the whole of "highlight it
 * (--color-accent-100 fill + accent left bar) and render its original wording" (task text): the
 * `.contract360-clause-highlight` background/border-left below *is* the highlight, not a
 * complement to a separate row-level treatment.
 *
 * Scrolls itself into view on mount/clause change (task text: "scroll to it") -- `scrollIntoView` is
 * invoked through an optional-chained method access rather than assumed to exist: every real browser
 * implements it, but nothing in this app has needed it before this task, so this does not lean on a
 * particular jsdom/browser guarantee either.
 */
export default function ClauseHighlight({ clause }: ClauseHighlightProps) {
  const highlightRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    highlightRef.current?.scrollIntoView?.({ behavior: "smooth", block: "center" });
  }, [clause.clauseId]);

  const hasSourceSpan = clause.sourceSpan !== null && clause.sourceSpan.trim() !== "";

  return (
    <div ref={highlightRef} className="contract360-clause-highlight" data-testid="clause-highlight">
      <p className="contract360-clause-highlight-meta micro-meta">
        {clause.clauseType}
        {clause.sourcePage !== null && ` · p.${clause.sourcePage}`}
        {hasSourceSpan && (
          <>
            {" "}
            · <strong className="contract360-clause-span">{clause.sourceSpan}</strong>
          </>
        )}
      </p>
      <blockquote className="contract360-clause-quote">{clause.rawText}</blockquote>
    </div>
  );
}
