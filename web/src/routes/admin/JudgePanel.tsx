import { useEffect, useMemo, useState } from "react";
import type { ApiClient, PlaygroundBaselineBody, PlaygroundJudgementBody, PlaygroundRubricBody, PlaygroundWorkflow } from "../../api/client";
import JudgementView from "./JudgementView";

/** The answers of one question, one per lane. `name` is the variant's (or "Baseline"): the judge never sees it. */
export interface JudgeTarget {
  runId: string;
  name: string;
}

/**
 * Scores the answers of the selected question with an LLM judge and a rubric. The judge gets the answers anonymised
 * and in a random order; the names are put back only in the result. A previous judgement of the same comparison is
 * shown first when there is one.
 */
export default function JudgePanel({
  apiClient,
  workflow,
  comparisonId,
  targets,
  baseline,
}: {
  apiClient: ApiClient;
  workflow: PlaygroundWorkflow;
  comparisonId: string | null;
  targets: JudgeTarget[];
  baseline: PlaygroundBaselineBody | null;
}) {
  const [rubrics, setRubrics] = useState<PlaygroundRubricBody[]>([]);
  const [rubricId, setRubricId] = useState(`default:${workflow}`);
  const [judgeModel, setJudgeModel] = useState("");
  const [twoPasses, setTwoPasses] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [judgement, setJudgement] = useState<PlaygroundJudgementBody | null>(null);

  const runKey = targets.map((t) => t.runId).join(",");

  useEffect(() => {
    let live = true;
    void (async () => {
      const result = await apiClient.listPlaygroundRubrics?.();
      if (live && result?.ok && result.body) setRubrics(result.body.rubrics);
    })();
    return () => {
      live = false;
    };
  }, [apiClient]);

  useEffect(() => {
    setRubricId(`default:${workflow}`);
  }, [workflow]);

  // A new question starts clean; if this comparison was judged before, show the latest judgement.
  useEffect(() => {
    let live = true;
    setJudgement(null);
    setError(null);
    void (async () => {
      if (!comparisonId) return;
      const result = await apiClient.listPlaygroundJudgements?.({ comparisonId });
      if (live && result?.ok && result.body?.judgements[0]) setJudgement(result.body.judgements[0]);
    })();
    return () => {
      live = false;
    };
  }, [apiClient, comparisonId, runKey]);

  const options = useMemo(() => rubrics.filter((r) => r.workflow === workflow), [rubrics, workflow]);

  const nameOf = (runId: string) => targets.find((t) => t.runId === runId)?.name ?? "Baseline";

  const judge = async () => {
    if (!apiClient.judgePlayground) return;
    setBusy(true);
    setError(null);
    const result = await apiClient.judgePlayground({
      runIds: targets.map((t) => t.runId),
      rubricId,
      ...(judgeModel ? { judgeModelId: judgeModel } : {}),
      passes: twoPasses ? 2 : 1,
    });
    setBusy(false);
    if (result.ok && result.body) setJudgement(result.body);
    else setError(result.error ?? "The judge failed.");
  };

  if (targets.length === 0) return null;

  return (
    <section className="admin-card pg-judge" aria-label="Judge">
      <h2 className="pg-heading">Judge</h2>
      <p className="hint">
        An LLM scores {targets.length > 1 ? `these ${targets.length} answers` : "this answer"} against a rubric. It sees them anonymised, in a random
        order, with the evidence each one was built from. Treat the vote as a signal: read the answers too.
      </p>
      <div className="pg-judge-controls">
        <div className="pg-field">
          <label htmlFor="pg-judge-rubric">Rubric</label>
          <select id="pg-judge-rubric" className="input" value={rubricId} onChange={(e) => setRubricId(e.target.value)} disabled={busy}>
            {options.length === 0 ? <option value={`default:${workflow}`}>Default</option> : null}
            {options.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
                {r.isDefault ? " (built-in)" : ""}
              </option>
            ))}
          </select>
        </div>
        <div className="pg-field">
          <label htmlFor="pg-judge-model">Judge model</label>
          <select id="pg-judge-model" className="input" value={judgeModel} onChange={(e) => setJudgeModel(e.target.value)} disabled={busy}>
            <option value="">Default (analyst role)</option>
            {(baseline?.allowedModelIds ?? []).map((id) => (
              <option key={id} value={id}>
                {id}
              </option>
            ))}
          </select>
        </div>
        <label className="pg-lane-pick">
          <input type="checkbox" checked={twoPasses} onChange={(e) => setTwoPasses(e.target.checked)} disabled={busy || targets.length < 2} /> Check order bias (2
          passes, costs twice)
        </label>
        <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void judge()}>
          {busy ? "Judging…" : judgement ? "Judge again" : "Judge"}
        </button>
      </div>
      {error ? (
        <p role="alert" className="admin-error">
          {error}
        </p>
      ) : null}
      {judgement ? <JudgementView judgement={withNames(judgement, nameOf)} /> : null}
    </section>
  );
}

/** The server names each answer after its variant; keep that, and fall back to this session's lane names. */
function withNames(judgement: PlaygroundJudgementBody, nameOf: (runId: string) => string): PlaygroundJudgementBody {
  return { ...judgement, answers: judgement.answers.map((a) => ({ ...a, variantName: a.variantName || nameOf(a.runId) })) };
}
