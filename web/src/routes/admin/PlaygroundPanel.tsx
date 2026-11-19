import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import type {
  ApiClient,
  PlaygroundBaselineBody,
  PlaygroundHistoryTurn,
  PlaygroundOverrides,
  PlaygroundRunBody,
  PlaygroundSandboxBody,
  PlaygroundSandboxKind,
  PlaygroundVariantBody,
  PlaygroundWorkflow,
  PlaygroundWorkflowInput,
} from "../../api/client";
import PlaygroundHistory from "./PlaygroundHistory";
import { Inspector, formatDuration } from "./TraceInspector";
import VariantEditor from "./VariantEditor";
import WorkflowInputs from "./WorkflowInputs";
import WorkflowResult, { workflowLabel } from "./WorkflowResult";

export { formatCost, prettyPayload, formatDuration } from "./TraceInspector";

const MAX_LANES = 3;
const BASELINE = "baseline";

interface Turn {
  id: number;
  question: string;
  run: PlaygroundRunBody | null;
  error: string | null;
}

type View = "compare" | "variants" | "history";
const VIEWS: ReadonlyArray<{ id: View; label: string }> = [
  { id: "compare", label: "Compare" },
  { id: "variants", label: "Variants" },
  { id: "history", label: "History" },
];

const WORKFLOWS: PlaygroundWorkflow[] = ["ask", "council", "drafting", "research", "extraction"];

const laneKey = (variantId: string | null): string => variantId ?? BASELINE;

/** The line a non-chat run shows in its lane. */
function summaryOf(workflow: PlaygroundWorkflow, input: PlaygroundWorkflowInput): string {
  switch (workflow) {
    case "council":
    case "drafting":
      return `${input.supplier ?? ""}${input.goal ? ` · ${input.goal}` : ""}`;
    case "research":
      return input.query ?? "";
    default:
      return input.fileName ?? `${(input.text ?? "").slice(0, 60)}…`;
  }
}

function historyOf(turns: Turn[]): PlaygroundHistoryTurn[] {
  return turns.flatMap((t): PlaygroundHistoryTurn[] =>
    t.run?.reply ? [{ role: "you", markdown: t.question }, { role: "raffa", markdown: t.run.reply.answerMarkdown }] : [],
  );
}

function newId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

/** What a variant changes, in a few words, for the lane header. */
function describe(overrides: PlaygroundOverrides): string {
  const parts: string[] = [];
  if (Object.keys(overrides.prompts ?? {}).length > 0) parts.push("prompt");
  const models = Object.keys(overrides.models ?? {});
  if (models.length > 0) parts.push(`model (${models.join(", ")})`);
  if ((overrides.disabledTools ?? []).length > 0) parts.push(`${overrides.disabledTools.length} tool(s) off`);
  if (overrides.maxToolCalls) parts.push(`max ${overrides.maxToolCalls} calls`);
  return parts.length > 0 ? parts.join(" · ") : "no changes";
}

/**
 * Playground (Super Admin only; tab of `/admin`, also `/admin/playground`): try Ask Raffa on one of the
 * synthetic sandbox workspaces, change what the agent is made of (prompt, models, tools) in a variant, send
 * the same question to up to three variants side by side and see, for each answer, everything the agent did.
 * Variants exist only for playground runs: production changes only through a reviewed pull request.
 */
