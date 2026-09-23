import { useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import type { TipCopy } from "./infoTipCopy";
import "./InfoTip.css";

const GAP = 8;
const EDGE = 12;

/** Per-browser convenience only (never state the app depends on), so localStorage is enough. */
const SEEN_GUIDES_KEY = "raffa.tips.seenGuides";

function readSeenGuides(): ReadonlySet<string> {
  try {
    const stored = window.localStorage.getItem(SEEN_GUIDES_KEY);
    const parsed: unknown = stored === null ? [] : JSON.parse(stored);
    return new Set(Array.isArray(parsed) ? parsed.filter((key): key is string => typeof key === "string") : []);
  } catch {
    return new Set();
  }
}

function hasSeenGuide(key: string): boolean {
  return readSeenGuides().has(key);
}

function markGuideSeen(key: string): void {
  try {
    const seen = readSeenGuides();
    if (seen.has(key)) return;
    window.localStorage.setItem(SEEN_GUIDES_KEY, JSON.stringify([...seen, key]));
  } catch {
    // Private windows and blocked storage: the guide simply stays highlighted on the next visit.
  }
}

export interface InfoTipProps {
  /** The trigger's accessible name ("What the statuses mean"); the bubble is its description. */
  label: string;
  children: ReactNode;
  align?: "start" | "end";
  /**
   * A screen guide (`ScreenTitle.tsx`): a larger "i" beside a screen's title that stays inked in
   * the accent until it has been opened once in this browser, so a newcomer sees where to start.
   * The value is the key it is remembered by.
   */
  guideKey?: string;
}

/**
 * A quiet "i" next to a label that explains, on hover, tap or keyboard focus, what a figure or a
 * control means -- so the screen keeps only the figures, and the explanation is one glance away,
 * never in the way. The bubble is portalled to `document.body` and placed with fixed coordinates,
 * so a scrolling table never clips it; it opens below the "i" (above when there is no room) and
 * stays inside the viewport. It is also the trigger's accessible description (`aria-describedby`),
 * always in the DOM. The "i" itself is drawn by CSS, so it never joins the label's text content.
 */
export default function InfoTip({ label, children, align = "start", guideKey }: InfoTipProps) {
  const id = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const bubbleRef = useRef<HTMLSpanElement>(null);
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState<{ top: number; left: number } | null>(null);
  const [unseen, setUnseen] = useState(() => guideKey !== undefined && !hasSeenGuide(guideKey));

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

  // Touch screens have no hover and do not always move focus: a tap opens the bubble (the trigger's
  // click), a tap anywhere else closes it.
  useEffect(() => {
    if (!open) return;
    const closeOnOutsidePress = (event: PointerEvent) => {
      if (triggerRef.current?.contains(event.target as Node)) return;
      setOpen(false);
      setPosition(null);
    };
    document.addEventListener("pointerdown", closeOnOutsidePress);
    return () => document.removeEventListener("pointerdown", closeOnOutsidePress);
  }, [open]);

  const show = () => {
    setOpen(true);
    if (guideKey !== undefined && unseen) {
      setUnseen(false);
      markGuideSeen(guideKey);
    }
  };
  const hide = () => {
    setOpen(false);
    setPosition(null);
  };

  const guide = guideKey !== undefined;
  const bubble = (
    <span
      ref={bubbleRef}
      role="tooltip"
      id={id}
      className={`info-tip-bubble${guide ? " info-tip-bubble--guide" : ""}${open && position !== null ? " is-open" : ""}`}
      style={position === null ? undefined : { top: position.top, left: position.left }}
    >
      {children}
    </span>
  );

  return (
    <span className={`info-tip${guide ? " info-tip--guide" : ""}`} onMouseEnter={show} onMouseLeave={hide}>
      <button
        ref={triggerRef}
        type="button"
        className={`info-tip-trigger${unseen ? " is-unseen" : ""}`}
        aria-label={label}
        aria-describedby={id}
        onFocus={show}
        onBlur={hide}
        onClick={show}
        onKeyDown={(event) => {
          if (event.key === "Escape") hide();
        }}
      />
      {typeof document === "undefined" ? bubble : createPortal(bubble, document.body)}
    </span>
  );
}

/** An InfoTip whose words come from the catalogue (`infoTipCopy.ts`): one paragraph per line, the small print last. */
export function CopyTip({ tip, align, guideKey }: { tip: TipCopy; align?: "start" | "end"; guideKey?: string }) {
  return (
    <InfoTip label={tip.label} align={align} guideKey={guideKey}>
      {tip.lines.map((line) => (
        <p key={line}>{line}</p>
      ))}
      {tip.meta !== undefined && <p className="info-tip-meta">{tip.meta}</p>}
    </InfoTip>
  );
}
