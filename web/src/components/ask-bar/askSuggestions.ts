import type { CapabilityBody } from "../../api/client";

/**
 * Global Ask bar copy, quoted from `contigo-v2/app.jsx`:
 *
 *   askPlaceholder: kbReady
 *     ? 'Ask Contigo — spend, dates, clauses, liability…'
 *     : 'Ask Contigo switches on after your first validated contract'
 *   chipsFor: { documents, portfolio, renewals, home, quote, ask, workspace }
 *   c360Chips: 'When must we give notice to '+cur.supplier+'?' …
 *   askChips: kbReady ? askChips : []
 *
 * One placeholder on every screen (the Day-1 per-route placeholders are gone). Suggestion text
 * still comes from `GET /api/capabilities` when the catalog has two examples for the screen;
 * otherwise the prototype's own `chipsFor` pair. Contract 360 has no catalog key — its chips stay
 * the prototype's supplier-templated pair, with "this supplier" when the bar has not loaded the
 * contract (the bar does not fetch).
 */
export interface AskBarCopy {
  placeholder: string;
  suggestions: readonly string[];
}

/** `app.jsx` `askPlaceholder` while `kbReady`. */
const READY_PLACEHOLDER = "Ask Contigo — spend, dates, clauses, liability…";

/** `app.jsx` `askPlaceholder` while `kbOff`. */
const KB_OFF_PLACEHOLDER = "Ask Contigo switches on after your first validated contract";

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

export function getAskBarCopy(
  pathname: string,
  kbReady: boolean,
  capabilities?: readonly CapabilityBody[] | null,
): AskBarCopy {
  const placeholder = kbReady ? READY_PLACEHOLDER : KB_OFF_PLACEHOLDER;
  if (!kbReady) return { placeholder, suggestions: [] };

  const screen = screenForPath(pathname);
  const catalogKey = CAPABILITY_KEY_BY_SCREEN[screen];
  const catalogSuggestions =
    catalogKey === null ? null : suggestionsFromCapabilityCatalog(capabilities ?? null, catalogKey);

  return { placeholder, suggestions: catalogSuggestions ?? CHIPS_FOR[screen] };
}

/**
 * `capabilities[key].exampleQuestions`, first two -- `null` when `capabilities` has not loaded yet,
 * has no entry for `key`, or that entry has fewer than two example questions (never a single-chip
 * row; the caller's own static fallback covers that instead).
 */
export function suggestionsFromCapabilityCatalog(
  capabilities: readonly CapabilityBody[] | null,
  key: string,
): readonly [string, string] | null {
  if (capabilities === null) return null;
  const match = capabilities.find((capability) => capability.key === key);
  if (!match || match.exampleQuestions.length < 2) return null;
  return [match.exampleQuestions[0], match.exampleQuestions[1]];
}