export default function PlaygroundPanel({ apiClient }: { apiClient: ApiClient }) {
  const [sandboxes, setSandboxes] = useState<PlaygroundSandboxBody[] | null>(null);
  const [baseline, setBaseline] = useState<PlaygroundBaselineBody | null>(null);
  const [variants, setVariants] = useState<PlaygroundVariantBody[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [view, setView] = useState<View>("compare");
  const [sandbox, setSandbox] = useState<PlaygroundSandboxKind>("seeded");
  const [workflow, setWorkflow] = useState<PlaygroundWorkflow>("ask");
  const [lanes, setLanes] = useState<Array<string | null>>([null]);
  const [turns, setTurns] = useState<Record<string, Turn[]>>({});
  const [selected, setSelected] = useState<{ lane: string; turnId: number } | null>(null);
  const [question, setQuestion] = useState("");
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState<PlaygroundVariantBody | "new" | null>(null);

  const reloadVariants = useCallback(async () => {
    const result = await apiClient.listPlaygroundVariants?.();
    if (result?.ok && result.body) setVariants(result.body.variants);
  }, [apiClient]);

  useEffect(() => {
    let live = true;
    void (async () => {
      if (!apiClient.listPlaygroundSandboxes) {
        setLoadError("This build has no playground.");
        return;
      }
      const result = await apiClient.listPlaygroundSandboxes();
      if (!live) return;
      if (!result.ok || !result.body) {
        setLoadError(result.statusCode === 404 ? "You are not a Raffa platform admin." : (result.error ?? "Could not load."));
        return;
      }
      setSandboxes(result.body.sandboxes);
      const [base, list] = await Promise.all([apiClient.getPlaygroundBaseline?.(), apiClient.listPlaygroundVariants?.()]);
      if (!live) return;
      if (base?.ok && base.body) setBaseline(base.body);
      if (list?.ok && list.body) setVariants(list.body.variants);
    })();
    return () => {
      live = false;
    };
  }, [apiClient]);

  const variantById = useMemo(() => new Map(variants.map((v) => [v.id, v])), [variants]);
  const laneName = (id: string | null): string => (id === null ? "Baseline (production)" : (variantById.get(id)?.name ?? "Variant"));

  const resetConversation = () => {
    setTurns({});
    setSelected(null);
  };

  const toggleLane = (id: string | null) => {
    setLanes((current) => {
      const on = current.includes(id);
      if (on) return current.length === 1 ? current : current.filter((l) => l !== id);
      return current.length >= MAX_LANES ? current : [...current, id];
    });
    resetConversation();
  };

  const changeSandbox = (kind: PlaygroundSandboxKind) => {
    setSandbox(kind);
    resetConversation();
  };

  const run = async (text: string, input?: PlaygroundWorkflowInput) => {
    if (busy || !apiClient.runPlayground) return;
    const id = Math.max(0, ...Object.values(turns).flatMap((t) => t.map((x) => x.id))) + 1;
    const comparisonId = newId();
    setBusy(true);
    setTurns((current) => {
      const next = { ...current };
      for (const lane of lanes) next[laneKey(lane)] = [...(current[laneKey(lane)] ?? []), { id, question: text, run: null, error: null }];
      return next;
    });
    setSelected({ lane: laneKey(lanes[0]), turnId: id });
    await Promise.all(
      lanes.map(async (lane) => {
        const key = laneKey(lane);
        const result = await apiClient.runPlayground!({
          sandbox,
          question: text,
          history: workflow === "ask" ? historyOf((turns[key] ?? []).filter((t) => t.id < id)) : [],
          ...(lane ? { variantId: lane } : {}),
          ...(workflow === "ask" ? {} : { workflow, input }),
          comparisonId,
        });
        const body = result.ok ? result.body : null;
        setTurns((current) => ({
          ...current,
          [key]: (current[key] ?? []).map((t) =>
            t.id === id ? { ...t, run: body, error: body ? body.error : (result.error ?? "The run failed.") } : t,
          ),
        }));
      }),
    );
    setBusy(false);
  };

  const send = async (event: FormEvent) => {
    event.preventDefault();
    const text = question.trim();
    if (!text) return;
    setQuestion("");
    await run(text);
  };

  const changeWorkflow = (next: PlaygroundWorkflow) => {
    setWorkflow(next);
    resetConversation();
  };

  const saveVariant = async (variant: { name: string; description: string | null; overrides: Partial<PlaygroundOverrides> }) => {
    const id = editing !== null && editing !== "new" ? editing.id : null;
    const result = await apiClient.savePlaygroundVariant?.(id, variant);
    if (!result?.ok) return result?.error ?? "The variant was not saved.";
    await reloadVariants();
    setEditing(null);
    return null;
  };

  const archive = async (variant: PlaygroundVariantBody) => {
    if (!window.confirm(`Archive "${variant.name}"? Runs that used it keep their own copy.`)) return;
    await apiClient.archivePlaygroundVariant?.(variant.id);
    setLanes((current) => (current.includes(variant.id) ? current.filter((l) => l !== variant.id).concat(current.length === 1 ? [null] : []) : current));
    resetConversation();
    await reloadVariants();
  };

  if (loadError) {
    return (
      <p role="alert" className="admin-error">
        {loadError}
      </p>
    );
  }
  if (!sandboxes) return <p className="hint">Loading…</p>;

  const selectedRun = selected ? (turns[selected.lane] ?? []).find((t) => t.id === selected.turnId)?.run ?? null : null;
  const selectedLaneName = selected ? laneName(selected.lane === BASELINE ? null : selected.lane) : "";

  return (
    <section className="pg-root" aria-label="Playground">
      <nav className="pg-views" aria-label="Playground sections">
        {VIEWS.map((v) => (
          <button
            key={v.id}
            type="button"
            className={v.id === view ? "admin-tab is-active" : "admin-tab"}
            aria-pressed={v.id === view}
            onClick={() => setView(v.id)}
          >
            {v.label}
          </button>
        ))}
      </nav>

      {view === "history" ? <PlaygroundHistory apiClient={apiClient} /> : null}

      {view === "variants" ? (
        <div className="pg-variants">
          {editing !== null && baseline ? (
            <VariantEditor
              key={editing === "new" ? "new" : editing.id}
              baseline={baseline}
              variant={editing === "new" ? null : editing}
              onSave={saveVariant}
              onCancel={() => setEditing(null)}
            />
          ) : (
            <section className="admin-card" aria-label="Variants">
              <div className="pg-actions">
                <button type="button" className="btn btn-primary" disabled={!baseline} onClick={() => setEditing("new")}>
                  New variant
                </button>
              </div>
              {variants.length === 0 ? (
                <p className="hint">No variants yet. The baseline is what production runs; a variant changes a prompt, a model or the tools.</p>
              ) : (
                <table className="table admin-table">
                  <thead>
                    <tr>
                      <th>Name</th>
                      <th>Changes</th>
                      <th>Updated</th>
                      <th>
                        <span className="visually-hidden">Actions</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {variants.map((v) => (
                      <tr key={v.id}>
                        <td>
                          {v.name}
                          {v.description ? <div className="hint">{v.description}</div> : null}
                        </td>
                        <td>{describe(v.overrides)}</td>
                        <td>{new Date(v.updatedAt).toLocaleDateString()}</td>
                        <td>
                          <button type="button" className="btn" onClick={() => setEditing(v)}>
                            Edit
                          </button>
                          <button type="button" className="btn" onClick={() => void archive(v)}>
                            Archive
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>
          )}
        </div>
      ) : null}

      {view === "compare" ? (
        <div className="pg">
          <aside className="pg-side" aria-label="Sandbox workspace">
            <h2 className="pg-heading">Workflow</h2>
            <select aria-label="Workflow" className="input" value={workflow} disabled={busy} onChange={(e) => changeWorkflow(e.target.value as PlaygroundWorkflow)}>
              {WORKFLOWS.map((w) => (
                <option key={w} value={w}>
                  {workflowLabel(w)}
                </option>
              ))}
            </select>
            <h2 className="pg-heading">Sandbox workspace</h2>
            <p className="hint">Synthetic data only. Customer workspaces are never available here.</p>
            {sandboxes.map((s) => (
              <label key={s.kind} className={s.kind === sandbox ? "pg-sandbox is-active" : "pg-sandbox"}>
                <input
                  type="radio"
                  name="pg-sandbox"
                  value={s.kind}
                  checked={s.kind === sandbox}
                  disabled={busy}
                  onChange={() => changeSandbox(s.kind)}
                />
                <span className="pg-sandbox-name">
                  {s.kind} <span className="pg-chip">{s.contracts} contracts</span>
                </span>
                <span className="pg-sandbox-desc">{s.description}</span>
              </label>
            ))}

            <h2 className="pg-heading">Compare (up to {MAX_LANES})</h2>
            <fieldset className="pg-lanes" disabled={busy}>
              <legend className="visually-hidden">Variants to compare</legend>
              <label className="pg-lane-pick">
                <input type="checkbox" checked={lanes.includes(null)} onChange={() => toggleLane(null)} /> Baseline (production)
              </label>
              {variants.map((v) => (
                <label key={v.id} className="pg-lane-pick">
                  <input type="checkbox" checked={lanes.includes(v.id)} onChange={() => toggleLane(v.id)} /> {v.name}
                </label>
              ))}
            </fieldset>
            <button type="button" className="btn" disabled={busy || Object.keys(turns).length === 0} onClick={resetConversation}>
              New conversation
            </button>
          </aside>

          <div className="pg-main">
            <div className="pg-lanes-grid" style={{ gridTemplateColumns: `repeat(${lanes.length}, minmax(0, 1fr))` }}>
              {lanes.map((lane) => {
                const key = laneKey(lane);
                const laneTurns = turns[key] ?? [];
                return (
                  <section key={key} className="pg-lane" aria-label={`Lane ${laneName(lane)}`}>
                    <h2 className="pg-heading">{laneName(lane)}</h2>
                    {lane ? <p className="hint">{describe(variantById.get(lane)?.overrides ?? { prompts: {}, models: {}, disabledTools: [], maxToolCalls: null })}</p> : null}
                    <ol className="pg-turns" aria-label={`Conversation ${laneName(lane)}`}>
                      {laneTurns.length === 0 ? (
                        <li className="hint">{workflow === "ask" ? "Ask Raffa something about the sandbox portfolio." : `Run the ${workflowLabel(workflow).toLowerCase()} below.`}</li>
                      ) : null}
                      {laneTurns.map((t) => (
                        <li key={t.id} className={selected?.lane === key && selected.turnId === t.id ? "pg-turn is-selected" : "pg-turn"}>
                          <p className="pg-you">{t.question}</p>
                          {t.run === null && t.error === null ? <p className="hint">Running…</p> : null}
                          {t.error ? (
                            <p role="alert" className="admin-error">
                              {t.error}
                            </p>
                          ) : null}
                          {t.run && workflow !== "ask" ? (
                            <button
                              type="button"
                              className="pg-reply"
                              onClick={() => setSelected({ lane: key, turnId: t.id })}
                              aria-label={`Inspect run ${t.id} of ${laneName(lane)}`}
                            >
                              <span className="pg-reply-meta">
                                <span className="pg-chip">{t.run.status}</span>
                                <span className="pg-chip">{formatDuration(t.run.durationMs)}</span>
                                <span className="pg-chip">{t.run.usage.modelCalls} model calls</span>
                              </span>
                            </button>
                          ) : null}
                          {t.run && workflow !== "ask" && t.run.result ? <WorkflowResult workflow={workflow} result={t.run.result} /> : null}
                          {t.run?.reply ? (
                            <button
                              type="button"
                              className="pg-reply"
                              onClick={() => setSelected({ lane: key, turnId: t.id })}
                              aria-label={`Inspect answer ${t.id} of ${laneName(lane)}`}
                            >
                              <span className="pg-reply-meta">
                                <span className="pg-chip">{t.run.reply.kind}</span>
                                {t.run.reply.provenance.unverified ? <span className="pg-chip pg-chip-warn">unverified</span> : null}
                                <span className="pg-chip">{formatDuration(t.run.durationMs)}</span>
                                <span className="pg-chip">{t.run.usage.toolCalls} tools</span>
                              </span>
                              <span className="pg-reply-text">{t.run.reply.answerMarkdown}</span>
                            </button>
                          ) : null}
                        </li>
                      ))}
                    </ol>
                  </section>
                );
              })}
            </div>

            {workflow === "ask" ? (
              <form onSubmit={(e) => void send(e)} className="pg-composer">
                <label htmlFor="pg-question" className="visually-hidden">
                  Question
                </label>
                <textarea
                  id="pg-question"
                  className="input"
                  rows={3}
                  value={question}
                  placeholder="e.g. Quando scade il contratto Salesforce?"
                  onChange={(e) => setQuestion(e.target.value)}
                />
                <button type="submit" className="btn btn-primary" disabled={busy || question.trim().length === 0}>
                  {busy ? "Running…" : lanes.length > 1 ? `Send to ${lanes.length} variants` : "Send"}
                </button>
              </form>
            ) : (
              <WorkflowInputs
                key={workflow}
                workflow={workflow}
                busy={busy}
                lanes={lanes.length}
                onRun={(input) => void run(summaryOf(workflow, input), input)}
              />
            )}

            <aside className="pg-inspector" aria-label="Inspector">
              <h2 className="pg-heading">Inspector</h2>
              {selectedRun ? <Inspector run={selectedRun} title={selectedLaneName} /> : <p className="hint">Select a result to see what the agent did.</p>}
            </aside>
          </div>
        </div>
      ) : null}
    </section>
  );
}
