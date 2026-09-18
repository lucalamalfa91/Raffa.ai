import { Link } from "react-router-dom";
import type { ApiClient, RenewalPipelineItemBody } from "../../api/client";
import {
  RENEWAL_ACTION_KINDS,
  formatContractRef,
  formatPaneHeading,
  getRenewalActionPlan,
  type RenewalActionKind,
} from "./renewalPipelineViewModel";
import type { RenewalActionRow } from "../../api/client";
import NegotiationTodoList from "./NegotiationTodoList";

export interface InsightCardProps {
  item: RenewalPipelineItemBody;
  /** This session's own recorded action for `item.contractId`, or `null` before anything has been actioned. */
  tracked: RenewalActionRow | null;
  /** Which action (if any) is mid-flight -- disables both buttons while any one is pending, so a second click cannot race the first. */
  actionPending: RenewalActionKind | null;
  /** The last action attempt's own failure message, or `null`. Never silently swallowed. */
  actionError: string | null;
  onAction: (kind: RenewalActionKind) => void;
  /**
   * Task E29/F04/US01/T01 (todo-web): threaded one level further down into `NegotiationTodoList`
   * below, which owns its own `GET`/`PUT /api/renewals/{id}/negotiation-todos` fetch/tick lifecycle
   * (see that component's own doc comment for why it, not `index.tsx` or this component, is the one
   * that owns that state). `apiClient` is already a prop on every route above this one
   * (`RenewalsRouteProps`); this is the first time a leaf this far down the renewals tree has needed
   * it directly.
   */
  apiClient: ApiClient;
  /** The signed-in caller's current workspace id (`index.tsx`'s own `workspace.id`) -- see
   * `NegotiationTodoList`'s own doc comment for why it is threaded as a prop rather than re-read
   * from session storage in this leaf. */
  tenantId: string;
}

/**
 * The V2 "Why it is here" pane beside the priority list (screens-v2.md #7 "insight card for the
 * selected row (facts + recommended action + rationale); actions Start negotiation / Assign; 'Open
 * contract →'"; `raffa-v2/markup.html` "RENEWALS" block, right column). Copy and structure quoted
 * from that block: `.card-kicker` "Why it is here" → h3 "{{ rsel.supplier }} — {{ rsel.cancelDays }}
 * days to notice" → the contract line → the accent "Recommended action" kicker → the action
 * (heading face, 18px) → the rationale → either the two block buttons (`rselOpen`) or, once acted,
 * the bordered "{{ rsel.st }} · owner … Open contract →" box (`rselActed`) → "See the facts behind
 * this →".
 *
 * **Facts vs AI (ADR-019).** The recommendation and its rationale are the renewal engine's own
 * deterministic output (`RenewalInsightCard.recommendations`, `backend/src/Raffa.Renewals`), not a
 * model's prose -- the pane names them as the recommended action, and the facts they rest on live one
 * click away on Contract 360 ("See the facts behind this →"), exactly where the prototype sends the
 * reader. The Day-1 card's six-cell fact grid (uplift, market position, potential savings -- all
 * honestly "Not yet available" until the benchmark modules feed the engine) is not part of this pane.
 *
 * **Negotiation TODOs (task E29/F04/US01/T01, wave w19 NW-85).** Below the recommended-action block
 * and above "See the facts behind this →", `NegotiationTodoList` renders the negotiation points Ask
 * ranked and persisted for this contract (`GET /api/renewals/{id}/negotiation-todos`) and lets
 * Procurement/Admin tick one done (`PUT` of the same route). Not part of the original V2 prototype
 * sequence quoted above -- this wave's own ux-ui-designer ruling is the source for this sub-surface
 * ("`.table` sub-surface, Mark-done `.btn-secondary`, Open/Done tags").
 */
export default function InsightCard({
  item,
  tracked,
  actionPending,
  actionError,
  onAction,
  apiClient,
  tenantId,
}: InsightCardProps) {
  const { recommendations } = item.insightCard;
  const contractRef = formatContractRef(item.contractId);
  const contractHref = `/contracts/${item.contractId}`;

  return (
    <aside className="renewal-pane" aria-label="Why it is here">
      <span className="card-kicker">Why it is here</span>
      <h3 className="renewal-pane-heading">{formatPaneHeading(item.supplierName, item.daysUntilCancellationDeadline)}</h3>
      <p className="renewal-pane-contract" title={contractRef.title}>
        {contractRef.label}
      </p>

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
          <Link to={contractHref} className="btn btn-ghost renewal-pane-inline-link">
            Open contract →
          </Link>
        </div>
      )}

      {actionError !== null && (
        <p className="hint" role="alert">
          {actionError}
        </p>
      )}

      <NegotiationTodoList apiClient={apiClient} tenantId={tenantId} contractId={item.contractId} />

      <Link to={contractHref} className="btn btn-ghost renewal-pane-facts-link">
        See the facts behind this →
      </Link>
    </aside>
  );
}
