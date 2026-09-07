import type { Contract360HeaderBody } from "../../../api/client";
import { formatSupplier, getContractTypeLabel } from "../portfolioTableFormatters";
import { blockedReason, isValidationBlocked, type ReviewProgress } from "./reviewViewModel";

export interface ReviewHeaderProps {
  header: Contract360HeaderBody;
  progress: ReviewProgress;
  onMarkValidated: () => void;
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
 * The export's own one-line summary ("SAP · S/4HANA Cloud · Private Edition ·
 * SAP_S4HANA_Cloud_OrderForm_2024.pdf") is that specific demo contract's own mock data -- `Contract`
 * has no supplier-name or filename field, the same gap `../contract360/Contract360Header.tsx`'s own
 * header comment already names. This reuses that component's exact honest substitutes
 * (`formatSupplier`/`getContractTypeLabel`) rather than inventing a new one.
 */
export default function ReviewHeader({ header, progress, onMarkValidated }: ReviewHeaderProps) {
  const blocked = isValidationBlocked(progress);
  const supplier = formatSupplier(header.supplierId);
  const typeLabel = getContractTypeLabel(header.type);

  return (
    <header className="review-header">
      <div className="review-header-top">
        <div>
          <p className="screen-kicker">R1 · Human validation</p>
          <h2 className="screen-title">Review extraction</h2>
          <p className="micro-meta review-header-summary" title={supplier.title}>
            {supplier.label} · {typeLabel}
          </p>
        </div>
        <div className="review-header-cta">
          <button type="button" className="btn btn-primary" disabled={blocked} onClick={onMarkValidated}>
            Mark as validated
          </button>
          {blocked && <span className="hint">{blockedReason(progress)}</span>}
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
