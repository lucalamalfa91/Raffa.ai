import type { Contract360HeaderBody } from "../../../api/client";
import { formatSupplier, getContractTypeLabel } from "../portfolioTableFormatters";
import { blockedReason, isValidationBlocked, type ReviewProgress } from "./reviewViewModel";

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
}

/**
 * Review screen header (screens.md #6: "Header + 'Mark as validated' (disabled until all < 80%
 * fields decided). Progress line + legend."; task E07/F03/US01/T01 AC-4; task E11/F07/US01/T01 gap
 * G-REV). Title, one-line summary, and the confidence legend are copied verbatim from
 * `inputs/design/prototypes/day1-demo.html`'s own Review block: kicker `"R1 · Human validation"`,
 * `<h2>Review extraction</h2>` (also this exact route's own CTA label on
 * `../contract360/Contract360Header.tsx`), and a >95%/80-95%/<80% tag legend next to the progress
 * line. The CTA's `disabled` state is paired with a visible `.hint` reason underneath it whenever
 * blocked (ADR-019 accessibility baseline: "a visible reason, not a hidden control") -- never only a
 * `title` tooltip, which is not reliably visible or accessible.
 *
 * "Mark as validated" is a real write now (`POST /api/documents/{id}/validate`, see
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
}: ReviewHeaderProps) {
  const blocked = isValidationBlocked(progress);
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
          <h2 className="screen-title">Review extraction</h2>
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
            <span className="tag tag-neutral">&gt;95%</span> auto-accepted
          </span>{" "}
          <span className="review-legend-item">
            <span className="tag tag-accent">80–95%</span> flagged
          </span>{" "}
          <span className="review-legend-item">
            <span className="tag tag-outline">&lt;80%</span> review required
          </span>
        </p>
      </div>
    </header>
  );
}
