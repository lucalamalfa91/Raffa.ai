import { useCallback, useState } from "react";

/** The rail's width in px: the mockup's 232 by default, draggable between these bounds. */
export const RAIL_WIDTH_DEFAULT = 232;
export const RAIL_WIDTH_MIN = 200;
export const RAIL_WIDTH_MAX = 440;

/** Per-browser convenience only (never state the app depends on), so localStorage is enough. */
const STORAGE_KEY = "raffa.shell.railWidth";

export function clampRailWidth(width: number): number {
  if (!Number.isFinite(width)) return RAIL_WIDTH_DEFAULT;
  return Math.round(Math.min(RAIL_WIDTH_MAX, Math.max(RAIL_WIDTH_MIN, width)));
}

function readStoredWidth(): number {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return stored === null ? RAIL_WIDTH_DEFAULT : clampRailWidth(Number(stored));
  } catch {
    return RAIL_WIDTH_DEFAULT;
  }
}

function storeWidth(width: number): void {
  try {
    if (width === RAIL_WIDTH_DEFAULT) window.localStorage.removeItem(STORAGE_KEY);
    else window.localStorage.setItem(STORAGE_KEY, String(width));
  } catch {
    // Private windows and blocked storage: the width simply resets on the next visit.
  }
}

export interface RailWidth {
  width: number;
  /** Live, while dragging -- not remembered yet. */
  resize: (width: number) => void;
  /** The drag (or key press) is over: keep this width for the next visit. */
  commit: (width: number) => void;
  /** Back to the default width, forgotten. */
  reset: () => void;
}

/**
 * The shell rail's width (`AppShell.tsx` sets it as `--shell-rail-width`), remembered per browser
 * so a rail widened to read long chat names stays that way across visits.
 */
export function useRailWidth(): RailWidth {
  const [width, setWidth] = useState(readStoredWidth);

  const resize = useCallback((next: number) => setWidth(clampRailWidth(next)), []);
  const commit = useCallback((next: number) => {
    const clamped = clampRailWidth(next);
    setWidth(clamped);
    storeWidth(clamped);
  }, []);
  const reset = useCallback(() => {
    setWidth(RAIL_WIDTH_DEFAULT);
    storeWidth(RAIL_WIDTH_DEFAULT);
  }, []);

  return { width, resize, commit, reset };
}
