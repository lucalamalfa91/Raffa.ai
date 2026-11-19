import type { PlaygroundJudgementBody } from "../../api/client";
import { TraceList, formatCost } from "./TraceInspector";

/**
 * One judgement: the scores per answer and criterion, the weighted total, who the judge preferred, and what it said.
 * The vote is a signal, never a verdict: when the two passes (answers in opposite orders) disagree the vote is flagged
 * as unreliable, and the judge's own calls are one click away.
 */
export default function JudgementView({ judgement }: { judgement: PlaygroundJudgementBody }) {
  const { answers, rubric } = judgement;
  const best = judgement.preferredRunId ? answers.find((a) => a.runId === judgement.preferredRunId) : undefined;
  const scoreOf = (runId: string, criterionId: string) => answers.find((a) => a.runId === runId)?.scores.find((s) => s.criterionId === criterionId);

  return (
    <section className="pg-judgement" aria-label="Judgement">
      <p className="pg-reply-meta">
        {answers.length > 1 ? (
          judgement.tie ? (
            <span className="pg-chip">Tie</span>
          ) : best ? (
            <span className="pg-chip pg-chip-best">Preferred: {best.variantName}</span>
          ) : null
        ) : null}
        <span className="pg-chip">{rubric.name}</span>
        <span className="pg-chip">{judgement.judgeModel ?? "default judge"}</span>
        <span className="pg-chip">{judgement.passes === 2 ? "2 passes" : "1 pass"}</span>
        <span className="pg-chip">{formatCost(judgement.usage.estimatedCostUsd)}</span>
      </p>

      {judgement.passes === 2 && !judgement.passesAgreed ? (
        <p role="alert" className="admin-error">
          The judge preferred a different answer when the order was reversed. Its vote depends on the order: read the answers yourself.
        </p>
      ) : null}
      {judgement.failures.length > 0 ? <p className="pg-note">Judge problems: {judgement.failures.join("; ")}</p> : null}

      {answers.length > 0 ? (
        <table className="table pg-scores" aria-label="Scores">
          <thead>
            <tr>
              <th>Criterion</th>
              {answers.map((a) => (
                <th key={a.runId}>{a.variantName}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rubric.criteria.map((c) => (
              <tr key={c.id}>
                <th scope="row" title={c.description}>
                  {c.label} <span className="hint">×{c.weight}</span>
                </th>
                {answers.map((a) => {
                  const cell = scoreOf(a.runId, c.id);
                  return (
                    <td key={a.runId} title={cell?.rationale ?? undefined}>
                      {cell ? cell.score.toFixed(cell.score % 1 === 0 ? 0 : 1) : "—"}
                    </td>
                  );
                })}
              </tr>
            ))}
            <tr className="pg-total">
              <th scope="row">Weighted (1–5)</th>
              {answers.map((a) => (
                <td key={a.runId}>
                  <strong>{a.weighted.toFixed(2)}</strong>
                </td>
              ))}
            </tr>
          </tbody>
        </table>
      ) : null}

      {answers.map((a) => (
        <details key={a.runId} className="pg-why">
          <summary>
            Why: {a.variantName}
            {a.summary ? ` — ${a.summary}` : ""}
          </summary>
          <ul className="pg-rationales">
            {a.scores.map((s) => (
              <li key={s.criterionId}>
                <strong>{rubric.criteria.find((c) => c.id === s.criterionId)?.label ?? s.criterionId}</strong> {s.score}: {s.rationale}
              </li>
            ))}
          </ul>
        </details>
      ))}

      {judgement.passDetails.map((p, i) => (
        <p key={i} className="hint">
          Pass {i + 1}: {p.reasoning}
        </p>
      ))}

      {judgement.trace && judgement.trace.length > 0 ? (
        <details>
          <summary>What the judge was sent and answered</summary>
          <TraceList events={judgement.trace} />
        </details>
      ) : null}
    </section>
  );
}
