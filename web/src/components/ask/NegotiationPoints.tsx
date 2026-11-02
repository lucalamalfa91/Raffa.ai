import { useEffect, useState } from "react";
import type { ApiClient, RenewalNegotiationTodoRow } from "../../api/client";
import { useI18n } from "../../i18n/I18nProvider";
import "./askShortcuts.css";

/**
 * The negotiation points Raffa's agentic engine ranked and saved for this contract (strategy answers in
 * Chiedi write them): topic, today → target, why, each tickable. Nothing renders until Raffa has written
 * some; a point is never added here, only read back and marked done (the server's state, not optimistic).
 */
export default function NegotiationPoints({ apiClient, tenantId, contractId }: { apiClient: ApiClient; tenantId: string; contractId: string }) {
  const { t } = useI18n();
  const [todos, setTodos] = useState<readonly RenewalNegotiationTodoRow[]>([]);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  useEffect(() => {
    if (!tenantId || !contractId || typeof apiClient.getRenewalNegotiationTodos !== "function") return;
    let alive = true;
    void apiClient.getRenewalNegotiationTodos(tenantId, contractId).then((r) => {
      if (alive && r.ok && r.todos) setTodos(r.todos.filter((x) => x.status !== "Superseded").sort((a, b) => a.rank - b.rank));
    });
    return () => {
      alive = false;
    };
  }, [apiClient, tenantId, contractId]);

  if (todos.length === 0) return null;

  const tick = async (row: RenewalNegotiationTodoRow) => {
    setBusyKey(row.pointKey);
    const r = await apiClient.tickRenewalNegotiationTodo(tenantId, contractId, { pointKey: row.pointKey });
    setBusyKey(null);
    if (r.ok && r.todo) {
      const next = r.todo;
      setTodos((list) => list.map((x) => (x.pointKey === next.pointKey ? next : x)));
    }
  };

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
                <button type="button" className="btn btn-secondary neg-point-tick" disabled={busyKey === row.pointKey} onClick={() => void tick(row)}>
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
