import { useEffect, useRef } from "react";
import type { Card } from "../types";
import type { Translate } from "./chatCopy";
import StepCard from "./StepCard";

export interface HistoryDrawerProps {
  card: Card;
  t: Translate;
  onClose: () => void;
}

/** A past card, read only, in a panel on the right. Esc, the scrim or Close put the user back. */
export default function HistoryDrawer({ card, t, onClose }: HistoryDrawerProps) {
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    closeRef.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("keydown", onKey);
      opener?.focus?.();
    };
  }, [onClose]);

  return (
    <div className="rc-drawer">
      <div className="rc-scrim" onClick={onClose} aria-hidden="true" />
      <aside
        className="rc-panel"
        role="dialog"
        aria-modal="true"
        aria-label={t("stepHistory")}
        // Close is the only thing to press in a read-only record, so Tab stays on it.
        onKeyDown={(event) => {
          if (event.key === "Tab") {
            event.preventDefault();
            closeRef.current?.focus();
          }
        }}
      >
        <div className="rc-panel-head">
          <span className="rc-label">{t("stepHistory")}</span>
          <button ref={closeRef} type="button" className="btn btn-secondary" onClick={onClose}>
            {t("close")}
          </button>
        </div>
        <StepCard card={card} t={t} readOnly />
      </aside>
    </div>
  );
}
