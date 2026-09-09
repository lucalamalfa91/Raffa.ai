import { getFilterHint, type AttentionFilterValue } from "./documentTable";

export interface AttentionFilterProps {
  value: AttentionFilterValue;
  onChange: (value: AttentionFilterValue) => void;
  attentionCount: number;
  allCount: number;
}

/**
 * The "Needs your attention · N" / "All documents · N" segmented toggle (`contigo-v2/markup.html`
 * lines ~152-156; `contigo-v2/app.jsx`'s `filterAttn`/`filterAll`/`filterHint`). Default filter is
 * "attention" (R-DOC-06) -- `index.tsx` owns the initial value (including reading `?filter=` from
 * the `/review` redirect `WorkspaceShellApp.tsx` already wires), this component is a controlled
 * pair of native `<button aria-pressed>` toggles (ADR-019 `.seg` catalogue entry: "native
 * `<button aria-pressed>` children").
 */
export default function AttentionFilter({ value, onChange, attentionCount, allCount }: AttentionFilterProps) {
  return (
    <div className="documents-filter-row">
      <div className="seg" role="group" aria-label="Filter documents">
        <button type="button" aria-pressed={value === "attention"} onClick={() => onChange("attention")}>
          Needs your attention · {attentionCount}
        </button>
        <button type="button" aria-pressed={value === "all"} onClick={() => onChange("all")}>
          All documents · {allCount}
        </button>
      </div>
      <span className="micro-meta">{getFilterHint(value)}</span>
    </div>
  );
}
