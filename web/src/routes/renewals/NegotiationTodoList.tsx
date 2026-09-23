import { useCallback, useEffect, useState } from "react";
import type { ApiClient, RenewalNegotiationTodoRow, RenewalNegotiationTodoStatusValue } from "../../api/client";
import type { SemanticTag } from "../../styles/semantics";
import { CopyTip } from "../../components/InfoTip";
import { TIPS } from "../../components/infoTipCopy";

export interface NegotiationTodoListProps {
  apiClient: ApiClient;
  /** The signed-in caller's current workspace id (`../../routes/signin/workspaceStore.ts`'s
   * `CurrentWorkspace.id`, the same `workspace.id` `index.tsx` already resolves before it will even
   * render `InsightCard`) -- threaded as a prop rather than re-read here, the same "leaf receives
   * already-resolved data" convention `RenewalTable.tsx` already follows (this component is a leaf
   * under `InsightCard.tsx`, not a route). */
  tenantId: string;
  /** `RenewalPipelineItemBody.contractId` -- the same id `GET /api/renewals` returns per row, and
   * the same `{id}` `GET`/`PUT /api/renewals/{id}/negotiation-todos` key on. */
  contractId: string;
}

type FetchState =
  | { phase: "loading" }
  | { phase: "error"; message: string }
  | { phase: "ready"; todos: readonly RenewalNegotiationTodoRow[] };

/**
 * `Superseded` (`RenewalNegotiationTodoService.UpsertAsync`'s own "vanished -> Superseded"
 * reconciliation) means the ranker no longer grounds this point -- the backend's own `GetAsync` doc
 * comment is explicit that it "does not filter by status" and leaves that decision to the client.
 * The parent story is titled "the negotiation TODOs **Ask wrote**" (present tense): a retired point
 * is not one of those any more, so it never reaches the rendered list. `Done` stays visible --
 * otherwise a repeat ask's own "Done preserved" (parent story AC-3) would be invisible on screen --
 * and the wave's own UX decision (`reports/architecture/waves/w19.md` NW-85: "Open/Done tags") never
 * names a third, Superseded tag at all.
 */
export function isVisibleNegotiationTodo(todo: RenewalNegotiationTodoRow): boolean {
  return todo.status !== "Superseded";
}

/** Open -> neutral / Done -> accent, the same "un-acted stays neutral, acted gets the accent
 * emphasis" mapping `renewalPipelineViewModel.ts#getRenewalStatusTag` already establishes for its
 * sibling entity. `Superseded` is mapped defensively (outline, matching `getConfidenceTag`'s own
 * "review_required" treatment for an in-between state) even though `isVisibleNegotiationTodo` above
 * keeps it off the rendered list -- so this function stays total and honest on its own. */
export function getNegotiationTodoStatusTag(status: RenewalNegotiationTodoStatusValue): SemanticTag {
  switch (status) {
    case "Done":
      return { variant: "accent", label: "Done" };
    case "Superseded":
      return { variant: "outline", label: "Superseded" };
    case "Open":
    default:
      return { variant: "neutral", label: "Open" };
  }
}

/**
 * The Renewals insight pane's negotiation TODO list (task E29/F04/US01/T01, todo-web; parent story
 * us-01-todo-web; wave w19 NW-85; ADR-012 w19 cl. 52 / ADR-020 w19 cl. 41 -- see this folder's own
 * `client.ts` provenance comment for why `reports/architecture/waves/w19.md`'s NW-85 row, not the
 * ADR bodies, is the citable source of that decision). Design anchor: `screens-v2.md` §7 (Renewals)
 * / §8 (Savings) -- both describe the same "list + status tag + action" shape this sub-surface
 * reuses; the wave's own ux-ui-designer ruling names "Mark-done `.btn-secondary`, Open/Done tags".
 * Its "`.table` sub-surface" did not survive the pane: five columns of sentence-long current /
 * target / why text inside a 380px column wrapped to one word per line, so each point is a stacked
 * item instead -- topic + tag, then Current / Target / Why as full-width label rows.
 *
 * Embedded in `InsightCard.tsx`'s "Why it is here" pane, below the existing recommended-action
 * block -- the negotiation points Ask ranked and persisted for this contract
 * (`Raffa.Insights.Application.NegotiationPointRanker`, epic-31; `RenewalNegotiationTodoService
 * .UpsertAsync`, epic-29/feature-01/02), read back over `GET /api/renewals/{id}/negotiation-todos`
 * and independently owning its own fetch/tick lifecycle (the same "leaf owns its own server state"
 * shape `src/routes/contracts/contract360/AnswersBand.tsx`'s negotiation-steps ticks use one level up,
 * in `contract360/index.tsx` -- this component has no route-level parent of its own to lift state
 * into, since `InsightCard` is itself a leaf under `renewals/index.tsx`, so it owns this one).
 *
 * **Never invents a point** (client-architect's own rule, parent story AC-3): a `Mark done` button
 * only ever PUTs a `pointKey` this component already read back from the server; there is no "add a
 * point" affordance anywhere here. A tick's response replaces that one row in place with the server's
 * own returned state -- never an optimistic local flip -- so "Done ticks survive a repeat ask" is
 * something this screen actually proves, not assumes: what renders after a tick is what the server
 * just persisted, not what the click predicted.
 */
