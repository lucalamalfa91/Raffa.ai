import { useEffect, useRef, type ReactNode } from "react";

export interface ArtifactPanelProps {
  /** The landmark's accessible name ("Email draft", "Market record"). */
  label: string;
  /** Muted type line under the title ("Email draft", "Market · representative"). */
  kicker: ReactNode;
  title: string;
  /** Optional decorative glyph for the header's icon tile. */
  icon?: ReactNode;
  /** The object's own actions (copy, open in mail), rendered before the close button. */
  actions?: ReactNode;
  /** `wide` for documents (a drafted email), `narrow` for a record's facts. */
  size: "wide" | "narrow";
  /** Moves focus into the panel on mount -- only when the user opened it; an automatic open (a
   * draft that just arrived) never pulls focus away from the composer. */
  focusOnOpen?: boolean;
  onClose: () => void;
  children: ReactNode;
}

/**
 * The screen's one right-hand slot for a reply object -- the Claude.ai artifact panel: the chat
 * narrows to the left and keeps working, the panel carries one header anatomy for every object
 * (icon tile, title, type line, the object's own actions, close) and a scrolling body. Below the
 * 900px breakpoint it becomes a full-screen sheet (`ask.css`). Escape closes it.
 */
export default function ArtifactPanel({ label, kicker, title, icon, actions, size, focusOnOpen = false, onClose, children }: ArtifactPanelProps) {
  const panelRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (focusOnOpen) panelRef.current?.focus();
  }, [focusOnOpen]);

  return (
    <aside
      ref={panelRef}
      className="artifact-panel"
      data-size={size}
      aria-label={label}
      tabIndex={-1}
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          event.stopPropagation();
          onClose();
        }
      }}
    >
      <header className="artifact-panel-header">
        {icon && (
          <span className="artifact-panel-icon" aria-hidden="true">
            {icon}
          </span>
        )}
        <div className="artifact-panel-heading">
          <h3 className="artifact-panel-title" title={title}>
            {title}
          </h3>
          <span className="artifact-panel-kicker">{kicker}</span>
        </div>
        {actions && <div className="artifact-panel-actions">{actions}</div>}
        <button type="button" className="artifact-panel-close" onClick={onClose} aria-label="Close">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true" focusable="false">
            <path d="M6 6l12 12M18 6 6 18" />
          </svg>
        </button>
      </header>
      <div className="artifact-panel-body">{children}</div>
    </aside>
  );
}
