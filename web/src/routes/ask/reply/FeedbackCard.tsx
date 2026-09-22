import { useState } from "react";
import type { FeedbackAnswers, FeedbackOffer, FeedbackQuestion } from "./replyTypes";

export interface FeedbackCardProps {
  offer: FeedbackOffer;
  /** Posts the three answers; resolves `{ ok: false }` on a transport/4xx failure (the card shows
   * the offer's own error line and lets the user retry). The confirmation turn itself arrives
   * through the caller (appended to the thread), never rendered by this card. */
  onSubmit: (answers: FeedbackAnswers) => Promise<{ ok: boolean }>;
}

type Step = "offer" | "questions" | "sending" | "thanks" | "error" | "dismissed";

/**
 * The in-chat feedback card of a capability-gap turn (ADR-030 D5): "Vuoi segnalarlo al team
 * Raffa.ai?" [Sì] [No]; on yes, the three interview questions **one at a time** (a prefilled
 * free-text field, then two rows of quick-choice chips), then one submit. Every string comes from
 * the server's `offer` (already in the question's language) -- this component owns no copy. The
 * interview state lives here until submit: no server round trip per question, nothing to disambiguate
 * from a new question typed in the composer. "No" collapses the card (local state only, nothing is
 * stored); a resumed conversation hides the card instead through `feedbackResult.forMessageId`
 * (`../askViewModel.ts#feedbackSubmittedMessageIds`).
 */
export default function FeedbackCard({ offer, onSubmit }: FeedbackCardProps) {
  const questions = offer.questions;
  const [step, setStep] = useState<Step>("offer");
  const [index, setIndex] = useState(0);
  const [answers, setAnswers] = useState<Record<string, string>>(() => {
    const initial: Record<string, string> = {};
    for (const question of questions) {
      if (question.kind === "text" && question.prefill) initial[question.key] = question.prefill;
    }
    return initial;
  });

  if (step === "dismissed") return null;

  const current: FeedbackQuestion | undefined = questions[index];
  const isLast = index === questions.length - 1;
  const currentAnswer = current ? (answers[current.key] ?? "") : "";
  const canAdvance = currentAnswer.trim() !== "";

  const submit = async () => {
    setStep("sending");
    const outcome = await onSubmit({
      what: answers.what ?? "",
      frequency: answers.frequency ?? "",
      importance: answers.importance ?? "",
    });
    setStep(outcome.ok ? "thanks" : "error");
  };

  const next = () => {
    if (!canAdvance) return;
    if (isLast) {
      void submit();
      return;
    }
    setIndex(index + 1);
  };

  return (
    <div className="reply-feedback" role="group" aria-label={offer.prompt}>
      {step === "offer" && (
        <>
          <p className="reply-feedback-prompt">{offer.prompt}</p>
          <div className="reply-feedback-actions">
            <button type="button" className="btn btn-primary" onClick={() => setStep("questions")}>
              {offer.yesLabel}
            </button>
            <button type="button" className="btn btn-ghost" onClick={() => setStep("dismissed")}>
              {offer.noLabel}
            </button>
          </div>
        </>
      )}

      {(step === "questions" || step === "error") && current && (
        <div className="reply-feedback-question" aria-live="polite">
          {index === 0 && <p className="reply-feedback-notice">{offer.publicNotice}</p>}
          <p className="reply-feedback-step">
            {index + 1}/{questions.length}
          </p>
          <label className="reply-feedback-label" htmlFor={`feedback-${current.key}`}>
            {current.label}
          </label>
          {current.kind === "text" ? (
            <textarea
              id={`feedback-${current.key}`}
              className="input reply-feedback-text"
              rows={3}
              maxLength={500}
              value={currentAnswer}
              onChange={(event) => setAnswers({ ...answers, [current.key]: event.target.value })}
            />
          ) : (
            <div className="reply-feedback-chips" id={`feedback-${current.key}`}>
              {(current.choices ?? []).map((choice) => (
                <button
                  key={choice.key}
                  type="button"
                  className={`reply-feedback-chip${currentAnswer === choice.key ? " is-selected" : ""}`}
                  aria-pressed={currentAnswer === choice.key}
                  onClick={() => setAnswers({ ...answers, [current.key]: choice.key })}
                >
                  {choice.label}
                </button>
              ))}
            </div>
          )}
          {step === "error" && (
            <p className="reply-feedback-error" role="alert">
              {offer.errorLabel}
            </p>
          )}
          <div className="reply-feedback-actions">
            {index > 0 && (
              <button type="button" className="btn btn-ghost" onClick={() => setIndex(index - 1)}>
                {offer.backLabel}
              </button>
            )}
            <button type="button" className="btn btn-primary" disabled={!canAdvance} onClick={next}>
              {isLast ? offer.submitLabel : offer.nextLabel}
            </button>
          </div>
        </div>
      )}

      {step === "sending" && (
        <p className="reply-feedback-status" role="status">
          {offer.sendingLabel}
        </p>
      )}

      {step === "thanks" && (
        <p className="reply-feedback-status" role="status">
          {offer.thanksLabel}
        </p>
      )}
    </div>
  );
}
