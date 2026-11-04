import type { Contract360HeaderBody } from "../../../api/client";
import { CopyTip } from "../../../components/InfoTip";
import { TIPS } from "../../../components/infoTipCopy";
import ScreenTitle from "../../../components/ScreenTitle";
import { formatSupplier, getContractTypeLabel } from "../../contracts/portfolioTableFormatters";
import { useI18n } from "../../../i18n/I18nProvider";
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
  const { t } = useI18n();
  const blocked = isValidationBlocked(progress);
  const thresholdPct = autoAcceptThreshold === null ? null : legendThresholdPct(autoAcceptThreshold);
  const supplier = formatSupplier(header.supplierId);
  // `formatSupplier`'s "Supplier 1a2b3c4d" fallback, with its word in the screen's language.
  const supplierLabel =
    header.supplierName ?? (header.supplierId === null ? supplier.label : t("Supplier {id}", { id: header.supplierId.slice(0, 8) }));
  const typeLabel = t(getContractTypeLabel(header.type));

  const disabled = blocked || validating || alreadyValidated || !canValidate;
  const ctaLabel = validating ? t("Validating…") : alreadyValidated ? t("Validated") : t("Mark as validated");
  const hint = alreadyValidated
    ? t("This document is already validated.")
    : !canValidate
      ? t("This contract has no document to validate.")
      : blocked
        ? blockedReason(progress, t)
        : null;
  const progressLine =
    progress.total === 1
      ? t("{resolved} of {total} field resolved", { resolved: progress.resolvedCount, total: progress.total })
      : t("{resolved} of {total} fields resolved", { resolved: progress.resolvedCount, total: progress.total });
  const needsReviewLine =
    progress.blockingCount === 0
      ? null
      : progress.blockingCount === 1
        ? t("{n} needs review", { n: progress.blockingCount })
        : t("{n} need review", { n: progress.blockingCount });

  return (
    <header className="review-header">
      <div className="review-header-top">
        <div>
          <p className="screen-kicker">{t("R1 · Human validation")}</p>
          <ScreenTitle guide="review">{reviewTitle(progress.blockingCount, t)}</ScreenTitle>
          <p className="micro-meta review-header-summary" title={header.supplierName ?? supplier.title}>
            {supplierLabel} · {typeLabel}
          </p>
        </div>
        <div className="review-header-cta">
          <span className="review-header-cta-row">
            <button type="button" className="btn btn-primary" disabled={disabled} onClick={onMarkValidated}>
              {ctaLabel}
            </button>
            <CopyTip tip={TIPS.reviewValidate} align="end" />
          </span>
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
          {progressLine}
          {needsReviewLine !== null ? ` · ${needsReviewLine}` : ""}
        </p>
        <p className="review-legend">
          <span className="review-legend-item">
            <span className="tag tag-neutral">{thresholdPct === null ? t("auto") : `≥${thresholdPct}%`}</span> {t("auto-accepted")}
          </span>{" "}
          <span className="review-legend-item">
            <span className="tag tag-outline">{thresholdPct === null ? t("review⟦legend⟧") : `<${thresholdPct}%`}</span> {t("review required")}
          </span>
        </p>
      </div>
    </header>
  );
}
