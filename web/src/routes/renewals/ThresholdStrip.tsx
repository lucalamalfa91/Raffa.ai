import type { RenewalWindowCount } from "./renewalPipelineViewModel";

export interface ThresholdStripProps {
  buckets: readonly RenewalWindowCount[];
  activeKey: string | null;
  onToggle: (key: string) => void;
}

/**
 * AC-1 "Threshold strip 0-30 ... 270-365d with counts (click = filter)" (screens.md #8). Reuses the
 * exact same `.attention-strip`/`.attention-cell` classes `../contracts/AttentionStrip.tsx` already
 * established (`src/styles/components.css`) -- ADR-019's own component catalogue names
 * "attention/threshold strip" as one shared pattern, not two forked styles. Same accessibility
 * shape as that component: a native `<button>` per cell, `aria-pressed` for the active bucket, and
 * the big number always paired with its own text label (ADR-019 "not colour-only"). Clicking the
 * already-active cell clears it, the same toggle-off UX `AttentionStrip.tsx` gives Portfolio's strip.
 *
 * `.is-urgent` (day1-demo.html's own accent-700 number colour) is applied only when this bucket has
 * a non-zero count *and* its own upper bound is <=120 days -- quoted verbatim from that prototype's
 * own `fg:n?(w<=120?'var(--color-accent-700)':'inherit'):'var(--color-neutral-400)'` rule for its
 * window strip, not `AttentionStrip.tsx`'s own simpler "any non-zero count is urgent" rule (every
 * one of Portfolio's four buckets is inherently bad news; a renewal due in 270-365 days is not).
 * Selected-cell background/bar feedback (`.is-active`) is a small additive rule in
 * `renewals.css`, scoped to this screen only -- `components.css` sets `aria-pressed` as the
 * accessible state but does not yet style `.is-active` for any strip.
 */
export default function ThresholdStrip({ buckets, activeKey, onToggle }: ThresholdStripProps) {
  return (
    <div className="attention-strip" role="group" aria-label="Renewal window">
      {buckets.map((bucket) => {
        const isActive = bucket.key === activeKey;
        const isUrgent = bucket.count > 0 && bucket.highInclusiveDays <= 120;
        return (
          <button
            key={bucket.key}
            type="button"
            className={`attention-cell${isUrgent ? " is-urgent" : ""}${isActive ? " is-active" : ""}`}
            aria-pressed={isActive}
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
