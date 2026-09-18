import type { CapabilityBody } from "../../api/client";
import type { WorkspaceRole } from "../shell/navItems";

/**
 * Global Ask bar copy, quoted from `raffa-v2/app.jsx`:
 *
 *   askPlaceholder: kbReady
 *     ? 'Ask Raffa — spend, dates, clauses, liability…'
 *     : 'Ask Raffa switches on after your first validated contract'
 *   chipsFor: { documents, portfolio, renewals, home, quote, ask, workspace }
 *   c360Chips: 'When must we give notice to '+cur.supplier+'?' …
 *   askChips: kbReady ? askChips : []
 *
 * One placeholder on every screen (the Day-1 per-route placeholders are gone). Suggestion text
 * still comes from `GET /api/capabilities` when the catalog has two examples for the screen;
 * otherwise the prototype's own `chipsFor` pair. Contract 360 has no catalog key — its chips stay
 * the prototype's supplier-templated pair, named with the open contract's real supplier.
 *
 * Task E27/F03/US01/T01 (NW-77; ADR-012 cl. 49 / ADR-020 §37.1 per `reports/architecture/waves/
 * w19.md`): `GlobalAskBar.tsx` now fetches `getContract360` itself for the open contract's
 * `header.supplierName` -- the same read `routes/ask/askViewModel.ts#buildScopedSuggestions`
 * already does for the Ask screen's own scoped chips -- and passes it into `getAskBarCopy` below
 * (`c360Chips`). "This supplier" only survives as the fallback while that fetch is in flight,
 * failed, or the contract genuinely has none; Portfolio/Ask-home (and every other screen) never
 * receive a supplier name at all, so they are unaffected by construction (AC-3).
 *
 * Task E25/F01/US01/T01 (ADR-022 S16-11, ADR-012 w17 cl 40): a catalog entry whose own `roleGate`
 * is not `"any"` (today, only `workspace-members` — `CapabilityCatalog.cs`) never surfaces its
 * `exampleQuestions` as chips to a non-Admin — it falls back to the same static `CHIPS_FOR` pair
 * used while the catalog has not loaded yet, never an admin action a Procurement caller cannot
 * take. `GET /api/capabilities` itself stays un-gated and identical for both roles (AC-2) — this
 * is presentation only, never a security control (the same rule `workspaceRole.ts`'s own header
 * comment states for the rail).
 */
export interface AskBarCopy {
  placeholder: string;
  suggestions: readonly string[];
}

/** `app.jsx` `askPlaceholder` while `kbReady`. */
const READY_PLACEHOLDER = "Ask Raffa — spend, dates, clauses, liability…";

/** `app.jsx` `askPlaceholder` while `kbOff`. */
const KB_OFF_PLACEHOLDER = "Ask Raffa switches on after your first validated contract";

type AskBarScreen = "documents" | "portfolio" | "renewals" | "home" | "quote" | "ask" | "workspace" | "c360";

/** `app.jsx` `chipsFor` + `c360Chips` (supplier unknown → "this supplier"). */
const CHIPS_FOR: Readonly<Record<AskBarScreen, readonly [string, string]>> = {
  documents: ["Which documents are not askable yet?", "Which fields still lack confidence?"],
  portfolio: ["Which of these have uncapped liability?", "Which contracts renew in the next 120 days?"],
  renewals: ["Why is this at the top?", "Which should we start first?"],
  home: ["Where is the largest saving still in review?", "Which contracts renew in the next 120 days?"],
  quote: ["How does this compare with our Snowflake contract?", "Which contracts renew in the next 120 days?"],
  ask: ["When does Salesforce expire?", "What liabilities do we have?"],
  workspace: ["When does Salesforce expire?", "What liabilities do we have?"],
  c360: ["When must we give notice to this supplier?", "What is our liability cap with this supplier?"],
};

const CAPABILITY_KEY_BY_SCREEN: Readonly<Record<AskBarScreen, string | null>> = {
  documents: "documents",
  portfolio: "portfolio",
  renewals: "renewals",
  home: "savings",
  quote: "quote-check",
  ask: "ask",
  workspace: "workspace-members",
  c360: null,
};

