import type { PlaygroundRunBody, PlaygroundTraceEvent, PlaygroundUsage } from "../../api/client";

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

/** What the agent did for one answer: totals, then every step with its raw request and result. */
export function Inspector({ run, title }: { run: Pick<PlaygroundRunBody, "durationMs" | "trace"> & { usage: PlaygroundUsage }; title?: string }) {
  const { usage } = run;
  return (
    <>
      {title ? <p className="pg-inspector-title">{title}</p> : null}
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
      {usage.unpricedModels.length > 0 ? <p className="hint">No price configured for: {usage.unpricedModels.join(", ")}.</p> : null}
      <TraceList events={run.trace} />
    </>
  );
}

/** Every step of a trace, collapsed, with its raw request and result inside. */
export function TraceList({ events }: { events: PlaygroundTraceEvent[] }) {
  return (
    <ol className="pg-trace" aria-label="Trace">
      {events.map((e) => (
        <TraceStep key={e.seq} event={e} />
      ))}
    </ol>
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
