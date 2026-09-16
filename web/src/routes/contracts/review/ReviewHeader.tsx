import type { Contract360HeaderBody } from "../../../api/client";
import { formatSupplier, getContractTypeLabel } from "../portfolioTableFormatters";
import {
  blockedReason,
  isValidationBlocked,
  legendThresholdPct,
  reviewTitle,
  type ReviewProgress,
} from "./reviewViewModel";

export interface ReviewHeaderProps {
  header: Contract360HeaderBody;
  progress: ReviewProgress;
  onMarkValidated: () => void;
  /** True while `POST /api/documents/{id}/validate` is in flight -- the CTA is disabled and says so. */
  validating?: boolean;
  /** The last sign-off failure (a 409 "still processing", a network error), shown under the CTA. */
  validationError?: string | null;
  /** True when the reviewed document is already `Completed`: the CTA reads "Validated" and stays off. */
  alreadyValidated?: boolean;
  /** False when the contract has no document to validate at all (nothing for the CTA to write). */
  canValidate?: boolean;
  /** The bar the server used (`GET /api/contracts/{id}/evidence`.autoAcceptThreshold). Never hardcoded. */
  autoAcceptThreshold?: number | null;
}

/**
 * Review screen header (screens.md #6; ADR-020 w17 §13). Title names the count of facts that
 * still need a human, never a threshold. The legend is two items, fed from the response's
 * `autoAcceptThreshold`. The CTA's `disabled` state is paired with a visible `.hint` reason
 * underneath it whenever blocked (ADR-019 accessibility baseline: "a visible reason, not a
 * hidden control") -- never only a `title` tooltip.
 *
 * "Mark as validated" is a real write (`POST /api/documents/{id}/validate`, see
 * `./useReviewSession.ts`): the header therefore also shows the in-flight state, the server's own
 * refusal when there is one, and an already-validated document as closed rather than re-askable.
 */
export default function ReviewHeader({
  header,
  progress,
  onMarkValidated,
  validating = false,
  validationError = null,
  alreadyValidated = false,
  canValidate = true,
  autoAcceptThreshold = null,
}: ReviewHeaderProps) {
  const blocked = isValidationBlocked(progress);
  const thresholdPct = autoAcceptThreshold === null ? null : legendThresholdPct(autoAcceptThreshold);
  const supplier = formatSupplier(header.supplierId);
  const supplierLabel = header.supplierName ?? supplier.label;
  const typeLabel = getContractTypeLabel(header.type);

  const disabled = blocked || validating || alreadyValidated || !canValidate;
  const ctaLabel = validating ? "Validating…" : alreadyValidated ? "Validated" : "Mark as validated";
  const hint = alreadyValidated
    ? "This document is already validated."
    : !canValidate
      ? "This contract has no document to validate."
      : blocked
        ? blockedReason(progress)
        : null;

  return (
    <header className="review-header">
      <div className="review-header-top">
        <div>
          <p className="screen-kicker">R1 · Human validation</p>
          <h2 className="screen-title">{reviewTitle(progress.blockingCount)}</h2>
          <p className="micro-meta review-header-summary" title={header.supplierName ?? supplier.title}>
            {supplierLabel} · {typeLabel}
          </p>
        </div>
        <div className="review-header-cta">
          <button type="button" className="btn btn-primary" disabled={disabled} onClick={onMarkValidated}>
            {ctaLabel}
          </button>
          {hint !== null && <span className="hint">{hint}</span>}
          {validationError !== null && (
            <span className="hint" role="alert">
              {validationError}
            </span>
          )}
        </div>
      </div>
      <div className="review-progress-bar">
        <p className="micro-meta review-progress-line">
          {progress.resolvedCount} of {progress.total} field{progress.total === 1 ? "" : "s"} resolved
          {progress.blockingCount > 0
            ? ` · ${progress.blockingCount} ${progress.blockingCount === 1 ? "needs" : "need"} review`
            : ""}
        </p>
        <p className="review-legend">
          <span className="review-legend-item">
            <span className="tag tag-neutral">{thresholdPct === null ? "auto" : `≥${thresholdPct}%`}</span> auto-accepted
          </span>{" "}
          <span className="review-legend-item">
            <span className="tag tag-outline">{thresholdPct === null ? "review" : `<${thresholdPct}%`}</span> review required
          </span>
        </p>
      </div>
    </header>
  );
}
