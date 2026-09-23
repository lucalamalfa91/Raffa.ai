import ActionRow from "./ActionRow";
import ArtifactCard from "./ArtifactCard";
import EvidenceCard from "./EvidenceCard";
import FeedbackCard from "./FeedbackCard";
import InterviewBlock from "./InterviewBlock";
import ReplyMarkdown from "./ReplyMarkdown";
import type { FeedbackAnswers, FeedbackOffer, InterviewOption, InterviewReply, Reply, ReplyCitation, ReplyDraft } from "./replyTypes";
import { DRAFT_CARD_TITLE } from "../askViewModel";
import "./reply.css";

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
  /** `draft`-only: the email card was clicked -- the screen opens the draft in its side panel
   * (`../DraftPanel.tsx`). Absent, the card renders as a static summary. */
  onOpenDraft?: (draft: ReplyDraft) => void;
  /** `draft`-only: this turn's draft is the one the side panel shows right now. */
  draftOpen?: boolean;
  /** False on every turn but the latest: suggested next questions belong to where the conversation
   * is now, not to its history (the Claude.ai rule -- nothing to click on stale turns). */
  showFollowUps?: boolean;
}

/** The follow-up question chips, shared by `answer`, `abstain`, `draft` and a capability-gap
 * `redirect` (ADR-030): quiet one-line suggestions at the end of the latest turn; clicking one asks
 * it. */
function FollowUps({ questions, onFollowUp }: { questions: readonly string[]; onFollowUp: (question: string) => void }) {
  if (questions.length === 0) {
    return null;
  }

  return (
    <div className="reply-followups" role="group" aria-label="Suggested questions">
      {questions.map((question) => (
        <button key={question} type="button" className="reply-followup" onClick={() => onFollowUp(question)}>
          <span className="reply-followup-text">{question}</span>
          <span className="reply-followup-arrow" aria-hidden="true">
            ↗
          </span>
        </button>
      ))}
    </div>
  );
}

/**
 * Composes `kind` -> layout (R-WEB-04; requirements.md §6; ADR-024). `answer` uses the shared
 * evidence card; `draft` adds the email's artifact card (the email opens in the side panel) and
 * the feedback card; `redirect`/`refusal`
 * keep one CTA; `interview` renders clickable options; `abstain` is Raffa's own way forward when no
 * grounded answer exists -- the same prose as any reply, never a "cannot determine" banner (the
 * accent-left "I don't have data I trust enough to answer." block read as an error, and persona
 * v2.4 retired it: Ask always proposes a solution); `error` is the existing `.error-state`. This is
 * the one place any of those layouts is chosen -- every other component in this folder only
 * renders what it is told to.
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
  onOpenDraft,
  draftOpen = false,
  showFollowUps = true,
}: ReplyBodyProps) {
  // ADR-030 D5: the feedback card renders only when the turn carries an offer, has a real server
  // id to submit against, the screen wired a submit path, and the offer was not answered yet.
  const feedbackCard = (offer: FeedbackOffer | null | undefined) =>
    offer && messageId && onSubmitFeedback && !feedbackDone ? (
      <FeedbackCard offer={offer} onSubmit={(answers) => onSubmitFeedback(messageId, answers)} />
    ) : null;
  const followUps = (questions: readonly string[] | undefined) =>
    showFollowUps ? <FollowUps questions={questions ?? []} onFollowUp={onFollowUp} /> : null;
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

          {followUps(reply.followUps)}
        </div>
      );

    case "draft":
      // ADR-030 D2: the honest preface, the email as an artifact card (the email itself, verbatim
      // with "Copy email", opens in the screen's side panel), the pack items the email was written
      // from, the actions, the follow-ups, then the feedback offer. Never the abstain block -- the
      // draft path cannot abstain.
      return (
        <div className="reply-body" data-reply-kind="draft">
          <ReplyMarkdown text={reply.answerMarkdown} citations={reply.citations} onOpenCitation={onOpenCitation} />

          <ArtifactCard
            title={reply.draft.subject}
            typeLabel={DRAFT_CARD_TITLE}
            active={draftOpen}
            onOpen={onOpenDraft ? () => onOpenDraft(reply.draft) : undefined}
          />

          {reply.citations.length > 0 ? (
            <div className="reply-cards">
              <EvidenceCard citations={reply.citations} actions={reply.actions} onOpenCitation={onOpenCitation} />
            </div>
          ) : (
            reply.actions.length > 0 && <ActionRow actions={reply.actions} />
          )}

          {followUps(reply.followUps)}

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
          {followUps(reply.followUps)}
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
          {followUps(reply.followUps)}
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
