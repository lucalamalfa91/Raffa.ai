export interface ArtifactCardProps {
  /** The artifact's own name -- for an email draft, its subject. Truncated to one line by CSS. */
  title: string;
  /** What the artifact is ("Email draft"), printed before the open hint. */
  typeLabel: string;
  /** True while this artifact is the one shown in the screen's side panel. */
  active: boolean;
  /** Opens the artifact in the side panel. Absent (a pure render with no screen around it), the
   * card renders as a static summary rather than a button that does nothing. */
  onOpen?: () => void;
}

export const ARTIFACT_OPEN_HINT = "Click to open";
export const ARTIFACT_OPEN_STATE = "Open";

/** The envelope glyph of an email artifact -- decorative, the type is also printed as text. */
export function MailIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <path d="m3.5 6.5 8.5 6.5 8.5-6.5" />
    </svg>
  );
}

/**
 * The in-chat handle of a long, self-contained reply object -- the Claude.ai "artifact" pattern:
 * the conversation keeps a one-line card (icon tile, title, "Type · Click to open") and the object
 * itself lives in the screen's right-hand panel (`../ArtifactPanel.tsx`), where its actions (copy,
 * open in mail) sit with it instead of in the middle of the thread. Today the only artifact is the
 * drafted negotiation email of a `draft` reply (ADR-030 D2).
 */
export default function ArtifactCard({ title, typeLabel, active, onOpen }: ArtifactCardProps) {
  const body = (
    <>
      <span className="artifact-card-icon" aria-hidden="true">
        <MailIcon />
      </span>
      <span className="artifact-card-text">
        <span className="artifact-card-title">{title}</span>
        <span className="artifact-card-meta">
          {typeLabel}
          {onOpen && (
            <>
              <span aria-hidden="true"> · </span>
              {active ? ARTIFACT_OPEN_STATE : ARTIFACT_OPEN_HINT}
            </>
          )}
        </span>
      </span>
    </>
  );

  if (!onOpen) {
    return (
      <div className="artifact-card" data-static="true">
        {body}
      </div>
    );
  }

  return (
    <button
      type="button"
      className="artifact-card"
      data-active={active ? "true" : "false"}
      aria-expanded={active}
      aria-label={`Open ${typeLabel.toLowerCase()}: ${title}`}
      onClick={onOpen}
    >
      {body}
    </button>
  );
}
