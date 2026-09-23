import { useId, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import "./InfoTip.css";

const GAP = 8;
const EDGE = 12;

/**
 * A quiet "i" next to a label that explains, on hover or keyboard focus, where a figure comes from
 * -- so the screen keeps only the figures, and the explanation is one glance away, never in the way.
 * The bubble is portalled to `document.body` and placed with fixed coordinates, so a scrolling
 * table never clips it; it opens below the "i" (above when there is no room) and stays inside the
 * viewport. It is also the trigger's accessible description (`aria-describedby`), always in the DOM.
 */
export default function InfoTip({ label, children, align = "start" }: { label: string; children: ReactNode; align?: "start" | "end" }) {
  const id = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const bubbleRef = useRef<HTMLSpanElement>(null);
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState<{ top: number; left: number } | null>(null);

  useLayoutEffect(() => {
    if (!open || triggerRef.current === null || bubbleRef.current === null) return;
    const anchor = triggerRef.current.getBoundingClientRect();
    const bubble = bubbleRef.current.getBoundingClientRect();
    const below = anchor.bottom + GAP;
    const top = below + bubble.height > window.innerHeight - EDGE ? Math.max(EDGE, anchor.top - GAP - bubble.height) : below;
    const preferred = align === "end" ? anchor.right + GAP - bubble.width : anchor.left - GAP;
    const left = Math.min(Math.max(EDGE, preferred), window.innerWidth - EDGE - bubble.width);
    setPosition({ top, left });
  }, [open, align]);

  const show = () => setOpen(true);
  const hide = () => {
    setOpen(false);
    setPosition(null);
  };

  const bubble = (
    <span
      ref={bubbleRef}
      role="tooltip"
      id={id}
      className={`info-tip-bubble${open && position !== null ? " is-open" : ""}`}
      style={position === null ? undefined : { top: position.top, left: position.left }}
    >
      {children}
    </span>
  );

  return (
    <span className="info-tip" onMouseEnter={show} onMouseLeave={hide}>
      <button
        ref={triggerRef}
        type="button"
        className="info-tip-trigger"
        aria-label={label}
        aria-describedby={id}
        onFocus={show}
        onBlur={hide}
        onKeyDown={(event) => {
          if (event.key === "Escape") hide();
        }}
      >
        i
      </button>
      {typeof document === "undefined" ? bubble : createPortal(bubble, document.body)}
    </span>
  );
}
