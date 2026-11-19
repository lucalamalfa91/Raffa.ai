import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import type {
  PlaygroundHistoryTurn,
  PlaygroundRunBody,
  PlaygroundSandboxBody,
  PlaygroundSandboxKind,
  PlaygroundTraceEvent,
  ApiClient,
} from "../../api/client";

interface Turn {
  id: number;
  question: string;
  run: PlaygroundRunBody | null;
  error: string | null;
}

const KIND_LABEL: Record<PlaygroundTraceEvent["kind"], string> = {
  model_call: "Model",
  tool_call: "Tool",
  guard: "Guard",
  note: "Note",
};

export const formatDuration = (ms: number): string => (ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(1)} s`);

/** `null` is "no price configured for a model used": shown as n/a, never as zero. */
export const formatCost = (usd: number | null): string => (usd === null ? "n/a" : `$${usd < 0.01 ? usd.toFixed(4) : usd.toFixed(2)}`);

/** Raw model and tool payloads are JSON as often as not: pretty-print what parses, show the rest as is. */
export function prettyPayload(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

function historyOf(turns: Turn[]): PlaygroundHistoryTurn[] {
  return turns.flatMap((t): PlaygroundHistoryTurn[] =>
    t.run?.reply ? [{ role: "you", markdown: t.question }, { role: "raffa", markdown: t.run.reply.answerMarkdown }] : [],
  );
}

/**
 * Playground (Super Admin only; tab of `/admin`, also `/admin/playground`): talk to Ask Raffa on one of the
 * synthetic sandbox workspaces and see, for each answer, everything the agent did to produce it: every model
 * call with the exact request and raw answer, every tool call with its arguments and result, the guards that
 * fired, tokens and estimated cost. It never touches a customer workspace.
 */
export default function PlaygroundPanel({ apiClient }: { apiClient: ApiClient }) {
  const [sandboxes, setSandboxes] = useState<PlaygroundSandboxBody[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [sandbox, setSandbox] = useState<PlaygroundSandboxKind>("seeded");
  const [turns, setTurns] = useState<Turn[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [question, setQuestion] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let live = true;
    void (async () => {
      if (!apiClient.listPlaygroundSandboxes) {
        setLoadError("This build has no playground.");
        return;
      }
      const result = await apiClient.listPlaygroundSandboxes();
      if (!live) return;
      if (result.ok && result.body) setSandboxes(result.body.sandboxes);
      else setLoadError(result.statusCode === 404 ? "You are not a Raffa platform admin." : (result.error ?? "Could not load."));
    })();
    return () => {
      live = false;
    };
  }, [apiClient]);

  const send = useCallback(
    async (event: FormEvent) => {
      event.preventDefault();
      const text = question.trim();
      if (!text || busy || !apiClient.runPlayground) return;
      const id = turns.length + 1;
      setBusy(true);
      setQuestion("");
      setTurns((current) => [...current, { id, question: text, run: null, error: null }]);
      setSelectedId(id);
      const result = await apiClient.runPlayground({ sandbox, question: text, history: historyOf(turns) });
      const run = result.ok ? result.body : null;
      setTurns((current) =>
        current.map((t) =>
          t.id === id ? { ...t, run, error: run ? run.error : (result.error ?? "The run failed.") } : t,
        ),
      );
      setBusy(false);
    },
    [apiClient, busy, question, sandbox, turns],
  );

  const selected = useMemo(() => turns.find((t) => t.id === selectedId) ?? null, [turns, selectedId]);

  const changeSandbox = (kind: PlaygroundSandboxKind) => {
    setSandbox(kind);
    setTurns([]);
    setSelectedId(null);
  };

  if (loadError) {
    return (
      <p role="alert" className="admin-error">
        {loadError}
      </p>
    );
  }
  if (!sandboxes) return <p className="hint">Loading…</p>;

  return (
    <section className="pg" aria-label="Playground">
      <aside className="pg-side" aria-label="Sandbox workspace">
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
        <button type="button" className="btn" disabled={busy || turns.length === 0} onClick={() => changeSandbox(sandbox)}>
          New conversation
        </button>
      </aside>

      <div className="pg-chat">
        <h2 className="pg-heading">Conversation</h2>
        <ol className="pg-turns" aria-label="Conversation">
          {turns.length === 0 ? <li className="hint">Ask Raffa something about the sandbox portfolio.</li> : null}
          {turns.map((t) => (
            <li key={t.id} className={t.id === selectedId ? "pg-turn is-selected" : "pg-turn"}>
              <p className="pg-you">{t.question}</p>
              {t.run === null && t.error === null ? <p className="hint">Running…</p> : null}
              {t.error ? (
                <p role="alert" className="admin-error">
                  {t.error}
                </p>
              ) : null}
              {t.run?.reply ? (
                <button type="button" className="pg-reply" onClick={() => setSelectedId(t.id)} aria-label={`Inspect answer ${t.id}`}>
                  <span className="pg-reply-meta">
                    <span className="pg-chip">{t.run.reply.kind}</span>
                    {t.run.reply.provenance.unverified ? <span className="pg-chip pg-chip-warn">unverified</span> : null}
                    <span className="pg-chip">{formatDuration(t.run.durationMs)}</span>
                  </span>
                  <span className="pg-reply-text">{t.run.reply.answerMarkdown}</span>
                </button>
              ) : null}
            </li>
          ))}
        </ol>
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
            {busy ? "Running…" : "Send"}
          </button>
        </form>
      </div>

      <aside className="pg-inspector" aria-label="Inspector">
        <h2 className="pg-heading">Inspector</h2>
        {selected?.run ? <Inspector run={selected.run} /> : <p className="hint">Select an answer to see what the agent did.</p>}
      </aside>
    </section>
  );
}

function Inspector({ run }: { run: PlaygroundRunBody }) {
  const { usage } = run;
  return (
    <>
      <dl className="pg-usage" aria-label="Run summary">
        <div>
          <dt>Duration</dt>
          <dd>{formatDuration(run.durationMs)}</dd>
        </div>
        <div>
          <dt>Model calls</dt>
          <dd>{usage.modelCalls}</dd>
        </div>
        <div>
          <dt>Tool calls</dt>
          <dd>{usage.toolCalls}</dd>
        </div>
        <div>
          <dt>Tokens in / out</dt>
          <dd>
            {usage.promptTokens} / {usage.completionTokens}
          </dd>
        </div>
        <div>
          <dt>Estimated cost</dt>
          <dd>{formatCost(usage.estimatedCostUsd)}</dd>
        </div>
      </dl>
      {usage.unpricedModels.length > 0 ? (
        <p className="hint">No price configured for: {usage.unpricedModels.join(", ")}.</p>
      ) : null}
      <ol className="pg-trace" aria-label="Trace">
        {run.trace.map((e) => (
          <TraceStep key={e.seq} event={e} />
        ))}
      </ol>
    </>
  );
}

function TraceStep({ event }: { event: PlaygroundTraceEvent }) {
  const hasDetail = Boolean(event.input || event.output);
  return (
    <li className={event.ok ? "pg-step" : "pg-step is-failed"}>
      <details>
        <summary>
          <span className={`pg-chip pg-kind-${event.kind}`}>{KIND_LABEL[event.kind]}</span> <strong>{event.name}</strong>
          <span className="pg-step-meta">
            {event.model ? ` · ${event.model}` : ""}
            {event.promptTokens !== null || event.completionTokens !== null
              ? ` · ${event.promptTokens ?? 0}/${event.completionTokens ?? 0} tok`
              : ""}
            {event.durationMs > 0 ? ` · ${formatDuration(event.durationMs)}` : ""}
            {event.ok ? "" : " · failed"}
          </span>
        </summary>
        {event.note ? <p className="pg-note">{event.note}</p> : null}
        {hasDetail ? (
          <>
            {event.input ? (
              <>
                <h3 className="pg-sub">{event.kind === "model_call" ? "Request sent to the model" : "Arguments"}</h3>
                <pre className="pg-pre">{prettyPayload(event.input)}</pre>
              </>
            ) : null}
            {event.output ? (
              <>
                <h3 className="pg-sub">{event.kind === "model_call" ? "Raw answer" : "Result"}</h3>
                <pre className="pg-pre">{prettyPayload(event.output)}</pre>
              </>
            ) : null}
          </>
        ) : null}
      </details>
    </li>
  );
}
