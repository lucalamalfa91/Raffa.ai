import { blockedReason, isValidationBlocked, type ReviewProgress } from "./reviewViewModel";

export interface ReviewHeaderProps {
  progress: ReviewProgress;
  onMarkValidated: () => void;
}

/**
 * Review screen header (screens.md #6: "Header + 'Mark as validated' (disabled until all < 80%
 * fields decided). Progress line + legend."; task E07/F03/US01/T01 AC-4). The CTA's `disabled`
 * state is paired with a visible `.hint` reason underneath it whenever blocked (ADR-019
 * accessibility baseline: "a visible reason, not a hidden control") -- never only a `title`
 * tooltip, which is not reliably visible or accessible.
 */
export default function ReviewHeader({ progress, onMarkValidated }: ReviewHeaderProps) {
  const blocked = isValidationBlocked(progress);

  return (
    <header className="review-header">
      <div className="review-header-top">
        <div>
          <p className="screen-kicker">R1</p>
          <h2 className="screen-title">Review &amp; correction</h2>
        </div>
        <div className="review-header-cta">
          <button type="button" className="btn btn-primary" disabled={blocked} onClick={onMarkValidated}>
            Mark as validated
          </button>
          {blocked && <span className="hint">{blockedReason(progress)}</span>}
        </div>
      </div>
      <p className="micro-meta review-progress-line">
        {progress.resolvedCount} of {progress.total} field{progress.total === 1 ? "" : "s"} resolved
        {progress.blockingCount > 0
          ? ` · ${progress.blockingCount} ${progress.blockingCount === 1 ? "needs" : "need"} review`
          : ""}
      </p>
    </header>
  );
}
