import ActionRow from "./ActionRow";
import EvidenceCard from "./EvidenceCard";
import InterviewBlock from "./InterviewBlock";
import ReplyMarkdown from "./ReplyMarkdown";
import type { InterviewOption, InterviewReply, Reply, ReplyCitation } from "./replyTypes";
import "./reply.css";

export interface ReplyBodyProps {
  reply: Reply;
  /** Shared by every inline `[n]` marker (`ReplyMarkdown`) and every row of the `EvidenceCard` --
   * both surfaces call this exact same callback with the exact same citation object, not a DOM
   * anchor jump. See `ReplyMarkdown.tsx`'s own header comment for why: an `href="#id"` anchor
   * cannot stay unique once more than one reply is on screen at once, which every real
   * conversation is. */
  onOpenCitation: (citation: ReplyCitation) => void;
  /** A follow-up chip was clicked -- on an `answer`, or on an `abstain` that carries next-step
   * questions (`AbstainReply.followUps`); never called for any other kind. */
  onFollowUp: (question: string) => void;
  /** `interview`-only (ADR-030): the user picked an option. Optional so the pure component still
   * renders an interview read-only (a resumed, already-answered one) without a handler. */
  onInterviewOption?: (reply: InterviewReply, questionKey: string, option: InterviewOption) => void;
}

/** The "Next" row of follow-up question chips, shared by `answer` and `abstain`. */
function FollowUps({ questions, onFollowUp }: { questions: readonly string[]; onFollowUp: (question: string) => void }) {
  if (questions.length === 0) {
    return null;
  }

  return (
    <div className="reply-followups">
      <span className="reply-followups-label">Next</span>
      {questions.map((question) => (
        <button key={question} type="button" className="reply-followup" onClick={() => onFollowUp(question)}>
          {question} →
        </button>
      ))}
    </div>
  );
}

/**
 * Composes `kind` -> layout (R-WEB-04; requirements.md §6; ADR-024). One `Reply` in, one layout
 * out: `answer` gets the markdown, **one** evidence card (every citation grouped by supplier, the
 * reply's actions folded into that card's single action row -- `EvidenceCard.tsx`) and the
 * follow-ups; `redirect` and `refusal` share warm prose + one CTA; `abstain` is Raffa's own way
 * forward when no grounded answer exists -- the same prose as any reply, never a "cannot determine"
 * banner (the accent-left "I don't have data I trust enough to answer." block read as an error, and
 * persona v2.4 retired it: Ask always proposes a solution); `error` is the existing `.error-state`.
 * This is the one place any of those layouts is chosen -- every other component in this folder
 * only renders what it is told to.
 *
 * Never renders an engineer route line or a guid (R-ASK-08): `Reply` (`replyTypes.ts`) has no
 * `route`/raw-id field for any variant to leak in the first place -- there is nothing here to
 * accidentally print.
 */
export default function ReplyBody({ reply, onOpenCitation, onFollowUp, onInterviewOption }: ReplyBodyProps) {
  switch (reply.kind) {
    case "answer":
      return (
        <div className="reply-body" data-reply-kind="answer" data-unverified={reply.unverifiedWeb ? "true" : undefined}>
          {reply.unverifiedWeb && (
            <p className="reply-unverified-banner" role="note">
              <strong>Public web · not verified.</strong> These findings come from public sources and were not checked
              against your contracts.
            </p>
          )}
          <ReplyMarkdown text={reply.answerMarkdown} citations={reply.citations} onOpenCitation={onOpenCitation} />

          {reply.citations.length > 0 ? (
            <div className="reply-cards">
              <EvidenceCard citations={reply.citations} actions={reply.actions} onOpenCitation={onOpenCitation} />
            </div>
          ) : (
            reply.actions.length > 0 && <ActionRow actions={reply.actions} />
          )}

          <FollowUps questions={reply.followUps} onFollowUp={onFollowUp} />
        </div>
      );

    case "redirect":
    case "refusal":
      return (
        <div className="reply-body" data-reply-kind={reply.kind}>
          <ReplyMarkdown text={reply.answerMarkdown} citations={[]} onOpenCitation={onOpenCitation} />
          {/* R-ASK-07 / parent AC-3 "one CTA": rendered defensively -- only ever the first action --
              even if the reply somehow carried more than one; see replyTypes.ts#RedirectReply. */}
          {reply.actions.length > 0 && <ActionRow actions={reply.actions.slice(0, 1)} />}
        </div>
      );

    case "interview":
      return (
        <div className="reply-body" data-reply-kind="interview">
          <ReplyMarkdown text={reply.prompt} citations={[]} onOpenCitation={onOpenCitation} />
          <InterviewBlock reply={reply} onOption={(questionKey, option) => onInterviewOption?.(reply, questionKey, option)} />
        </div>
      );

    case "abstain":
      return (
        <div className="reply-body" data-reply-kind="abstain">
          {/* The server's proposal (a plan, a draft, the screen to open) as plain reply prose --
              markdown, like an answer, with no citation to point at. Old conversations persisted
              before persona v2.4 render their stored reason the same way: no banner. */}
          <ReplyMarkdown text={reply.reason} citations={[]} onOpenCitation={onOpenCitation} />
          {/* ADR-024 "every abstain has a clickable next step" / parent story AC-1: the recovery
              action always renders secondary, never primary -- forced here regardless of the
              `kind` the mapper produced, the same defensive posture the redirect/refusal case
              above already takes with its own "only ever the first action" slice. AC-3: no
              action is not an error -- the prose above already renders on its own. */}
          {reply.actions && reply.actions.length > 0 && (
            <ActionRow actions={reply.actions.map((action) => ({ ...action, kind: "secondary" }))} />
          )}
          {/* A gap is never a dead end: the server's next-step questions, when it sent any. */}
          <FollowUps questions={reply.followUps ?? []} onFollowUp={onFollowUp} />
        </div>
      );

    case "error":
      return (
        <div className="reply-body" data-reply-kind="error">
          <div className="error-state" role="alert">
            <p className="micro-meta">{reply.reason}</p>
          </div>
        </div>
      );

    default: {
      // Exhaustiveness guard: if `ReplyKind` ever grows a variant, this line stops compiling
      // instead of silently rendering nothing for it.
      const exhaustiveCheck: never = reply;
      return exhaustiveCheck;
    }
  }
}