export default function NegotiationTodoList({ apiClient, tenantId, contractId }: NegotiationTodoListProps) {
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  const [tickError, setTickError] = useState<string | null>(null);

  const load = useCallback(() => {
    setFetchState({ phase: "loading" });
    setPendingKey(null);
    setTickError(null);

    void apiClient.getRenewalNegotiationTodos(tenantId, contractId).then((result) => {
      if (!result.ok || !result.todos) {
        setFetchState({
          phase: "error",
          message: result.error ?? "The negotiation TODOs could not be loaded.",
        });
        return;
      }
      setFetchState({ phase: "ready", todos: result.todos });
    });
  }, [apiClient, tenantId, contractId]);

  useEffect(() => {
    load();
  }, [load]);

  const handleTick = (pointKey: string) => {
    setPendingKey(pointKey);
    setTickError(null);

    void apiClient.tickRenewalNegotiationTodo(tenantId, contractId, { pointKey }).then((result) => {
      setPendingKey(null);

      if (!result.ok || !result.todo) {
        setTickError(result.error ?? "This point could not be marked done. Try again.");
        return;
      }

      // Replace exactly this row with the server's own returned state -- see this component's own
      // doc comment for why that is not an optimistic local mutation.
      const ticked = result.todo;
      setFetchState((current) =>
        current.phase === "ready"
          ? { ...current, todos: current.todos.map((todo) => (todo.pointKey === ticked.pointKey ? ticked : todo)) }
          : current,
      );
    });
  };

  if (fetchState.phase === "loading") {
    return (
      <section className="renewal-pane-todos" aria-label="Negotiation TODOs">
        <p className="card-kicker">Negotiation TODOs</p>
        <p className="micro-meta">Loading negotiation TODOs…</p>
      </section>
    );
  }

  if (fetchState.phase === "error") {
    return (
      <section className="renewal-pane-todos" aria-label="Negotiation TODOs">
        <p className="card-kicker">Negotiation TODOs</p>
        <p className="hint" role="alert">
          {fetchState.message}
        </p>
        <button type="button" className="btn btn-secondary" onClick={load}>
          Retry
        </button>
      </section>
    );
  }

  const visibleTodos = fetchState.todos.filter(isVisibleNegotiationTodo);

  return (
    <section className="renewal-pane-todos" aria-label="Negotiation TODOs">
      <p className="card-kicker">
        Negotiation TODOs
        <CopyTip tip={TIPS.renewalsTodos} align="end" />
      </p>

      {visibleTodos.length === 0 ? (
        <p className="micro-meta">No negotiation points yet.</p>
      ) : (
        <ol className="renewal-todos">
          {visibleTodos.map((todo) => {
            const tag = getNegotiationTodoStatusTag(todo.status);
            return (
              <li key={todo.pointKey} className="renewal-todo">
                <div className="renewal-todo-head">
                  <span className="renewal-todo-topic">{todo.topic}</span>
                  <span className={`tag tag-${tag.variant}`}>{tag.label}</span>
                </div>
                <dl className="renewal-todo-facts">
                  <dt>Current</dt>
                  <dd>{todo.current}</dd>
                  <dt>Target</dt>
                  <dd>{todo.target}</dd>
                  <dt>Why</dt>
                  <dd className="renewal-todo-why">{todo.rationale}</dd>
                </dl>
                {todo.status === "Open" && (
                  <button
                    type="button"
                    className="btn btn-secondary renewal-todo-done"
                    aria-label={`Mark ${todo.topic} done`}
                    disabled={pendingKey !== null}
                    onClick={() => handleTick(todo.pointKey)}
                  >
                    {pendingKey === todo.pointKey ? "Saving…" : "Mark done"}
                  </button>
                )}
              </li>
            );
          })}
        </ol>
      )}

      {tickError !== null && (
        <p className="hint" role="alert">
          {tickError}
        </p>
      )}
    </section>
  );
}
