import type { AttentionBucketCount, AttentionBucketKey } from "./portfolioAttention";

export interface AttentionStripProps {
  buckets: readonly AttentionBucketCount[];
  activeKey: AttentionBucketKey | null;
  onToggle: (key: AttentionBucketKey) => void;
}

/**
 * AC-2 "Attention strip (Deadlines <45d, Need review, Failed/processing, High risk) click = filter."
 * Uses the shared `.attention-strip`/`.attention-cell` classes (`src/styles/components.css`, ADR-019
 * component catalogue) for the equal-cell grid and the border-right divider, plus this screen's own
 * additive rules (`contracts.css`) for the parts that shared sheet does not give any strip yet -- a
 * native `<button>` per cell (ADR-019 accessibility baseline: every interactive control is native),
 * `aria-pressed` for the currently-active bucket, and the big number paired with its own text label so
 * urgency is never colour-only. Clicking the already-active cell clears it -- the same toggle-off UX
 * day1-demo.html's own `clearFilter` gives the chips row, applied here to the strip too.
 *
 * The three visible pieces of text (number, label, meta) and the two independent bits of per-bucket
 * colour (the left bar + number colour by urgency; the background by selection) are quoted verbatim
 * from day1-demo.html's own compiled markup and its `attDef.map` view model (task E11/F05/US01/T01,
 * gap G-PORT) -- not invented:
 *   - `meta` (e.g. "Cancellation notice due soon") is a *visible* third line under the label in the
 *     export, not a hover-only tooltip -- this component used to drop it into a `title` attribute;
 *     it now renders it, so no information is only available on hover.
 *   - Urgency is **not** a uniform "any non-zero count" rule: the export's own `attDef.map` gives
 *     three buckets (deadline/review/failed) the accent/accent-700 treatment once they have a match,
 *     but "High risk" alone stays ink-coloured even with matches -- it is already surfaced by each
 *     row's own risk tag/colour, so the strip does not double it. `isRiskFlagged` exists so that one
 *     bucket gets its own (neutral, not accent) "has a match" treatment instead of `is-urgent`'s.
 */
export default function AttentionStrip({ buckets, activeKey, onToggle }: AttentionStripProps) {
  return (
    <div className="attention-strip" role="group" aria-label="Attention">
      {buckets.map((bucket) => {
        const isActive = bucket.key === activeKey;
        const isUrgent = bucket.count > 0 && bucket.key !== "risk";
        const isRiskFlagged = bucket.count > 0 && bucket.key === "risk";
        return (
          <button
            key={bucket.key}
            type="button"
            className={`attention-cell${isUrgent ? " is-urgent" : ""}${isRiskFlagged ? " is-risk-flagged" : ""}${isActive ? " is-active" : ""}`}
            aria-pressed={isActive}
            onClick={() => onToggle(bucket.key)}
          >
            <div className="attention-cell-headline">
              <span className="attention-number">{bucket.count}</span>
              <span className="attention-label">{bucket.label}</span>
            </div>
            <span className="attention-meta">{bucket.meta}</span>
          </button>
        );
      })}
    </div>
  );
}
