import { Link } from "react-router-dom";
import type { Contract360HeaderBody, RenewalPriorityBody } from "../../../api/client";
import { daysUntil } from "../portfolioAttention";
import {
  formatAnnualSpend as formatPlainAmount,
  formatAutoRenewal,
  formatDateOnly,
  getContractTypeLabel,
  getPortfolioRiskTag,
  getPortfolioStatusTag,
} from "../portfolioTableFormatters";
import { formatPriorityFact, resolveSupplierLabel, type BackLink } from "./contract360ViewModel";

export interface Contract360HeaderProps {
  header: Contract360HeaderBody;
  /** This contract's own linked-document count (`tabs.documents.length`) -- see this component's own comment on the "family" wording gap. */
  docCount: number;
  /** `null` while the `GET /api/renewals/{id}/priority` call has not resolved (or failed) yet. */
  priority: RenewalPriorityBody | null;
  /**
   * Task E13/F10/US01/T01 (contract360-landing, AC-1 "back label reads 'Ask Contigo' when arriving
   * from a chat"; ADR-020 screen 5 "Back label follows the origin"). Resolved by the route from
   * `location.state.from` (`contract360ViewModel.ts#resolveBackLink`) -- `null` renders no back
   * link at all, the same as before this task.
   */
  backLink: BackLink | null;
}

/**
 * Contract 360 header + 6-cell fact row (ADR-020 screen 5, screens.md #5; task E07/F02/US01/T01
 * AC-1/AC-2). Quoted verbatim from day1-demo.html's own header block: supplier kicker (`cur.
 * supplier`) / name (`cur.name`) / type + status tags / doc count, then a
 * `grid-template-columns:repeat(6,1fr)` row of Annual spend / TCV / Start → End / Renewal /
 * Cancellation deadline / Risk.
 *
 * Two honest adaptations from the cited prototype's own mock data, both already established by
 * `../portfolioTableFormatters.ts` for the identical gaps on the Portfolio screen (never re-derived
 * here):
 *  - `Contract` has no title/name field yet, so the h2 title reuses the same type-label proxy the
 *    Portfolio table's own "Contract" column already uses (`getContractTypeLabel`) -- the type tag
 *    just below repeats it in compact form, which is redundant but not fabricated.
 *  - `Contract.SupplierId` is a bare id (Suppliers/Products has no name-resolution endpoint), so the
 *    kicker fell back to the same short id fragment + full-id tooltip `formatSupplier` already
 *    renders for the Portfolio table's own "Supplier" column -- superseded by a real name whenever
 *    one is available, see the "Citation landing" paragraph below.
 *
 * "Cancellation deadline" also drops the prototype's `{{ cur.notice }}-day notice` subtext: no
 * `CancellationNoticeDays` column exists anywhere in this schema yet
 * (`Contigo.Renewals.Application.ContractRenewalTerms`'s own doc comment names this exact gap) --
 * this shows a real "in N days" countdown instead of a notice-day figure this codebase cannot know.
 * "N documents in family" becomes "N documents" -- this endpoint returns one contract's own linked
 * documents, not the full amendment chain `Contract.ParentContractId` could in principle walk, so
 * "family" would overclaim what was actually counted.
 *
 * **Citation landing (task E13/F10/US01/T01, AC-1/AC-2/AC-3)**: three additions on top of the
 * V1 header above. (1) `backLink`, when non-null, renders `markup.html`'s own `← {{ backLabel }}`
 * ghost link (`contract360ViewModel.ts#resolveBackLink`) ahead of the kicker/title, exactly the
 * prototype's screen-5 position. (2) The supplier kicker now prefers a real `supplierName`
 * (`resolveSupplierLabel`, read defensively -- see that function's own doc comment) over the
 * `formatSupplier` id-fragment fallback. (3) **Ask about it** (`.btn-secondary`) navigates to
 * `/ask?scope=<contractId>`, a *new* chat scoped to this contract -- the Ask route itself does not
 * consume `?scope=` yet (`web/src/routes/ask/**` is F09/T04's own scope, out of this task's "Files
 * to create or modify"), so this link is forward-compatible/dormant until that task lands, the same
 * pattern `Contract360Header.tsx` already uses for the always-real "Review extraction" link above.
 */
export default function Contract360Header({ header, docCount, priority, backLink }: Contract360HeaderProps) {
  const typeLabel = getContractTypeLabel(header.type);
  const statusTag = getPortfolioStatusTag(header.status);
  const riskTag = getPortfolioRiskTag(header.risk);
  const supplier = resolveSupplierLabel(header);
  const renewalDays = daysUntil(header.renewalDate);
  const cancelDays = daysUntil(header.cancellationDeadline);

  return (
    <header className="contract360-header">
      {backLink !== null && (
        <Link to={backLink.href} className="btn btn-ghost contract360-back-link">
          ← {backLink.label}
        </Link>
      )}
      <div className="contract360-header-top">
        <div>
          <p className="screen-kicker" title={supplier.title}>
            {supplier.label}
          </p>
          <h2 className="screen-title">{typeLabel}</h2>
          <div className="contract360-header-tags">
            <span className="tag tag-neutral">{typeLabel}</span>
            <span className={`tag tag-${statusTag.variant}`}>{statusTag.label}</span>
            <span className="micro-meta">
              {docCount} document{docCount === 1 ? "" : "s"}
            </span>
          </div>
        </div>
        <div className="contract360-header-actions">
          {/* AC-2 "Contract 360 offers Ask about it, navigating to /ask?scope=<contractId>" -- a new
              chat, not a continuation of whatever conversation (if any) cited this contract. */}
          <Link to={`/ask?scope=${header.contractId}`} className="btn btn-secondary">
            Ask about it
          </Link>
          {/* Absolute path (not a relative "review") -- explicit and unambiguous regardless of this
              component's own position in the route tree, matching ADR-018's route map literally
              (`/contracts/:id/review`). */}
          <Link to={`/contracts/${header.contractId}/review`} className="btn btn-primary">
            Review extraction
          </Link>
        </div>
      </div>

      <div className="contract360-fact-row">
        <div className="contract360-fact-cell">
          <span className="micro-meta">Annual spend</span>
          <span className="key-fact-number">{formatPlainAmount(header.annualSpend)}</span>
        </div>
        <div className="contract360-fact-cell">
          <span className="micro-meta">TCV</span>
          <span className="key-fact-number">{formatPlainAmount(header.totalContractValue)}</span>
        </div>
        <div className="contract360-fact-cell">
          <span className="micro-meta">Start → End</span>
          <span className="contract360-fact-value">
            {formatDateOnly(header.startDate)} → {formatDateOnly(header.endDate)}
          </span>
        </div>
        <div className="contract360-fact-cell">
          <span className="micro-meta">Renewal</span>
          <span className="key-fact-number">{formatDateOnly(header.renewalDate)}</span>
          <span className="micro-meta">
            {renewalDays !== null ? `in ${renewalDays} days · ` : ""}
            auto-renew {formatAutoRenewal(header.autoRenewal)}
          </span>
        </div>
        <div className="contract360-fact-cell">
          <span className="micro-meta">Cancellation deadline</span>
          <span className="key-fact-number">{formatDateOnly(header.cancellationDeadline)}</span>
          <span className="micro-meta">{cancelDays !== null ? `in ${cancelDays} days` : "—"}</span>
        </div>
        <div className="contract360-fact-cell">
          <span className="micro-meta">Risk</span>
          <span className={`tag tag-${riskTag.variant}`}>{riskTag.label}</span>
          <span className="micro-meta">{formatPriorityFact(priority)}</span>
        </div>
      </div>
    </header>
  );
}