export function screenForPath(pathname: string): AskBarScreen {
  if (pathname.startsWith("/documents") || pathname.startsWith("/review") || /\/contracts\/[^/]+\/review(?:\/|$)/.test(pathname)) {
    return "documents";
  }
  if (/^\/contracts\/[^/]+/.test(pathname)) return "c360";
  if (pathname.startsWith("/contracts")) return "portfolio";
  if (pathname.startsWith("/renewals")) return "renewals";
  if (pathname.startsWith("/quotes")) return "quote";
  if (pathname.startsWith("/savings")) return "home";
  if (pathname.startsWith("/workspace")) return "workspace";
  return "ask";
}

/**
 * Task E27/F03/US01/T01 (NW-77, AC-1): the `:contractId` half of `screenForPath`'s own c360 test,
 * for `GlobalAskBar.tsx#submit` to scope the new conversation's navigation (`/ask?scope=<id>`) --
 * `null` on every screen `screenForPath` does not resolve to `"c360"`, which already excludes
 * Portfolio (`/contracts`, no id), the review sub-route (`/contracts/:id/review`) and every other
 * screen. One predicate decides both "is this Contract 360" and "which contract", so the bar's
 * navigation and this module's own c360 chip lookup (`c360Chips` below) can never disagree about
 * which contract is open.
 */
export function contractIdForPath(pathname: string): string | null {
  if (screenForPath(pathname) !== "c360") return null;
  const match = /^\/contracts\/([^/]+)/.exec(pathname);
  return match ? match[1] : null;
}

/**
 * `app.jsx` `c360Chips`, supplier-named (task E27/F03/US01/T01, NW-77, AC-2) -- the same
 * construction `routes/ask/askViewModel.ts#buildScopedSuggestions` already gives the Ask screen's
 * own scoped chips from the identical `getContract360` read, duplicated here rather than imported
 * (independent, separately-evolving screens; the same convention that file's own header comment
 * states for `isChipVisibleForRole`/`suggestionsFromCapabilityCatalog`). `supplierName` null or
 * blank keeps `CHIPS_FOR.c360`'s own "this supplier" wording verbatim -- the bar has not resolved
 * the open contract yet, the fetch failed, or the contract genuinely has none.
 */
function c360Chips(supplierName: string | null): readonly [string, string] {
  if (supplierName === null || supplierName.trim() === "") return CHIPS_FOR.c360;
  const supplier = supplierName.trim();
  return [`When must we give notice to ${supplier}?`, `What is our liability cap with ${supplier}?`];
}

export function getAskBarCopy(
  pathname: string,
  kbReady: boolean,
  capabilities: readonly CapabilityBody[] | null | undefined,
  role: WorkspaceRole,
  /** Task E27/F03/US01/T01 (NW-77, AC-2/AC-3): the open contract's real supplier name, read by
   * `GlobalAskBar.tsx` (see this file's own header comment) -- optional and ignored on every screen
   * but `"c360"`, so Portfolio/Ask-home chips stay unscoped (AC-3) even if a caller passed one. */
  supplierName?: string | null,
): AskBarCopy {
  const placeholder = kbReady ? READY_PLACEHOLDER : KB_OFF_PLACEHOLDER;
  if (!kbReady) return { placeholder, suggestions: [] };

  const screen = screenForPath(pathname);
  if (screen === "c360") {
    return { placeholder, suggestions: c360Chips(supplierName ?? null) };
  }

  const catalogKey = CAPABILITY_KEY_BY_SCREEN[screen];
  const catalogSuggestions =
    catalogKey === null ? null : suggestionsFromCapabilityCatalog(capabilities ?? null, catalogKey, role);

  return { placeholder, suggestions: catalogSuggestions ?? CHIPS_FOR[screen] };
}

/**
 * `capabilities[key].exampleQuestions`, first two -- `null` when `capabilities` has not loaded yet,
 * has no entry for `key`, that entry has fewer than two example questions (never a single-chip
 * row; the caller's own static fallback covers that instead), or the entry's own `roleGate` is not
 * `"any"` and `role` is not `"admin"` (task E25/F01/US01/T01, AC-1) -- the caller's static fallback
 * covers that case too, so a non-Admin still sees two generic, role-safe chips rather than none.
 */
export function suggestionsFromCapabilityCatalog(
  capabilities: readonly CapabilityBody[] | null,
  key: string,
  role: WorkspaceRole,
): readonly [string, string] | null {
  if (capabilities === null) return null;
  const match = capabilities.find((capability) => capability.key === key);
  if (!match || match.exampleQuestions.length < 2) return null;
  if (match.roleGate !== "any" && role !== "admin") return null;
  return [match.exampleQuestions[0], match.exampleQuestions[1]];
}
