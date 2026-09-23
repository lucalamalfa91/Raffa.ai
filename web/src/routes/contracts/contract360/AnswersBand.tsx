import { useState } from "react";
import { Link } from "react-router-dom";
import type { Contract360HeaderBody, RenewalActionRow } from "../../../api/client";
import InfoTip, { CopyTip } from "../../../components/InfoTip";
import { TIPS } from "../../../components/infoTipCopy";
import { getRenewalActionPlan, type RenewalActionKind } from "../../renewals/renewalPipelineViewModel";
import { buildSavingsContractHref } from "../../savings/savingsFilters";
import {
  CLOSE_CYCLE_KICKER,
  CLOSE_CYCLE_RENEWED_LABEL,
  CLOSE_CYCLE_TERMINATED_LABEL,
  SAVE_EXPLAINED,
  SIMILAR_IN_TOTAL,
  buildClosedOutcome,
  formatCloseCycleNote,
  formatTrackerMeta,
  type AnswersBand as AnswersBandModel,
  type NegotiationStep,
} from "./contract360ViewModel";

export type AnswersActionPending = RenewalActionKind | "undo" | "close" | "reopen" | null;

export interface AnswersBandProps {
  answers: AnswersBandModel;
  header: Contract360HeaderBody;
  currency: string;
  tracked: RenewalActionRow | null;
  steps: readonly NegotiationStep[];
  tickedKeys: ReadonlySet<string>;
  actionPending: AnswersActionPending;
  actionError: string | null;
  stepsError: string | null;
  onAction: (kind: RenewalActionKind) => void;
  onUndo: () => void;
  onToggleStep: (key: string) => void;
  onRetrySteps: () => void;
  /** "Terminated — I sent notice" confirmed with the date the notice went out (`yyyy-MM-dd`). */
  onTerminated: (noticeDate: string) => void;
  /** "Reopen" on a closed contract: back to "In negotiation". */
  onReopen: () => void;
}

function answerDisplayClass(base: string, text: string, extra = ""): string {
  return `${base}${text.length > 28 ? " is-prose" : ""}${extra}`;
}

function todayDateOnly(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * The answers band (`Raffa.ai V2.dc.html` CONTRACT 360: "Where you can save" · "When you must move"
 * · "What to do"), with the act cell's three faces: open (the recommended action as the primary
 * button + "Assign to me"), acted (the tracker: status · owner, four steps, "Track it in Renewals →"
 * / "Undo", then "Close the cycle"), and closed (the recorded outcome, "See it in Renewals →" /
 * "Reopen"). Every state is the server's own `savedAction` on `GET /api/renewals` (`NotStarted` ->
 * open, `InProgress` -> acted, `Completed` -> closed); nothing here is remembered client-side.
 *
 * "Renewed — upload the signed document" goes to Documents (the only place a document enters
 * Raffa.ai; there is no in-place upload on this screen) and "Terminated — I sent notice" records
 * the notice date as a `Completed` action -- the mock's sample-amendment shortcut has no wire.
 */
