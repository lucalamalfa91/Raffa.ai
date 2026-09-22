import ActionRow from "./ActionRow";
import EvidenceCard from "./EvidenceCard";
import InterviewBlock from "./InterviewBlock";
import ReplyMarkdown from "./ReplyMarkdown";
import type { InterviewOption, InterviewReply, Reply, ReplyCitation } from "./replyTypes";
import "./reply.css";

/** The abstain block's lead-in (`abstainTitle` in `Raffa.ai V2.dc.html`, quoted). */
export const ABSTAIN_TITLE = "I don't have data I trust enough to answer.";

export interface ReplyBodyProps {
  reply: Reply;
  /** Shared by every inline `[n]` marker (`ReplyMarkdown`) and every row of the `EvidenceCard` --
   * both surfaces call this exact same callback with the exact same citation object, not a DOM
   * anchor jump. See `ReplyMarkdown.tsx`'s own header comment for why: an `href="#id"` anchor
   * cannot stay unique once more than one reply is on screen at once, which every real
   * conversation is. */
  onOpenCitation: (citation: ReplyCitation) => void;
  /** `answer`-only (task text: "markdown + cards + actions + follow-ups"); never called for any
   * other kind, since only `AnswerReply` carries `followUps`. */
  onFollowUp: (question: string) => void;
  /** `interview`-only (ADR-030): the user picked an option. Optional so the pure component still
   * renders an interview read-only (a resumed, already-answered one) without a handler. */
  onInterviewOption?: (reply: InterviewReply, questionKey: string, option: InterviewOption) => void;
}

/**
 * Composes `kind` -> layout (R-WEB-04; requirements.md §6; ADR-024). One `Reply` in, one layout
 * out: `answer` gets the markdown, **one** evidence card (every citation grouped by supplier, the
 * reply's actions folded into that card's single action row -- `EvidenceCard.tsx`) and the
 * follow-ups; `redirect` and `refusal` share warm prose + one CTA; `abstain` is only ever the
 * accent-left block; `error` is the existing `.error-state`. This is the one place any of those
 * five layouts is chosen -- every other component in this folder only renders what it is told to.
 *
 * Never renders an engineer route line or a guid (R-ASK-08): `Reply` (`replyTypes.ts`) has no
 * `route`/raw-id field for any variant to leak in the first place -- there is nothing here to
 * accidentally print.
 */
export default function ReplyBody({ reply, onOpenCitation, onFollowUp, onInterviewOption }: ReplyBodyProps) {
  switch (reply.kind) {
    case "answer":
      return (
        <div className="reply-body" data-reply-kind="answer">
          <ReplyMarkdown text={reply.answerMarkdown} citations={reply.citations} onOpenCitation={onOpenCitation} />

          {reply.citations.length > 0 ? (
            <div className="reply-cards">
              <EvidenceCard citations={reply.citations} actions={reply.actions} onOpenCitation={onOpenCitation} />
            </div>
          ) : (
            reply.actions.length > 0 && <ActionRow actions={reply.actions} />
          )}

          {reply.followUps.length > 0 && (
            <div className="reply-followups">
              <span className="reply-followups-label">Next</span>
              {reply.followUps.map((question) => (
                <button key={question} type="button" className="reply-followup" onClick={() => onFollowUp(question)}>
                  {question} →
                </button>
              ))}
            </div>
          )}
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
          <div className="abstain-block">
            <strong>{ABSTAIN_TITLE}</strong> {reply.reason}
          </div>
          {/* ADR-024 "every abstain has a clickable next step" / parent story AC-1: the recovery
              action always renders secondary, never primary -- forced here regardless of the
              `kind` the mapper produced, the same defensive posture the redirect/refusal case
              above already takes with its own "only ever the first action" slice. AC-3: no
              action is not an error -- the block above already renders on its own. */}
          {reply.actions && reply.actions.length > 0 && (
            <ActionRow actions={reply.actions.map((action) => ({ ...action, kind: "secondary" }))} />
          )}
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
