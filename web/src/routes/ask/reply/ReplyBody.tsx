import ActionRow from "./ActionRow";
import DraftCard from "./DraftCard";
import EvidenceCard from "./EvidenceCard";
import FeedbackCard from "./FeedbackCard";
import InterviewBlock from "./InterviewBlock";
import ReplyMarkdown from "./ReplyMarkdown";
import type { FeedbackAnswers, FeedbackOffer, InterviewOption, InterviewReply, Reply, ReplyCitation } from "./replyTypes";
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
  /** A follow-up chip was clicked -- on an `answer`, on an `abstain` that carries next-step
   * questions (`AbstainReply.followUps`), on a `draft`, or on a capability-gap `redirect`
   * (ADR-030); never called for any other kind. */
  onFollowUp: (question: string) => void;
  /** ADR-030 D5: this turn's server message id -- what the feedback card submits against; `null`
   * for a client-built turn, which then never shows the card. */
  messageId?: string | null;
  /** ADR-030 D5: posts the feedback card's answers for `messageId`; absent, no card renders. */
  onSubmitFeedback?: (messageId: string, answers: FeedbackAnswers) => Promise<{ ok: boolean }>;
  /** ADR-030 D5: true once this turn's offer was answered (live or on resume) -- hides the card. */
  feedbackDone?: boolean;
  /** `interview`-only (ADR-030): the user picked an option. Optional so the pure component still
   * renders an interview read-only (a resumed, already-answered one) without a handler. */
  onInterviewOption?: (reply: InterviewReply, questionKey: string, option: InterviewOption) => void;
}

/** The "Next" row of follow-up question chips, shared by `answer`, `abstain`, `draft` and a
 * capability-gap `redirect` (ADR-030). */
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
 * Composes `kind` -> layout (R-WEB-04; requirements.md §6; ADR-024). `answer` uses the shared
 * evidence card; `draft` adds the verbatim email card and feedback card; `redirect`/`refusal`
 * keep one CTA; `interview` renders clickable options; `abstain` is only the accent-left block;
 * `error` is the existing `.error-state`.
 *
 * Never renders an engineer route line or a guid (R-ASK-08): `Reply` (`replyTypes.ts`) has no
 * `route`/raw-id field for any variant to leak in the first place -- there is nothing here to
 * accidentally print.
 */
export default function ReplyBody({
  reply,
  onOpenCitation,
  onFollowUp,
  messageId,
  onSubmitFeedback,
  feedbackDone,
  onInterviewOption,
}: ReplyBodyProps) {
  // ADR-030 D5: the feedback card renders only when the turn carries an offer, has a real server
  // id to submit against, the screen wired a submit path, and the offer was not answered yet.
  const feedbackCard = (offer: FeedbackOffer | null | undefined) =>
    offer && messageId && onSubmitFeedback && !feedbackDone ? (
      <FeedbackCard offer={offer} onSubmit={(answers) => onSubmitFeedback(messageId, answers)} />
    ) : null;
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

    case "draft":
      // ADR-030 D2: the honest preface, the email card (verbatim + "Copy email"), the pack items
      // the email was written from, the actions, the follow-ups, then the feedback offer. Never
      // the abstain block -- the draft path cannot abstain.
      return (
        <div className="reply-body" data-reply-kind="draft">
          <ReplyMarkdown text={reply.answerMarkdown} citations={reply.citations} onOpenCitation={onOpenCitation} />

          <DraftCard draft={reply.draft} />

          {reply.citations.length > 0 ? (
            <div className="reply-cards">
              <EvidenceCard citations={reply.citations} actions={reply.actions} onOpenCitation={onOpenCitation} />
            </div>
          ) : (
            reply.actions.length > 0 && <ActionRow actions={reply.actions} />
          )}

          <FollowUps questions={reply.followUps} onFollowUp={onFollowUp} />

          {feedbackCard(reply.feedbackOffer)}
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
          {/* ADR-030: a capability-gap redirect ("which contract?") offers one supplier per chip
              and the feedback card; every other redirect/refusal carries neither. */}
          <FollowUps questions={reply.followUps ?? []} onFollowUp={onFollowUp} />
          {feedbackCard(reply.feedbackOffer)}
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
