import ActionRow from "./ActionRow";
import CitationCard from "./CitationCard";
import ReplyMarkdown from "./ReplyMarkdown";
import type { Reply, ReplyCitation } from "./replyTypes";
import "./reply.css";

export interface ReplyBodyProps {
  reply: Reply;
  /** Shared by every inline `[n]` marker (`ReplyMarkdown`) and every `CitationCard`'s own button --
   * "linking to the matching card" (task text) means both surfaces call this exact same callback
   * with the exact same citation object, not a DOM anchor jump. See `ReplyMarkdown.tsx`'s own
   * header comment for why: an `href="#id"` anchor cannot stay unique once more than one reply is
   * on screen at once, which every real conversation is. */
  onOpenCitation: (citation: ReplyCitation) => void;
  /** `answer`-only (task text: "markdown + cards + actions + follow-ups"); never called for any
   * other kind, since only `AnswerReply` carries `followUps`. */
  onFollowUp: (question: string) => void;
}

/**
 * Composes `kind` -> layout (task text; R-WEB-04; requirements.md §6; ADR-024). One `Reply` in,
 * one layout out: `answer` gets the full markdown/cards/actions/follow-ups treatment; `redirect`
 * and `refusal` share warm prose + one CTA; `abstain` is only ever the accent-left block; `error`
 * is the existing `.error-state`. This is the one place any of those five layouts is chosen --
 * every other component in this folder only renders what it is told to.
 *
 * Never renders an engineer route line or a guid (task text; R-ASK-08): `Reply` (`replyTypes.ts`)
 * has no `route`/raw-id field for any variant to leak in the first place -- there is nothing here
 * to accidentally print.
 */
export default function ReplyBody({ reply, onOpenCitation, onFollowUp }: ReplyBodyProps) {
  switch (reply.kind) {
    case "answer":
      return (
        <div className="reply-body" data-reply-kind="answer">
          <ReplyMarkdown text={reply.answerMarkdown} citations={reply.citations} onOpenCitation={onOpenCitation} />

          {reply.citations.length > 0 && (
            <div className="reply-cards">
              {reply.citations.map((citation) => (
                <CitationCard key={citation.n} {...citation} onOpen={() => onOpenCitation(citation)} />
              ))}
            </div>
          )}

          {reply.actions.length > 0 && <ActionRow actions={reply.actions} />}

          {reply.followUps.length > 0 && (
            <div className="reply-followups">
              <p className="reply-followups-label micro-meta">Follow up</p>
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

    case "abstain":
      return (
        <div className="reply-body" data-reply-kind="abstain">
          <div className="abstain-block">
            <strong>Cannot determine reliably.</strong> {reply.reason}
          </div>
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
