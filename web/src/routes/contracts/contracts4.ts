import type { Translate } from "../../i18n/translate";
import { useShellContext } from "../../components/shell/shellContext";
import { loadCurrentWorkspace } from "../signin/workspaceStore";

/**
 * AskRaffa 4.0 slice 4 helpers shared by Contratti, Contratto and Revisione.
 */

/** A server message (`{id, vars}`) or a text that is already final (the document's own words). */
export type ServerText = string | { id: string; vars?: unknown } | null | undefined;

/** Translates a server message; a plain string is shown as is. */
export function tx(t: Translate, text: ServerText | unknown): string {
  if (text == null) return "";
  if (typeof text === "string") return text;
  if (typeof text === "object" && "id" in (text as object) && typeof (text as { id: unknown }).id === "string") {
    const { id, vars } = text as { id: string; vars?: unknown };
    return t(id, (vars ?? undefined) as Parameters<Translate>[1]);
  }
  return "";
}

/**
 * "CHF 640k", and "CHF 2.4M" from a million up — the same rule as Risultati (`k` in
 * resultsViewModel.ts), so one amount reads the same on every screen (D9, PARITY.md).
 */
export function chfK(value: number): string {
  return value >= 1e6 ? `CHF ${(value / 1e6).toFixed(1)}M` : `CHF ${Math.round(value / 1000)}k`;
}

/** "1,500" — the prototype's `toLocaleString('en-US')` for contract quantities and prices. */
export function enNumber(value: number): string {
  return value.toLocaleString("en-US");
}

/** The notice deadline turns red this close (prototype: ≤45 days). */
export const URGENT_NOTICE_DAYS = 45;

/** The tenant id the screens call the API with (the shell's workspace, else the remembered one). */
export function useTenantId(): string | null {
  const shell = useShellContext();
  return shell?.workspaceId ?? loadCurrentWorkspace()?.id ?? null;
}

/** Fired after a contract is validated or a decision changes, so other screens (the Oggi badge) re-read. */
export const CONTRACTS_CHANGED_EVENT = "raffa4:contracts-changed";

export function announceContractsChanged(): void {
  if (typeof window !== "undefined") window.dispatchEvent(new Event(CONTRACTS_CHANGED_EVENT));
}
