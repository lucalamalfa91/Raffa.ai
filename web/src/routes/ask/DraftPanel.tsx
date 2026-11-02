import { useEffect, useState } from "react";
import { useI18n } from "../../i18n/I18nProvider";
import ArtifactPanel from "./ArtifactPanel";
import type { EmailDraft } from "./draft";

export const COPIED_RESET_MS = 2000;

/** The envelope glyph of an email artifact. */
export function MailIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <path d="m3.5 6.5 8.5 6.5 8.5-6.5" />
    </svg>
  );
}

/** A `mailto:` with no recipient: the user's own mail client opens a new message with subject and body filled in.
 * Raffa never sends anything itself. */
export function buildMailtoHref(draft: EmailDraft): string {
  return `mailto:?subject=${encodeURIComponent(draft.subject)}&body=${encodeURIComponent(draft.body)}`;
}

/** The in-chat handle of an email draft: one line (icon, subject, "Email draft · Click to open"). */
export function ArtifactCard({ draft, active, onOpen }: { draft: EmailDraft; active: boolean; onOpen: () => void }) {
  const { t } = useI18n();
  return (
    <button type="button" className="artifact-card" data-active={active ? "true" : undefined} aria-pressed={active} onClick={onOpen}>
      <span className="artifact-card-icon" aria-hidden="true">
        <MailIcon />
      </span>
      <span className="artifact-card-text">
        <span className="artifact-card-title">{draft.subject || t("Email draft")}</span>
        <span className="artifact-card-meta">
          {t("Email draft")}
          <span aria-hidden="true"> · </span>
          {active ? t("Open⟦panel⟧") : t("Click to open")}
        </span>
      </span>
    </button>
  );
}

/** The drafted email, in the right-hand panel: subject and body verbatim, with Copy and Open in mail. */
export default function DraftPanel({ draft, onClose }: { draft: EmailDraft; onClose: () => void }) {
  const { t } = useI18n();
  const [copied, setCopied] = useState(false);

  useEffect(() => setCopied(false), [draft.subject, draft.body]);
  useEffect(() => {
    if (!copied) return undefined;
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
      label={t("Email draft")}
      kicker={t("Email draft")}
      title={draft.subject || t("Email draft")}
      icon={<MailIcon />}
      size="wide"
      onClose={onClose}
      actions={
        <>
          <a className="artifact-panel-action" href={buildMailtoHref(draft)}>
            {t("Open in mail")}
          </a>
          <button type="button" className="artifact-panel-action" data-primary="true" onClick={copy} aria-live="polite">
            {copied ? t("Copied") : t("Copy email")}
          </button>
        </>
      }
    >
      <article className="draft-document">
        {draft.subject ? (
          <p className="draft-document-subject">
            <span className="draft-document-subject-label">{t("Subject")}</span>
            <span>{draft.subject}</span>
          </p>
        ) : null}
        <pre className="draft-document-body">{draft.body}</pre>
      </article>
    </ArtifactPanel>
  );
}
