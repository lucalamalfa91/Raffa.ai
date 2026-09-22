import { useEffect, useRef } from "react";
import type { InterviewOption, InterviewQuestion, InterviewReply } from "./replyTypes";

export interface ConsentDialogProps {
  reply: InterviewReply;
  question: InterviewQuestion;
  /** The chosen option -- the caller posts it by key exactly like any interview chip. */
  onDecide: (reply: InterviewReply, question: InterviewQuestion, option: InterviewOption) => void;
}

/** The consent's two options by convention (`WebConsentInterview` on the server): "decline" is the
 * safe default and takes focus; "allow" is the one that lets a search happen. */
export const CONSENT_ALLOW_KEY = "allow";
export const CONSENT_DECLINE_KEY = "decline";

/**
 * ADR-030: the alert the product owner asked for -- Raffa never reaches the public web without
 * an explicit yes on *this* question. `role="alertdialog"`, modal, focus starts on the safe
 * button ("No, stay in Raffa"), Tab cycles inside the dialog, Escape declines. The dialog only
 * ever renders for an interview whose question carries `presentation: "consent"`, and the same
 * question stays available as chips underneath for anyone who dismissed the dialog by keyboard.
 */
export default function ConsentDialog({ reply, question, onDecide }: ConsentDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const declineRef = useRef<HTMLButtonElement>(null);

  const allow = question.options.find((option) => option.key === CONSENT_ALLOW_KEY) ?? null;
  const decline = question.options.find((option) => option.key === CONSENT_DECLINE_KEY) ?? null;

  useEffect(() => {
    declineRef.current?.focus();
  }, []);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape" && decline) {
        event.preventDefault();
        onDecide(reply, question, decline);
        return;
      }
      if (event.key === "Tab" && dialogRef.current) {
        const focusable = Array.from(dialogRef.current.querySelectorAll<HTMLElement>("button:not([disabled])"));
        if (focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault();
          last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first.focus();
        }
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [decline, onDecide, question, reply]);

  if (allow === null || decline === null) return null;

  const titleId = `consent-title-${question.key}`;
  const bodyId = `consent-body-${question.key}`;

  return (
    <div className="consent-dialog-backdrop" data-testid="consent-dialog">
      <div ref={dialogRef} className="consent-dialog" role="alertdialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={bodyId}>
        <p className="consent-dialog-kicker">Ask Raffa · web research</p>
        <h3 id={titleId} className="consent-dialog-title">
          Search the public web?
        </h3>
        <div id={bodyId} className="consent-dialog-body">
          {question.prompt}
        </div>
        <div className="consent-dialog-actions">
          <button ref={declineRef} type="button" className="btn btn-secondary" title={decline.hint ?? undefined} onClick={() => onDecide(reply, question, decline)}>
            {decline.label}
          </button>
          <button type="button" className="btn btn-primary" title={allow.hint ?? undefined} onClick={() => onDecide(reply, question, allow)}>
            {allow.label}
          </button>
        </div>
      </div>
    </div>
  );
}
