import { Link } from "react-router-dom";
import type { TrackedRenewalAction } from "../../renewals/renewalActionStore";
import { RENEWAL_ACTION_KINDS, getRenewalActionPlan, type RenewalActionKind } from "../../renewals/renewalPipelineViewModel";
import { formatTrackerMeta, type AnswersBand as AnswersBandModel, type NegotiationStep } from "./contract360ViewModel";

export interface AnswersBandProps {
  answers: AnswersBandModel;
  /** This session's recorded action for this contract, or `null` while it is still open. */
  tracked: TrackedRenewalAction | null;
  steps: readonly NegotiationStep[];
  stepsDone: readonly boolean[];
  actionPending: RenewalActionKind | "undo" | null;
  actionError: string | null;
  onAction: (kind: RenewalActionKind) => void;
  onUndo: () => void;
  onToggleStep: (index: number) => void;
}

/**
 * The three answers (`contigo-v2/markup.html` "CONTRACT 360 — three answers": *Where you can save*
 * · *When you must move* · *What to do*), the last cell carrying either the two actions
 * (`c360Open`) or, once acted, the negotiation tracker (`c360Acted`): status · owner, "target … ·
 * close by …", the four-step checklist, "Track it in Renewals →" and "Undo".
 *
 * **Facts vs AI (ADR-019).** "What to do" is the Renewals module's deterministic recommendation --
 * the only recommendation text on the screen -- and it never shares a cell with the dated facts to
 * its left.
 */
export default function AnswersBand({
  answers,
  tracked,
  steps,
  stepsDone,
  actionPending,
  actionError,
  onAction,
  onUndo,
  onToggleStep,
}: AnswersBandProps) {
  const { save, move, act } = answers;

  return (
    <section className="contract360-answers" aria-label="Answers">
      <div className="contract360-answer">
        <p className="contract360-answer-label">Where you can save</p>
        <p className="contract360-answer-value">{save.estimate}</p>
        <p className="contract360-answer-detail">{save.lever}</p>
      </div>

      <div className="contract360-answer">
        <p className="contract360-answer-label">When you must move</p>
        <p className={`contract360-answer-value${move.isUrgent ? " deadline-critical" : ""}`}>{move.deadline}</p>
        <p className="contract360-answer-detail">
          {move.cancelDays !== null && move.cancelDays >= 0 ? (
            <>
              in <strong>{move.cancelDays} day{move.cancelDays === 1 ? "" : "s"}</strong>
              {move.detail.slice(`in ${move.cancelDays} day${move.cancelDays === 1 ? "" : "s"}`.length)}
            </>
          ) : (
            move.detail
          )}
        </p>
      </div>

      <div className="contract360-answer contract360-answer-act">
        <p className="contract360-answer-label contract360-answer-label-accent">What to do</p>
        <p className="contract360-answer-action">{act.statement}</p>
        <p className="contract360-answer-detail">{act.rationale}</p>

        {tracked === null ? (
          <div className="contract360-answer-buttons">
            {RENEWAL_ACTION_KINDS.map((kind) => {
              const plan = getRenewalActionPlan(kind);
              return (
                <button
                  key={kind}
                  type="button"
                  className={kind === "negotiate" ? "btn btn-primary" : "btn btn-ghost contract360-assign"}
                  disabled={actionPending !== null}
                  onClick={() => onAction(kind)}
                >
                  {actionPending === kind ? "Saving…" : plan.buttonLabel}
                </button>
              );
            })}
          </div>
        ) : (
          <div className="contract360-tracker" role="status">
            <div className="contract360-tracker-head">
              <span>
                <strong>{tracked.action}</strong> · owner {tracked.owner}
              </span>
              <span className="contract360-tracker-meta">{formatTrackerMeta(save, move)}</span>
            </div>
            <div className="contract360-tracker-steps">
              {steps.map((step, index) => {
                const done = stepsDone[index] === true;
                return (
                  <button
                    key={step.label}
                    type="button"
                    className={`contract360-step${done ? " is-done" : ""}`}
                    aria-pressed={done}
                    onClick={() => onToggleStep(index)}
                  >
                    <span className="contract360-step-box" aria-hidden="true" />
                    <span className="contract360-step-label">{step.label}</span>
                    <span className="contract360-step-due">{step.due}</span>
                  </button>
                );
              })}
            </div>
            <div className="contract360-tracker-links">
              <Link to="/renewals" className="btn btn-ghost contract360-tracker-link">
                Track it in Renewals →
              </Link>
              <button type="button" className="btn btn-ghost contract360-tracker-link contract360-undo" disabled={actionPending !== null} onClick={onUndo}>
                {actionPending === "undo" ? "Undoing…" : "Undo"}
              </button>
            </div>
          </div>
        )}

        {actionError !== null && (
          <p className="hint" role="alert">
            {actionError}
          </p>
        )}
      </div>
    </section>
  );
}
