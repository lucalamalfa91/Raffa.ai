import {
  getReadinessFilterHint,
  type ReadinessCounts,
  type ReadinessFilterValue,
} from "./readiness";

export interface ReadinessFilterProps {
  value: ReadinessFilterValue;
  onChange: (value: ReadinessFilterValue) => void;
  counts: ReadinessCounts;
  /** Accessible name for the segmented group — screen-specific ("Filter portfolio", "Filter renewals"). */
  ariaLabel: string;
}

/**
 * Compact `.seg` toggle: Ready · N / To review · N / All · N. Catalogue use of the existing
 * segmented control (Documents' attention chips, ADR-019), not a new product surface. Numbers are
 * the already-loaded page's own buckets, never a second fetch.
 */
export default function ReadinessFilter({ value, onChange, counts, ariaLabel }: ReadinessFilterProps) {
  return (
    <div className="readiness-filter-row">
      <div className="seg" role="group" aria-label={ariaLabel}>
        <button type="button" aria-pressed={value === "ok"} onClick={() => onChange("ok")}>
          Ready · {counts.ok}
        </button>
        <button type="button" aria-pressed={value === "review"} onClick={() => onChange("review")}>
          To review · {counts.review}
        </button>
        <button type="button" aria-pressed={value === "all"} onClick={() => onChange("all")}>
          All · {counts.all}
        </button>
      </div>
      <span className="micro-meta">{getReadinessFilterHint(value)}</span>
    </div>
  );
}
