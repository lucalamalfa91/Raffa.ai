import { Link } from "react-router-dom";
import type { ApiClient, RenewalPipelineItemBody } from "../../api/client";
import {
  RENEWAL_ACTION_KINDS,
  buildRenewalFacts,
  formatContractRef,
  formatPaneHeading,
  getRenewalActionPlan,
  getRenewalOwner,
  type RenewalActionKind,
  type RenewalTableRow,
} from "./renewalPipelineViewModel";
import { buildScoreParts } from "../contracts/contract360/contract360ViewModel";
import { buildRenewalActionContext, buildRenewalActionGroups } from "./renewalActions";
import RenewalActionLauncher from "./RenewalActionLauncher";
import NegotiationTodoList from "./NegotiationTodoList";

export interface InsightCardProps {
  /** The selected row: the pipeline item, its score, the persisted action (`tracked`) and the portfolio's contract info. */
  row: RenewalTableRow;
  /** Which action (if any) is mid-flight -- disables both buttons while any one is pending, so a second click cannot race the first. */
  actionPending: RenewalActionKind | null;
  /** The last action attempt's own failure message, or `null`. Never silently swallowed. */
  actionError: string | null;
  onAction: (kind: RenewalActionKind) => void;
  /**
   * Task E29/F04/US01/T01 (todo-web): threaded one level further down into `NegotiationTodoList`
   * below, which owns its own `GET`/`PUT /api/renewals/{id}/negotiation-todos` fetch/tick lifecycle
   * (see that component's own doc comment for why it, not `index.tsx` or this component, is the one
   * that owns that state).
   */
  apiClient: ApiClient;
  /** The signed-in caller's current workspace id (`index.tsx`'s own `workspace.id`) -- see
   * `NegotiationTodoList`'s own doc comment for why it is threaded as a prop rather than re-read
   * from session storage in this leaf. */
  tenantId: string;
}

function contract360State(item: RenewalPipelineItemBody) {
  // Contract 360's "← Renewals" comes back to this same row, not the top of the list.
  return { from: "renewals", returnTo: `/renewals?select=${encodeURIComponent(item.contractId)}` };
}

/**
 * The "Why it is here" pane beside the priority list, and the one place every renewal action
 * starts from (screens-v2.md #7 "insight card for the selected row (facts + recommended action +
 * rationale); actions Start negotiation / Assign; 'Open contract →'"). Top to bottom:
 *
 * 1. `.card-kicker` "Why it is here" → h3 "{supplier} — {N} days to notice" → the contract line
 *    (portfolio type when known) with the owner once someone has claimed it.
 * 2. Four key facts -- notice by, renews, annual spend, priority -- and the priority's five
 *    components one click away (`buildScoreParts`, the same bars Contract 360 draws).
 * 3. The accent "Recommended action" + rationale, then the workflow: "Start negotiation" / "Assign
 *    to me" (the real `POST /api/renewals/{id}/action`), or, once acted, the "{status} · owner …
 *    Open contract →" box.
 * 4. "What you can do from here" -- the action registry (`renewalActions.ts`): Ask Raffa launches
 *    bound to this contract, the screens that open with it in context, and the operations coming
 *    next. New capabilities are added there, not here.
 * 5. Negotiation TODOs Ask ranked for this contract (`NegotiationTodoList`), then "See the facts
 *    behind this →".
 *
 * **Facts vs AI (ADR-019).** The recommendation and its rationale are the renewal engine's own
 * deterministic output (`RenewalInsightCard.recommendations`), not a model's prose; the facts they
 * rest on live one click away on Contract 360.
 */
export default function InsightCard({ row, actionPending, actionError, onAction, apiClient, tenantId }: InsightCardProps) {
  const { item, tracked } = row;
  const { recommendations } = item.insightCard;
  const contractRef = formatContractRef(item.contractId, row.contract);
  const contractHref = `/contracts/${item.contractId}`;
  const owner = getRenewalOwner(item);
  const facts = buildRenewalFacts(row);
  const scoreParts = buildScoreParts(item.priority ?? null);
  const actionGroups = buildRenewalActionGroups(buildRenewalActionContext(row));

  return (
    <aside className="renewal-pane" aria-label="Why it is here">
      <span className="card-kicker">Why it is here</span>
      <h3 className="renewal-pane-heading">{formatPaneHeading(item.supplierName, item.daysUntilCancellationDeadline)}</h3>
      <p className="renewal-pane-contract" title={contractRef.title}>
        {contractRef.label}
        {owner !== null && tracked === null && <> · assigned to {owner}</>}
      </p>

      <dl className="renewal-facts">
        {facts.map((fact) => (
          <div key={fact.key} className="renewal-fact">
            <dt>{fact.label}</dt>
            <dd className={fact.urgent ? "deadline-critical" : undefined}>{fact.value}</dd>
          </div>
        ))}
      </dl>

      {scoreParts.length > 0 && (
        <details className="renewal-score-details">
          <summary>What the priority is made of</summary>
          <div className="renewal-score-parts">
            {scoreParts.map((part) => (
              <div key={part.key} className="renewal-score-part">
                <div className="renewal-score-part-row">
                  <span>{part.label}</span>
                  <span className="renewal-score-part-value">{part.value}</span>
                </div>
                <div className="renewal-score-bar" aria-hidden="true">
                  <div className={`renewal-score-bar-fill${part.accent ? " is-accent" : ""}`} style={{ width: part.width }} />
                </div>
              </div>
            ))}
          </div>
        </details>
      )}

      <p className="renewal-pane-action-kicker">Recommended action</p>
      <p className="renewal-pane-action">{recommendations.recommendedAction}</p>
      <p className="renewal-pane-rationale">{recommendations.explanation}</p>

      {tracked === null ? (
        <div className="renewal-pane-actions">
          {RENEWAL_ACTION_KINDS.map((kind) => {
            const plan = getRenewalActionPlan(kind);
            return (
              <button
                key={kind}
                type="button"
                className={`btn btn-${plan.emphasis} btn-block`}
                disabled={actionPending !== null}
                onClick={() => onAction(kind)}
              >
                {actionPending === kind ? "Saving…" : plan.buttonLabel}
              </button>
            );
          })}
        </div>
      ) : (
        <div className="renewal-pane-acted" role="status">
          <strong>{tracked.action}</strong> · owner {tracked.owner}.{" "}
          <Link to={contractHref} state={contract360State(item)} className="btn btn-ghost renewal-pane-inline-link">
            Open contract →
          </Link>
          {tracked.status === "InProgress" && (
            <span className="renewal-pane-acted-next">Close the cycle — renewed or notice sent — from Contract 360.</span>
          )}
        </div>
      )}

      {actionError !== null && (
        <p className="hint" role="alert">
          {actionError}
        </p>
      )}

      <RenewalActionLauncher groups={actionGroups} />

      <NegotiationTodoList apiClient={apiClient} tenantId={tenantId} contractId={item.contractId} />

      <Link to={contractHref} state={contract360State(item)} className="btn btn-ghost renewal-pane-facts-link">
        See the facts behind this →
      </Link>
    </aside>
  );
}
