import { createContext, useContext, useEffect, useState, useSyncExternalStore, type ReactNode } from "react";
import { createAskSessionStore, type AskSessionStore, type AskSessionsSnapshot } from "./askSessions";

const AskSessionsContext = createContext<AskSessionStore | null>(null);

/**
 * One `askSessions.ts` store for the whole signed-in shell (`AppShell.tsx`), so the rail, the Ask
 * screen and the reply notifier all read the same sessions. Also tells the store when the tab comes
 * back into view, so a reply that landed on the open chat while the tab was hidden stops counting as
 * unread once the user can actually see it.
 */
export function AskSessionsProvider({ children }: { children: ReactNode }) {
  const [store] = useState(createAskSessionStore);

  useEffect(() => {
    const onVisibilityChange = () => store.markViewingSeen();
    document.addEventListener("visibilitychange", onVisibilityChange);
    window.addEventListener("focus", onVisibilityChange);
    return () => {
      document.removeEventListener("visibilitychange", onVisibilityChange);
      window.removeEventListener("focus", onVisibilityChange);
    };
  }, [store]);

  return <AskSessionsContext.Provider value={store}>{children}</AskSessionsContext.Provider>;
}

/**
 * The shell's store, or -- when a screen is rendered outside the shell (unit tests mount `AskRoute`
 * and `RailNav` on their own) -- a private one for that component, which behaves exactly like a
 * single-session screen.
 */
export function useAskSessionStore(): AskSessionStore {
  const shared = useContext(AskSessionsContext);
  const [local] = useState(() => (shared === null ? createAskSessionStore() : null));
  return shared ?? (local as AskSessionStore);
}

export function useAskSessionsSnapshot(store: AskSessionStore): AskSessionsSnapshot {
  return useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
}
