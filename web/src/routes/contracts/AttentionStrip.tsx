import type { AttentionBucketCount, AttentionBucketKey } from "./portfolioAttention";

export interface AttentionStripProps {
  buckets: readonly AttentionBucketCount[];
  activeKey: AttentionBucketKey | null;
  onToggle: (key: AttentionBucketKey) => void;
}

/**
 * AC-2 "Attention strip (Deadlines <45d, Need review, Failed/processing, High risk) click = filter."
 * Uses the shared `.attention-strip`/`.attention-cell` classes verbatim (`src/styles/components.css`,
 * ADR-019 component catalogue) -- a native `<button>` per cell (ADR-019 accessibility baseline: every
 * interactive control is native), `aria-pressed` for the currently-active bucket, and the big number
 * paired with its own text label so urgency is never colour-only (`.is-urgent` only adds colour on top
 * of a label that already says "High risk"/"Deadlines < 45 d", it never stands alone). Clicking the
 * already-active cell clears it -- the same toggle-off UX day1-demo.html's own `clearFilter` gives the
 * chips row, applied here to the strip too.
 */
export default function AttentionStrip({ buckets, activeKey, onToggle }: AttentionStripProps) {
  return (
    <div className="attention-strip" role="group" aria-label="Attention">
      {buckets.map((bucket) => {
        const isActive = bucket.key === activeKey;
        return (
          <button
            key={bucket.key}
            type="button"
            className={`attention-cell${bucket.count > 0 ? " is-urgent" : ""}${isActive ? " is-active" : ""}`}
            aria-pressed={isActive}
            title={bucket.meta}
            onClick={() => onToggle(bucket.key)}
          >
            <span className="attention-number">{bucket.count}</span>
            <span className="attention-label">{bucket.label}</span>
          </button>
        );
      })}
    </div>
  );
}
