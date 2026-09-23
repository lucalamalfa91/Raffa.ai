import { useEffect, useState } from "react";
import ArtifactPanel from "./ArtifactPanel";
import { MailIcon } from "./reply/ArtifactCard";
import type { ReplyDraft } from "./reply/replyTypes";
import { COPIED_LABEL, COPY_EMAIL_LABEL, DRAFT_CARD_TITLE, DRAFT_SUBJECT_LABEL, OPEN_IN_MAIL_LABEL } from "./askViewModel";

export interface DraftPanelProps {
  draft: ReplyDraft;
  focusOnOpen?: boolean;
  onClose: () => void;
}

/** How long "Copied" stays on the button before it reads "Copy email" again. */
export const COPIED_RESET_MS = 2000;

/** A `mailto:` with no recipient -- the user's own mail client opens a new message with the subject
 * and body filled in. Raffa never sends anything itself (ADR-030 D2: the draft path is a draft). */
export function buildMailtoHref(draft: ReplyDraft): string {
  return `mailto:?subject=${encodeURIComponent(draft.subject)}&body=${encodeURIComponent(draft.body)}`;
}

/**
 * The drafted negotiation email of a `draft` reply (ADR-030 D2), opened in the side panel from its
 * in-chat card (`reply/ArtifactCard.tsx`). The subject and the body are rendered **verbatim** -- the
 * body a `<pre>` with `white-space: pre-wrap` in the reading font, never through `ReplyMarkdown` or
 * `humanizeReplyText` -- so what the user reads is byte-for-byte what "Copy email" puts on the
 * clipboard (`subject`, a blank line, `body`): the same `navigator.clipboard.writeText` pattern
 * `../workspace/members/InvitePane.tsx`'s "Copy link" uses, with the same honest fallback (no
 * clipboard API, no button state change). "Open in mail" hands the same text to the user's mail
 * client.
 */
export default function DraftPanel({ draft, focusOnOpen, onClose }: DraftPanelProps) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    setCopied(false);
  }, [draft.subject, draft.body]);

  useEffect(() => {
    if (!copied) return;
    const timer = window.setTimeout(() => setCopied(false), COPIED_RESET_MS);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const copy = () => {
    const clipboard = typeof navigator === "undefined" ? undefined : navigator.clipboard;
    if (!clipboard) return;
    void clipboard.writeText(`${draft.subject}\n\n${draft.body}`).then(() => setCopied(true));
  };

  return (
    <ArtifactPanel
      label={DRAFT_CARD_TITLE}
      kicker={DRAFT_CARD_TITLE}
      title={draft.subject}
      icon={<MailIcon />}
      size="wide"
      focusOnOpen={focusOnOpen}
      onClose={onClose}
      actions={
        <>
          <a className="artifact-panel-action" href={buildMailtoHref(draft)}>
            {OPEN_IN_MAIL_LABEL}
          </a>
          <button type="button" className="artifact-panel-action" data-primary="true" onClick={copy} aria-live="polite">
            {copied ? COPIED_LABEL : COPY_EMAIL_LABEL}
          </button>
        </>
      }
    >
      <article className="draft-document">
        <p className="draft-document-subject">
          <span className="draft-document-subject-label">{DRAFT_SUBJECT_LABEL}</span>
          <span>{draft.subject}</span>
        </p>
        <pre className="draft-document-body">{draft.body}</pre>
      </article>
    </ArtifactPanel>
  );
}
