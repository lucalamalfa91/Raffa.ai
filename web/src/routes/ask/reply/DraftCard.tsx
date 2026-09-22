import { useEffect, useState } from "react";
import type { ReplyDraft } from "./replyTypes";
import { COPIED_LABEL, COPY_EMAIL_LABEL, DRAFT_CARD_TITLE, DRAFT_SUBJECT_LABEL } from "../askViewModel";

export interface DraftCardProps {
  draft: ReplyDraft;
}

/**
 * The drafted negotiation email of a `draft` reply (ADR-030 D2). The subject and the body are
 * rendered **verbatim** -- a `<pre>` with `white-space: pre-wrap`, never through `ReplyMarkdown`
 * or `humanizeReplyText` -- so what the user reads is byte-for-byte what "Copy email" puts on the
 * clipboard (`subject`, a blank line, `body`): the same `navigator.clipboard.writeText` pattern
 * `../../workspace/members/InvitePane.tsx`'s "Copy link" already uses, with the same honest
 * fallback (no clipboard API, no button state change). The chrome ("Draft email", "Subject",
 * "Copy email") is English like every other label of this screen; the email itself arrives in the
 * question's language from the server.
 */
export default function DraftCard({ draft }: DraftCardProps) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    setCopied(false);
  }, [draft.subject, draft.body]);

  const copy = () => {
    const clipboard = typeof navigator === "undefined" ? undefined : navigator.clipboard;
    if (!clipboard) return;
    void clipboard.writeText(`${draft.subject}\n\n${draft.body}`).then(() => setCopied(true));
  };

  return (
    <section className="reply-draft" aria-label={DRAFT_CARD_TITLE}>
      <div className="reply-draft-header">
        <span className="reply-draft-label">{DRAFT_CARD_TITLE}</span>
        <button type="button" className="btn btn-secondary reply-draft-copy" onClick={copy} aria-live="polite">
          {copied ? COPIED_LABEL : COPY_EMAIL_LABEL}
        </button>
      </div>
      <p className="reply-draft-subject">
        <span className="reply-draft-subject-label">{DRAFT_SUBJECT_LABEL}:</span> {draft.subject}
      </p>
      <pre className="reply-draft-body">{draft.body}</pre>
    </section>
  );
}
