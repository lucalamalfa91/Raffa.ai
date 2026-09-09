import { useOutletContext } from "react-router-dom";

/**
 * What the app shell knows once, for every screen under it (ADR-024 V2 IA; R-WEB-02): whether the
 * "From your contracts" tier is lit (`kbReady`, at least one validated contract) and how many
 * validated contracts there are. `AppShell.tsx` fetches this through `useValidatedContractCount`
 * and hands it down the router `<Outlet context>`, so a screen that needs the same signal (the
 * Workspace & members tip, for instance) reads the shell's answer instead of re-fetching the
 * portfolio for a second, possibly different, verdict.
 */
export interface ShellOutletContext {
  kbReady: boolean;
  validatedContractCount: number;
}

/**
 * The shell context for the current screen, or `null` when the screen is rendered outside the
 * shell (unit tests mount routes directly) -- a caller treats `null` as "unknown" and falls back to
 * whatever it can derive from its own data, never as "not ready".
 */
export function useShellContext(): ShellOutletContext | null {
  const context = useOutletContext<ShellOutletContext | undefined>();
  return context ?? null;
}
