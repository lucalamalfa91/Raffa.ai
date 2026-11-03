import { useCallback, useEffect, useState } from "react";
import type { ApiClient, RenewalNegotiationTodoRow } from "../../api/client";
import { useI18n } from "../../i18n/I18nProvider";
import "./askShortcuts.css";

/**
 * The negotiation points Raffa's agentic engine ranked and saved for this contract (strategy answers in
 * Chiedi write them), superseded ones left out, by rank. A point is never added here, only read back and
 * marked done (the server's state, not optimistic). Contract 360 reads them through this hook so the
 * points it shows and the number it announces come from the same list.
 */
export function useNegotiationPoints(apiClient: ApiClient, tenantId: string, contractId: string) {
  const [todos, setTodos] = useState<readonly RenewalNegotiationTodoRow[]>([]);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  useEffect(() => {
    setTodos([]);
    if (!tenantId || !contractId || typeof apiClient.getRenewalNegotiationTodos !== "function") return;
    let alive = true;
    void apiClient
      .getRenewalNegotiationTodos(tenantId, contractId)
      .then((r) => {
        if (alive && r.ok && r.todos) setTodos(r.todos.filter((x) => x.status !== "Superseded").sort((a, b) => a.rank - b.rank));
      })
      .catch(() => undefined); // no points shown rather than an unhandled rejection
    return () => {
      alive = false;
    };
  }, [apiClient, tenantId, contractId]);

  const tick = useCallback(
    async (row: RenewalNegotiationTodoRow) => {
      setBusyKey(row.pointKey);
      const r = await apiClient.tickRenewalNegotiationTodo(tenantId, contractId, { pointKey: row.pointKey });
      setBusyKey(null);
      if (r.ok && r.todo) {
        const next = r.todo;
        setTodos((list) => list.map((x) => (x.pointKey === next.pointKey ? next : x)));
      }
    },
    [apiClient, tenantId, contractId],
  );

  return { todos, busyKey, tick };
}

/** The points as a list (topic, today → target, why, each tickable). Renders nothing while there are none. */
export default function NegotiationPoints({
  todos,
  busyKey,
  onTick,
}: {
  todos: readonly RenewalNegotiationTodoRow[];
  busyKey: string | null;
  onTick: (row: RenewalNegotiationTodoRow) => void;
}) {
  const { t } = useI18n();
  if (todos.length === 0) return null;

  return (
    <section className="neg-points" data-parity-hide aria-label={t("Negotiation points from Raffa")}>
      <h3 className="neg-points-title">{t("Negotiation points from Raffa")}</h3>
      <ol className="neg-points-list">
        {todos.map((row) => (
          <li key={row.pointKey} className={row.status === "Done" ? "neg-point is-done" : "neg-point"}>
            <div className="neg-point-head">
              <strong>{row.topic}</strong>
              {row.status === "Done" ? (
                <span className="neg-point-done">{t("Done⟦point⟧")}</span>
              ) : (
                <button type="button" className="btn btn-secondary neg-point-tick" disabled={busyKey === row.pointKey} onClick={() => onTick(row)}>
                  {t("Mark done")}
                </button>
              )}
            </div>
            <div className="neg-point-line">
              {row.current ? <span>{row.current}</span> : null}
              {row.current && row.target ? <span aria-hidden="true"> → </span> : null}
              {row.target ? <span className="neg-point-target">{row.target}</span> : null}
            </div>
            {row.rationale ? <div className="neg-point-why">{row.rationale}</div> : null}
          </li>
        ))}
      </ol>
    </section>
  );
}