export default function AnswersBand({
  answers,
  header,
  currency,
  tracked,
  steps,
  tickedKeys,
  actionPending,
  actionError,
  stepsError,
  onAction,
  onUndo,
  onToggleStep,
  onRetrySteps,
  onTerminated,
  onReopen,
}: AnswersBandProps) {
  const { save, move, act } = answers;
  const [closing, setClosing] = useState(false);
  const [noticeDate, setNoticeDate] = useState(todayDateOnly);
  const busy = actionPending !== null;
  const closed = tracked !== null && tracked.status === "Completed";
  const negotiate = getRenewalActionPlan("negotiate");
  const assign = getRenewalActionPlan("assign");
  // Renewals and Savings open with this contract in focus, not at the top of their lists.
  const renewalsHref = `/renewals?select=${encodeURIComponent(header.contractId)}`;
  const savingsHref = buildSavingsContractHref(header.contractId);

  return (
    <section className="contract360-answers" aria-label="Answers">
      <div className="contract360-answer">
        <p className="contract360-answer-label">
          Where you can save
          {save.source !== "" && (
            <InfoTip label="Where this saving comes from">
              <p>{SAVE_EXPLAINED}</p>
              {save.similar === true && <p>{SIMILAR_IN_TOTAL}</p>}
              <p className="info-tip-meta">{save.source}</p>
            </InfoTip>
          )}
        </p>
        <p className={answerDisplayClass("contract360-answer-value", save.estimate)}>{save.estimate}</p>
        <p className="contract360-answer-detail">{save.lever}</p>
        <Link to={savingsHref} className="btn btn-ghost contract360-tracker-link contract360-answer-link">
          Track it in Savings →
        </Link>
      </div>

      <div className="contract360-answer">
        <p className="contract360-answer-label">
          When you must move
          <CopyTip tip={TIPS.contractMove} />
        </p>
        <p className={answerDisplayClass("contract360-answer-value", move.deadline, move.isUrgent ? " deadline-critical" : "")}>
          {move.deadlineHref !== null ? <Link to={move.deadlineHref}>{move.deadline}</Link> : move.deadline}
        </p>
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
        <p className="contract360-answer-label contract360-answer-label-accent">
          What to do
          <CopyTip tip={TIPS.contractAct} align="end" />
        </p>
        <p className={answerDisplayClass("contract360-answer-action", act.statement)}>{act.statement}</p>
        <p className="contract360-answer-detail">{act.rationale}</p>

        {tracked === null && (
          <div className="contract360-answer-buttons">
            {/* `<button class="btn btn-primary">{{ cur.action }}</button>`: the recommended action is
                the button; without a recommendation the plain verb stands in. */}
            <button type="button" className="btn btn-primary" disabled={busy} onClick={() => onAction("negotiate")}>
              {actionPending === "negotiate" ? "Saving…" : act.hasRecommendation ? act.statement : negotiate.buttonLabel}
            </button>
            <button type="button" className="btn btn-ghost contract360-assign" disabled={busy} onClick={() => onAction("assign")}>
              {actionPending === "assign" ? "Saving…" : assign.buttonLabel}
            </button>
          </div>
        )}

        {tracked !== null && !closed && (
          <div className="contract360-tracker" role="status">
            <div className="contract360-tracker-head">
              <span>
                <strong>{tracked.action}</strong> · owner {tracked.owner}
              </span>
              <span className="contract360-tracker-meta">{formatTrackerMeta(save, move)}</span>
            </div>
            <div className="contract360-tracker-steps">
              {steps.map((step) => {
                const done = tickedKeys.has(step.key);
                return (
                  <button
                    key={step.key}
                    type="button"
                    className={`contract360-step${done ? " is-done" : ""}`}
                    aria-pressed={done}
                    onClick={() => onToggleStep(step.key)}
                  >
                    <span className="contract360-step-box" aria-hidden="true" />
                    <span className="contract360-step-label">{step.label}</span>
                    <span className="contract360-step-due">{step.due}</span>
                  </button>
                );
              })}
            </div>
            <div className="contract360-tracker-links">
              <Link to={renewalsHref} className="btn btn-ghost contract360-tracker-link">
                Track it in Renewals →
              </Link>
              <button type="button" className="btn btn-ghost contract360-tracker-link contract360-undo" disabled={busy} onClick={onUndo}>
                {actionPending === "undo" ? "Undoing…" : "Undo"}
              </button>
            </div>

            <div className="contract360-close">
              <div className="contract360-close-kicker">{CLOSE_CYCLE_KICKER}</div>
              <p className="contract360-close-note">{formatCloseCycleNote(move.deadline)}</p>
              {!closing ? (
                <div className="contract360-close-buttons">
                  <Link to="/documents" className="btn btn-secondary contract360-close-button">
                    {CLOSE_CYCLE_RENEWED_LABEL}
                  </Link>
                  <button type="button" className="btn btn-ghost contract360-close-button" disabled={busy} onClick={() => setClosing(true)}>
                    {CLOSE_CYCLE_TERMINATED_LABEL}
                  </button>
                </div>
              ) : (
                <div className="contract360-close-form">
                  <label className="field contract360-close-field">
                    Date notice was sent
                    <input
                      className="input"
                      type="date"
                      value={noticeDate}
                      max={todayDateOnly()}
                      onChange={(event) => setNoticeDate(event.target.value)}
                    />
                  </label>
                  <div className="contract360-close-hint">Must be on or before {move.deadline}. You can also drop the notice letter as proof.</div>
                  <div className="contract360-close-buttons">
                    <button
                      type="button"
                      className="btn btn-primary contract360-close-button"
                      disabled={busy || noticeDate === ""}
                      onClick={() => {
                        setClosing(false);
                        onTerminated(noticeDate);
                      }}
                    >
                      {actionPending === "close" ? "Saving…" : `Confirm — contract ends ${move.termEnd ?? "at term end"}`}
                    </button>
                    <button type="button" className="btn btn-ghost contract360-close-button contract360-close-cancel" disabled={busy} onClick={() => setClosing(false)}>
                      Cancel
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>
        )}

        {tracked !== null && closed && (() => {
          const outcome = buildClosedOutcome(tracked.action, header, currency);
          return (
            <div className="contract360-outcome" role="status">
              <div className="contract360-outcome-head">
                <span>
                  <strong>{outcome.title}</strong> · {outcome.when}
                </span>
                <span className={`tag tag-${outcome.tag} contract360-outcome-tag`}>{outcome.kind}</span>
              </div>
              {outcome.facts.length > 0 && (
                <div className="contract360-outcome-facts">
                  {outcome.facts.map((fact) => (
                    <div key={fact.label} className="contract360-outcome-fact">
                      <span>{fact.label}</span>
                      <span className="contract360-outcome-fact-value">{fact.value}</span>
                    </div>
                  ))}
                </div>
              )}
              <div className="contract360-outcome-note">{outcome.note}</div>
              <div className="contract360-tracker-links">
                <Link to={renewalsHref} className="btn btn-ghost contract360-tracker-link">
                  See it in Renewals →
                </Link>
                <button type="button" className="btn btn-ghost contract360-tracker-link contract360-undo" disabled={busy} onClick={onReopen}>
                  {actionPending === "reopen" ? "Reopening…" : "Reopen"}
                </button>
              </div>
            </div>
          );
        })()}

        {stepsError !== null && (
          <div className="error-state" role="alert">
            <h4>Negotiation steps unavailable</h4>
            <p className="micro-meta">{stepsError}</p>
            <button type="button" className="btn btn-secondary" onClick={onRetrySteps}>
              Retry
            </button>
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
