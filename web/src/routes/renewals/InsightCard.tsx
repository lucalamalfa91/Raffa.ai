import { Link } from "react-router-dom";
import type { RenewalPipelineItemBody } from "../../api/client";
import { daysUntil } from "../contracts/portfolioAttention";
import { formatAnnualSpend, formatDateOnly, formatSupplier } from "../contracts/portfolioTableFormatters";
import { isDeadlineCritical } from "../../styles/semantics";
import {
  RENEWAL_ACTION_KINDS,
  formatContractRef,
  formatMarketPosition,
  formatPotentialSavings,
  formatUpliftPercent,
  getInsightOwner,
  getRenewalActionPlan,
  type RenewalActionKind,
} from "./renewalPipelineViewModel";
import type { TrackedRenewalAction } from "./renewalActionStore";

export interface InsightCardProps {
  item: RenewalPipelineItemBody;
  /** This session's own recorded action for `item.contractId`, or `null` before anything has been actioned. */
  tracked: TrackedRenewalAction | null;
  /** Which action (if any) is currently mid-flight -- disables every button while any one is pending, so a second click cannot race the first. */
  actionPending: RenewalActionKind | null;
  /** The last action attempt's own failure message, or `null`. Never silently swallowed. */
  actionError: string | null;
  onAction: (kind: RenewalActionKind) => void;
}

/**
 * AC-3 "Insight card (§9.3 fields: spend, cancellation deadline, uplift, market position, potential
 * savings, owner; recommended action + rationale) with actions Start negotiation / Assign to me /
 * Snooze -> confirmation + link to Contract 360" (screens.md #8). Rendered as the shared
 * `.detail-pane` (ADR-019 component catalogue: "340-400px, surface, 2px left rule; wraps below the
 * list under ~900px main width") -- the same responsive side-panel primitive
 * `src/styles/components.css` already defines, not a forked layout.
 *
 * **Facts vs AI, inside one card.** Unlike Contract 360's Overview tab (a *separate*
 * `.ai-recommendation` card from every fact table, ADR-019 "Facts vs AI separation"), spec §9.3 and
 * screens.md #8 both describe *one* insight card holding both groups -- and the backend's own
 * `RenewalInsightCard` wire shape already keeps them apart as two named, non-overlapping objects
 * (`facts` / `recommendations`, never merged into one flat bag -- see that record's own doc comment
 * in `backend/src/Contigo.Renewals/Application/RenewalPipelineItem.cs`). This component honours that
 * same separation visually within the one card: the deterministic facts render as plain labelled
 * values; only the recommended-action block below them is wrapped in the shared `.ai-recommendation`
 * / `.ai-recommendation-label` classes `OverviewTab.tsx` already established, so the two are still
 * never rendered as one indistinguishable paragraph.
 *
 * The confirmation panel (rendered only once `tracked !== null`, i.e. this browser has acted on this
 * row this session) is adapted, not copied verbatim, from day1-demo.html's own confirmation text
 * (`"{{ rsel.st }} - owner {{ rsel.owner }}. A SavingsOpportunity was created and appears on the
 * Savings screen."`): this app cannot honestly claim a `SavingsOpportunity` row was created (
 * `SavingsOpportunityService.CreateAsync` is not wired to an HTTP route yet -- see
 * `../renewals/renewalActionStore.ts`'s own header comment), so the wording says what is actually
 * true -- a real, durable renewal action was recorded, tracked here as an opportunity for Home --
 * and links to both Home (this task's own required "action creates opportunity link to Home") and
 * Contract 360 (AC-3's own named link) rather than overclaiming a backend entity that does not exist.
 */
export default function InsightCard({ item, tracked, actionPending, actionError, onAction }: InsightCardProps) {
  const { facts, recommendations } = item.insightCard;
  const cancelDays = daysUntil(facts.cancellationDeadline);
  const supplier = formatSupplier(facts.supplierId);
  const contractRef = formatContractRef(item.contractId);

  return (
    <aside className="detail-pane renewal-insight-card">
      <p className="screen-kicker">Insight card</p>
      <h6>
        {supplier.label} · {contractRef.label}
      </h6>

      <div className="renewal-insight-facts">
        <div className="renewal-fact-cell">
          <span className="micro-meta">Annual spend</span>
          <span className="key-fact-number">{formatAnnualSpend(facts.annualSpend)}</span>
        </div>
        <div className="renewal-fact-cell">
          <span className="micro-meta">Cancellation deadline</span>
          <span className={cancelDays !== null && isDeadlineCritical(cancelDays) ? "deadline-critical" : "key-fact-number"}>
            {formatDateOnly(facts.cancellationDeadline)}
          </span>
        </div>
        <div className="renewal-fact-cell">
          <span className="micro-meta">Annual uplift</span>
          <span>{formatUpliftPercent(recommendations.annualUpliftPercent)}</span>
        </div>
        <div className="renewal-fact-cell">
          <span className="micro-meta">Market position</span>
          <span>{formatMarketPosition(recommendations.marketPosition)}</span>
        </div>
        <div className="renewal-fact-cell">
          <span className="micro-meta">Potential savings</span>
          <span>{formatPotentialSavings(recommendations.potentialSavingsRange)}</span>
        </div>
        <div className="renewal-fact-cell">
          <span className="micro-meta">Owner</span>
          <span>{getInsightOwner(tracked)}</span>
        </div>
      </div>

      <div className="ai-recommendation">
        <p className="ai-recommendation-label">Recommended action</p>
        <p className="renewal-recommendation-statement">{recommendations.recommendedAction}</p>
        <p className="micro-meta">{recommendations.explanation}</p>
      </div>

      <div className="renewal-insight-actions">
        {RENEWAL_ACTION_KINDS.map((kind) => {
          const plan = getRenewalActionPlan(kind);
          return (
            <button
              key={kind}
              type="button"
              className="btn btn-secondary btn-block"
              disabled={actionPending !== null}
              onClick={() => onAction(kind)}
            >
              {actionPending === kind ? "Saving…" : plan.buttonLabel}
            </button>
          );
        })}
      </div>

      {actionError !== null && (
        <p className="hint" role="alert">
          {actionError}
        </p>
      )}

      {tracked !== null && (
        <div className="renewal-confirmation" role="status">
          <p>
            <strong>{tracked.action}</strong> · owner {tracked.owner}. This renewal is now tracked as an
            opportunity — visible on Home.
          </p>
          <Link to={`/contracts/${item.contractId}`} className="btn btn-ghost">
            Open Contract 360 →
          </Link>
          <Link to="/" className="btn btn-ghost">
            Open Home →
          </Link>
        </div>
      )}
    </aside>
  );
}
