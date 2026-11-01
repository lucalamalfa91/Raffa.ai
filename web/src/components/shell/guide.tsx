import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";

/**
 * Guida Raffa (AskRaffa 4.0 shell): "Dove sei · Cosa fare ora · Perché · Dopo" + one optional
 * recommended action. A screen states its guide with `useGuide({...})` (final, already-translated
 * text); when no screen does, the panel shows the screen's fallback copy (`guideFallback.ts`).
 * The Oggi badge is set the same way, with `useNavBadge()`.
 */
export interface GuideCta {
  label: string;
  onClick: () => void;
}

export interface GuideContent {
  where: string;
  now: string;
  why: string;
  next: string;
  cta?: GuideCta | null;
}

interface GuideState {
  content: GuideContent | null;
  open: boolean;
  setOpen: (open: boolean) => void;
  toggle: () => void;
  badge: number | null;
  setBadge: (count: number | null) => void;
  register: (id: number, content: GuideContent | null) => void;
  unregister: (id: number) => void;
}

const GuideContext = createContext<GuideState | null>(null);

/** Below this width the guide starts closed (DESIGN-GAPS A1); the toggle still opens it. */
export const GUIDE_AUTO_CLOSE_BELOW = 1100;

function initiallyOpen(): boolean {
  return typeof window === "undefined" || window.innerWidth >= GUIDE_AUTO_CLOSE_BELOW;
}

export function GuideProvider({ children }: { children: ReactNode }) {
  const [entries, setEntries] = useState<ReadonlyArray<readonly [number, GuideContent | null]>>([]);
  const [open, setOpen] = useState<boolean>(initiallyOpen);
  const [badge, setBadge] = useState<number | null>(null);

  const register = useCallback((id: number, content: GuideContent | null) => {
    setEntries((current) => {
      const index = current.findIndex(([key]) => key === id);
      if (index === -1) return [...current, [id, content] as const];
      // Same screen, new text: keep its place in the stack.
      return current.map((entry, i) => (i === index ? ([id, content] as const) : entry));
    });
  }, []);
  const unregister = useCallback((id: number) => setEntries((current) => current.filter(([key]) => key !== id)), []);

  const value = useMemo<GuideState>(
    () => ({
      // The most recently registered screen wins (a nested step over its screen).
      content: entries.length > 0 ? (entries[entries.length - 1]?.[1] ?? null) : null,
      open,
      setOpen,
      toggle: () => setOpen((o) => !o),
      badge,
      setBadge,
      register,
      unregister,
    }),
    [entries, open, badge, register, unregister],
  );
  return <GuideContext.Provider value={value}>{children}</GuideContext.Provider>;
}

let nextId = 1;

const NOOP_STATE: GuideState = {
  content: null,
  open: false,
  setOpen: () => undefined,
  toggle: () => undefined,
  badge: null,
  setBadge: () => undefined,
  register: () => undefined,
  unregister: () => undefined,
};

/** The guide panel's state (open/closed, content, badge); a no-op outside the shell. */
export function useGuideState(): GuideState {
  return useContext(GuideContext) ?? NOOP_STATE;
}

/**
 * States what Guida Raffa says while the calling screen is mounted. Pass `null` to fall back to the
 * screen's default copy. Re-registers only when the visible text changes; the CTA's latest
 * `onClick` is always the one called.
 */
export function useGuide(content: GuideContent | null): void {
  const { register, unregister } = useGuideState();
  const idRef = useRef<number>(0);
  if (idRef.current === 0) idRef.current = nextId++;
  const onClickRef = useRef<(() => void) | undefined>(content?.cta?.onClick);
  onClickRef.current = content?.cta?.onClick;

  const where = content?.where;
  const now = content?.now;
  const why = content?.why;
  const next = content?.next;
  const ctaLabel = content?.cta?.label;
  const isNull = content === null;

  useEffect(() => {
    const id = idRef.current;
    register(
      id,
      isNull
        ? null
        : {
            where: where ?? "",
            now: now ?? "",
            why: why ?? "",
            next: next ?? "",
            cta: ctaLabel ? { label: ctaLabel, onClick: () => onClickRef.current?.() } : null,
          },
    );
  }, [register, isNull, where, now, why, next, ctaLabel]);

  useEffect(() => {
    const id = idRef.current;
    return () => unregister(id);
  }, [unregister]);
}

/** The setter for the badge on the Oggi tab (the number of items for the current user; hidden at
 * 0/null). The value stays when the screen that set it unmounts. */
export function useNavBadge(): (count: number | null) => void {
  return useGuideState().setBadge;
}
