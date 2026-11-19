import { useEffect, useState } from "react";
import type { ApiClient, PlaygroundJudgementBody, PlaygroundRunSummaryBody, PlaygroundStoredRunBody } from "../../api/client";
import { Inspector, formatCost, formatDuration } from "./TraceInspector";
import JudgementView from "./JudgementView";
import WorkflowResult, { workflowLabel } from "./WorkflowResult";

/** The last runs, newest first; opening one shows its stored answer and trace. */
export default function PlaygroundHistory({ apiClient }: { apiClient: ApiClient }) {
  const [runs, setRuns] = useState<PlaygroundRunSummaryBody[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState<PlaygroundStoredRunBody | null>(null);
  const [judgements, setJudgements] = useState<PlaygroundJudgementBody[]>([]);

  useEffect(() => {
    let live = true;
    void (async () => {
      const result = await apiClient.listPlaygroundRuns?.(30);
      if (!live) return;
      if (result?.ok && result.body) setRuns(result.body.runs);
      else setError(result?.error ?? "Could not load the history.");
    })();
    return () => {
      live = false;
    };
  }, [apiClient]);

  const show = async (id: string) => {
    const result = await apiClient.getPlaygroundRun?.(id);
    if (result?.ok && result.body) {
      setOpen(result.body);
      const judged = await apiClient.listPlaygroundJudgements?.({ runId: id });
      setJudgements(judged?.ok && judged.body ? judged.body.judgements : []);
    } else setError(result?.error ?? "Could not open the run.");
  };

  if (error) {
    return (
      <p role="alert" className="admin-error">
        {error}
      </p>
    );
  }
  if (!runs) return <p className="hint">Loading…</p>;

  return (
    <div className="pg-history">
      <section className="admin-card" aria-label="Run history">
        {runs.length === 0 ? (
          <p className="hint">No runs yet.</p>
        ) : (
          <table className="table admin-table">
            <thead>
              <tr>
                <th>When</th>
                <th>Workflow</th>
                <th>Sandbox</th>
                <th>Variant</th>
                <th>Question</th>
                <th>Duration</th>
                <th>Cost</th>
                <th>
                  <span className="visually-hidden">Open</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {runs.map((r) => (
                <tr key={r.id}>
                  <td>{new Date(r.createdAt).toLocaleString()}</td>
                  <td>{workflowLabel(r.workflow)}</td>
                  <td>{r.sandbox}</td>
                  <td>{r.variantName ?? "Baseline"}</td>
                  <td>{r.input.question ?? ""}</td>
                  <td>{formatDuration(r.durationMs)}</td>
                  <td>{formatCost(r.estimatedCostUsd)}</td>
                  <td>
                    <button type="button" className="btn" onClick={() => void show(r.id)}>
                      Open
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      {open ? (
        <section className="admin-card" aria-label="Stored run">
          <p className="pg-you">{open.input.question}</p>
          {open.output.reply ? <p className="pg-reply-text">{open.output.reply.answerMarkdown}</p> : null}
          {open.output.result ? <WorkflowResult workflow={open.workflow ?? "ask"} result={open.output.result} /> : null}
          {open.output.error ? (
            <p role="alert" className="admin-error">
              {open.output.error}
            </p>
          ) : null}
          <Inspector run={{ durationMs: open.durationMs, trace: open.trace, usage: open.output.usage }} title={open.variantName ?? "Baseline"} />
          {judgements.length > 0 ? (
            <details aria-label="Judgements of this run">
              <summary>Judgements of this run ({judgements.length})</summary>
              {judgements.map((j) => (
                <JudgementView key={j.judgementId} judgement={j} />
              ))}
            </details>
          ) : null}
        </section>
      ) : null}
    </div>
  );
}
