import type { DocumentCountsBody } from "../../components/shell/useDocumentCounts";
import { getFilterHint, type AttentionFilterValue } from "./documentTable";

export interface AttentionFilterProps {
  value: AttentionFilterValue;
  onChange: (value: AttentionFilterValue) => void;
  /** The server's tenant-wide `counts` (ADR-027 §D7): each chip reads exactly one member, and none
   * is computed from another (§C9.1). */
  counts: DocumentCountsBody;
}

/**
 * The "Needs your attention · N" / "All documents · N" segmented toggle (`raffa-v2/markup.html`
 * lines ~152-156; `raffa-v2/app.jsx`'s `filterAttn`/`filterAll`/`filterHint`), plus -- task
 * E16/F03/US01/T01 (ADR-020 w15 §1.5, ADR-019 w15 clause 3a) -- a third chip, "Not added · K",
 * rendered only while `counts.rejected > 0`: a third native `<button aria-pressed>` inside the same
 * `.seg`, a catalogue use rather than an extension. Default filter is "attention" (R-DOC-06) --
 * `index.tsx` owns the initial value (including reading `?filter=` from the `/review` redirect
 * `WorkspaceShellApp.tsx` already wires); this component is a controlled set of toggles. The
 * numbers are the server's, never the fetched page's (ADR-012 w15 §4: fifteen just-dropped rows
 * never sit under a chip reading 0).
 */
export default function AttentionFilter({ value, onChange, counts }: AttentionFilterProps) {
  return (
    <div className="documents-filter-row">
      <div className="seg" role="group" aria-label="Filter documents">
        <button type="button" aria-pressed={value === "attention"} onClick={() => onChange("attention")}>
          Needs your attention · {counts.needsAttention}
        </button>
        <button type="button" aria-pressed={value === "all"} onClick={() => onChange("all")}>
          All documents · {counts.all}
        </button>
        {counts.rejected > 0 && (
          <button type="button" aria-pressed={value === "rejected"} onClick={() => onChange("rejected")}>
            Not added · {counts.rejected}
          </button>
        )}
      </div>
      <span className="micro-meta">{getFilterHint(value)}</span>
    </div>
  );
}
